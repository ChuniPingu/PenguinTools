using System.Buffers.Binary;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class AirSlideArrowColorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeArrowAndBodyColors_SurviveBinaryReadWrite(bool arrowInverted, bool bodyInverted)
    {
        var input = TestTempPaths.Create(".mgxc");
        var output = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new(input, Chart(Color.DEF, null))).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            var (arrow, body) = Positions(bytes);
            bytes[arrow + 3] = (byte)(arrowInverted ? 1 : 0);
            bytes[body + 3] = (byte)(bodyInverted ? 1 : 0);
            await File.WriteAllBytesAsync(input, bytes, ct);

            // Native bytes are changed after writing, so saved appearance
            // metadata cannot hide a parser error for either inverted case.
            var parsed = await new MgxcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var slide = Assert.Single(parsed.Value!.Notes.Children.OfType<U.AirSlide>());
            Assert.Equal(ColorValue(arrowInverted), slide.ArrowColor ?? slide.Color);
            Assert.Equal(ColorValue(bodyInverted), slide.Color);
            if (arrowInverted || bodyInverted)
            {
                Assert.False(parsed.Value.Extras.BinarySnapshotValid);
            }

            var written = await new MgxcChartWriter(new(output, parsed.Value)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            Assert.DoesNotContain(written.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Warn_Air_arrow_color_not_reproduced);
            AssertNativeColors(await File.ReadAllBytesAsync(output, ct), arrowInverted, bodyInverted);

            var converted = new C2SChartConverter(new(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(ColorValue(bodyInverted), Assert.Single(converted.Value!.Notes.OfType<C.AirSlide>()).Color);
            Assert.Empty(converted.Value.Notes.OfType<C.Air>());
            Assert.Equal(arrowInverted != bodyInverted, converted.Diagnostics.Diagnostics.Any(
                d => d.Message.Key == MsgKeys.Warn_Air_arrow_color_not_reproduced));
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Ugc_RetainsArrowColorInValidatedMetadata(bool arrowInverted, bool bodyInverted)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        var source = Chart(ColorValue(bodyInverted), ColorValue(arrowInverted));
        try
        {
            var written = await new UgcChartWriter(input, source).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            Assert.Equal(arrowInverted != bodyInverted, written.Diagnostics.Diagnostics.Any(
                d => d.Message.Key == MsgKeys.Warn_Air_arrow_color_not_reproduced));
            var lines = await File.ReadAllLinesAsync(input, ct);
            var nativeHead = Assert.Single(lines, line => line.Contains(":S", StringComparison.Ordinal));
            Assert.EndsWith(bodyInverted ? "I" : "N", nativeHead);
            Assert.DoesNotContain(lines, line => line.Contains(":a", StringComparison.Ordinal));

            var parsed = await new UgcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var slide = Assert.Single(parsed.Value!.Notes.Children.OfType<U.AirSlide>());
            Assert.Equal(ColorValue(arrowInverted), slide.ArrowColor);
            Assert.Equal(ColorValue(bodyInverted), slide.Color);
            Assert.True((await new MgxcChartWriter(new(output, parsed.Value)).WriteAsync(ct)).Succeeded);
            AssertNativeColors(await File.ReadAllBytesAsync(output, ct), arrowInverted, bodyInverted);
            Assert.Equal(ColorValue(arrowInverted), Assert.Single(source.Notes.Children.OfType<U.AirSlide>()).ArrowColor);
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UgcEdits_RestoreOnlyUnchangedNativeColors(bool editBody)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(input, Chart(Color.DEF, Color.PNK)).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(input, ct);
            var index = Array.FindIndex(lines, line => editBody
                ? line.Contains(":S", StringComparison.Ordinal)
                : line.StartsWith("@TITLE\t", StringComparison.Ordinal));
            Assert.True(index >= 0);
            lines[index] = editBody ? lines[index][..^1] + "I" : "@TITLE\tEdited title";
            await File.WriteAllLinesAsync(input, lines, ct);

            var parsed = await new UgcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var slide = Assert.Single(parsed.Value!.Notes.Children.OfType<U.AirSlide>());
            Assert.Equal(editBody ? Color.PNK : Color.DEF, slide.Color);
            if (editBody)
            {
                Assert.Null(slide.ArrowColor);
            }
            else
            {
                Assert.Equal(Color.PNK, slide.ArrowColor);
                Assert.Equal("Edited title", parsed.Value.Meta.Title);
            }
            Assert.True((await new MgxcChartWriter(new(output, parsed.Value)).WriteAsync(ct)).Succeeded);
            AssertNativeColors(await File.ReadAllBytesAsync(output, ct), true, editBody);
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BinaryColorEdits_AreNotOverwrittenBySavedAppearance(bool editArrow)
    {
        var input = TestTempPaths.Create(".mgxc");
        var output = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new(input, Chart(Color.DEF, Color.PNK))).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            var (arrow, body) = Positions(bytes);
            bytes[(editArrow ? arrow : body) + 3] = (byte)(editArrow ? 0 : 1);
            await File.WriteAllBytesAsync(input, bytes, ct);

            var parsed = await new MgxcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.False(parsed.Value!.Extras.BinarySnapshotValid);
            var slide = Assert.Single(parsed.Value.Notes.Children.OfType<U.AirSlide>());
            Assert.Equal(editArrow ? Color.DEF : Color.PNK, slide.ArrowColor);
            Assert.Equal(editArrow ? Color.DEF : Color.PNK, slide.Color);
            Assert.True((await new MgxcChartWriter(new(output, parsed.Value)).WriteAsync(ct)).Succeeded);
            AssertNativeColors(await File.ReadAllBytesAsync(output, ct), !editArrow, !editArrow);
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    private static U.Chart Chart(Color body, Color? arrow)
    {
        var chart = new U.Chart();
        chart.Meta.Title = "Arrow colors";
        chart.Meta.SortName = "ARROWCOLORS";
        chart.Meta.MainBpm = 120;
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0 });
        var tap = new U.Tap { Tick = 480, Lane = 2, Width = 4 };
        var slide = new U.AirSlide { Color = body, ArrowColor = arrow, Height = 80 };
        slide.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 4, Width = 4, Height = 160 });
        tap.MakePair(slide);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(slide);
        return chart;
    }

    private static Color ColorValue(bool inverted) => inverted ? Color.PNK : Color.DEF;

    private static void AssertNativeColors(byte[] bytes, bool arrowInverted, bool bodyInverted)
    {
        var (arrow, body) = Positions(bytes);
        Assert.Equal(arrowInverted ? 1 : 0, bytes[arrow + 3]);
        Assert.Equal(bodyInverted ? 1 : 0, bytes[body + 3]);
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
                var body = Assert.Single(records, i => bytes[i] == 9 && bytes[i + 1] == 1);
                return (arrow, body);
            }
            position += 8 + size;
        }
        throw new InvalidOperationException("Synthetic MGXC dat2 block is missing.");
    }
}
