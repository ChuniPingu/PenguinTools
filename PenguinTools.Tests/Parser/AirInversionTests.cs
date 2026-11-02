using System.Buffers.Binary;
using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core.Diagnostic;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class AirInversionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task ArrowInversionAppliesToTheWholeLongNote(bool slide, bool inverted, bool longRecordFlag)
    {
        var input = TestTempPaths.Create(".mgxc");
        var binary = TestTempPaths.Create(".mgxc");
        var ugc = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new(input, Chart(slide, Color.DEF))).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            var (arrow, body) = Positions(bytes);
            bytes[arrow + 3] = (byte)(inverted ? 1 : 0);
            bytes[body + 3] = (byte)(longRecordFlag ? 1 : 0);
            await File.WriteAllBytesAsync(input, bytes, ct);

            var parsed = await new MgxcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var expected = inverted ? Color.PNK : Color.DEF;
            AssertColor(parsed.Value!, slide, expected);

            var written = await new MgxcChartWriter(new(binary, parsed.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            Assert.DoesNotContain(written.Diagnostics.Diagnostics, d => d.Severity == Severity.Warning);
            AssertBinaryInversion(await File.ReadAllBytesAsync(binary, ct), inverted);

            var converted = new C2SChartConverter(new(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.DoesNotContain(converted.Diagnostics.Diagnostics, d => d.Severity == Severity.Warning);
            var colors = slide
                ? converted.Value!.Notes.OfType<C.AirSlide>().Select(n => n.Color).ToArray()
                : converted.Value!.Notes.OfType<C.AirHold>().Select(n => n.Color).ToArray();
            Assert.Equal(2, colors.Length);
            Assert.All(colors, color => Assert.Equal(expected, color));
            Assert.Empty(converted.Value.Notes.OfType<C.Air>());

            var exported = await new UgcChartWriter(ugc, parsed.Value!).WriteAsync(ct);
            Assert.True(exported.Succeeded, exported.ToString());
            Assert.DoesNotContain(exported.Diagnostics.Diagnostics, d => d.Severity == Severity.Warning);
            var lines = await File.ReadAllLinesAsync(ugc, ct);
            Assert.EndsWith(inverted ? "I" : "N",
                Assert.Single(lines, line => line.Contains(slide ? ":S" : ":H", StringComparison.Ordinal)));
            // Read only native UGC data: whole-note inversion must not depend
            // on PenguinTools metadata restoring a different color afterwards.
            await File.WriteAllLinesAsync(ugc, lines.Where(line =>
                !line.StartsWith("@COPYRIGHT", StringComparison.Ordinal) &&
                !line.StartsWith("@BOOKMARK", StringComparison.Ordinal) && !line.StartsWith('\'')), ct);
            var native = await new UgcParser(new(ugc, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(native.Succeeded, native.ToString());
            Assert.Empty(native.Value!.Extras.AirAppearances);
            AssertColor(native.Value, slide, expected);
            Assert.True((await new MgxcChartWriter(new(binary, native.Value)).WriteAsync(ct)).Succeeded);
            AssertBinaryInversion(await File.ReadAllBytesAsync(binary, ct), inverted);
        }
        finally { File.Delete(input); File.Delete(binary); File.Delete(ugc); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UgcEditsRespectWholeNoteInversion(bool editColor)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(input, Chart(true, Color.PNK)).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(input, ct);
            var index = Array.FindIndex(lines, line => editColor
                ? line.Contains(":S", StringComparison.Ordinal)
                : line.StartsWith("@TITLE\t", StringComparison.Ordinal));
            Assert.True(index >= 0);
            lines[index] = editColor ? lines[index][..^1] + "N" : "@TITLE\tEdited title";
            await File.WriteAllLinesAsync(input, lines, ct);

            var parsed = await new UgcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            AssertColor(parsed.Value!, true, editColor ? Color.DEF : Color.PNK);
            if (!editColor)
            {
                Assert.Equal("Edited title", parsed.Value!.Meta.Title);
            }
            Assert.True((await new MgxcChartWriter(new(output, parsed.Value!)).WriteAsync(ct)).Succeeded);
            AssertBinaryInversion(await File.ReadAllBytesAsync(output, ct), !editColor);
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BinaryEditsUseTheArrowInsteadOfSavedAppearanceOrLongRecord(bool editArrow)
    {
        var input = TestTempPaths.Create(".mgxc");
        var output = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new(input, Chart(true, Color.PNK))).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            var (arrow, body) = Positions(bytes);
            bytes[(editArrow ? arrow : body) + 3] = 0;
            await File.WriteAllBytesAsync(input, bytes, ct);

            var parsed = await new MgxcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.False(parsed.Value!.Extras.BinarySnapshotValid);
            AssertColor(parsed.Value, true, editArrow ? Color.DEF : Color.PNK);
            Assert.True((await new MgxcChartWriter(new(output, parsed.Value)).WriteAsync(ct)).Succeeded);
            AssertBinaryInversion(await File.ReadAllBytesAsync(output, ct), !editArrow);
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlierAppearanceMetadataUsesArrowColorForTheWholeLongNote(bool inverted)
    {
        var chart = Chart(true, Color.DEF);
        var slide = Assert.Single(chart.Notes.Children.OfType<U.AirSlide>());
        var expected = inverted ? Color.PNK : Color.DEF;
        chart.Extras.AirAppearances.Add(new(ChartExtras.AirKey(slide), inverted ? Color.DEF : Color.PNK, slide.Direction)
        { ArrowColor = expected });

        chart.Extras.RestoreAppearance(chart);

        Assert.Equal(expected, slide.Color);
    }

    private static U.Chart Chart(bool slide, Color color)
    {
        var chart = new U.Chart();
        chart.Meta.Title = "AIR inversion";
        chart.Meta.SortName = "AIRINVERSION";
        chart.Meta.MainBpm = 120;
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0 });
        var tap = new U.Tap { Tick = 480, Lane = 2, Width = 4 };
        U.NegativeNote air = slide ? new U.AirSlide { Color = color, Height = 80 } : new U.AirHold { Color = color };
        if (slide)
        {
            air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 4, Width = 4, Height = 160 });
            air.AppendChild(new U.AirSlideJoint { Tick = 1440, Lane = 6, Width = 4, Height = 80 });
        }
        else
        {
            air.AppendChild(new U.AirHoldJoint { Tick = 960 });
            air.AppendChild(new U.AirHoldJoint { Tick = 1440 });
        }
        tap.MakePair(air);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        return chart;
    }

    private static void AssertColor(U.Chart chart, bool slide, Color expected)
    {
        var color = slide ? Assert.Single(chart.Notes.Children.OfType<U.AirSlide>()).Color
            : Assert.Single(chart.Notes.Children.OfType<U.AirHold>()).Color;
        Assert.Equal(expected, color);
    }

    private static void AssertBinaryInversion(byte[] bytes, bool inverted)
    {
        var (arrow, body) = Positions(bytes);
        Assert.Equal(inverted ? 1 : 0, bytes[arrow + 3]);
        Assert.Equal(inverted ? 1 : 0, bytes[body + 3]);
    }

    private static (int Arrow, int Body) Positions(byte[] bytes)
    {
        Assert.True(bytes.AsSpan(0, 4).SequenceEqual("MGXC"u8));
        for (var position = 12; position < bytes.Length;)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position + 4));
            if (bytes.AsSpan(position, 4).SequenceEqual("dat2"u8))
            {
                var records = Enumerable.Range(0, size / 20).Select(i => position + 8 + i * 20).ToArray();
                var arrow = Assert.Single(records, i => bytes[i] == 7);
                var body = Assert.Single(records, i => bytes[i] is 8 or 9 && bytes[i + 1] == 1);
                return (arrow, body);
            }
            position += 8 + size;
        }
        throw new InvalidOperationException("Synthetic MGXC dat2 block is missing.");
    }
}
