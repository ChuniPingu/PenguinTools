using System.Buffers.Binary;
using System.Globalization;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class EditorNumericProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrecisionMetadata_RestoresOnlyUneditedNumericGroups(bool ugc)
    {
        const decimal original = 239.99999237060546874999999999m;
        var chart = new U.Chart();
        chart.Meta.MainBpm = original;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = original });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0 });
        chart.Notes.AppendChild(new U.Tap { Tick = 480, Lane = 0, Width = 4 });
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = ugc
                ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                : await new MgxcChartWriter(new(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded);
            Assert.Equal(original, chart.Meta.MainBpm);
            Assert.Equal(original, Assert.Single(chart.Events.Children.OfType<U.BpmEvent>()).Bpm);
            Assert.Empty(chart.Extras.EditorNumberProjections);
            double native;
            if (ugc)
            {
                var bpm = (await File.ReadAllLinesAsync(path, ct)).Single(l => l.StartsWith("@BPM\t", StringComparison.Ordinal));
                native = double.Parse(bpm.Split('\t')[2], CultureInfo.InvariantCulture);
            }
            else
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                native = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(BpmValueOffset(bytes)));
            }
            Assert.Equal(float.Parse(original.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), (float)native);
            var restored = await Read();
            Assert.True(restored.Succeeded);
            Assert.Equal(original, restored.Value!.Meta.MainBpm);
            Assert.Equal(original, Assert.Single(restored.Value.Events.Children.OfType<U.BpmEvent>()).Bpm);

            if (ugc)
            {
                var lines = await File.ReadAllLinesAsync(path, ct);
                var index = Array.FindIndex(lines, l => l.StartsWith("@BPM\t", StringComparison.Ordinal));
                lines[index] = "@BPM\t0'0\t120.00000381469727";
                await File.WriteAllLinesAsync(path, lines, ct);
            }
            else
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(BpmValueOffset(bytes)), 120.00000381469727);
                await File.WriteAllBytesAsync(path, bytes, ct);
            }

            var edited = await Read();
            Assert.True(edited.Succeeded);
            var value = Assert.Single(edited.Value!.Events.Children.OfType<U.BpmEvent>()).Bpm;
            Assert.Equal(120f, float.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
            Assert.Equal(original, edited.Value.Meta.MainBpm);
        }
        finally
        {
            File.Delete(path);
        }

        Task<OperationResult<U.Chart>> Read() => ugc
            ? new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
            : new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
    }

    [Fact]
    public async Task PrecisionMetadata_RespectsSpeedAndPositionEditsWithoutChangingOtherGroups()
    {
        const decimal original = 1.0000000596046447753906249999m;
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        chart.Meta.Title = "Original title";
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0 });
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Timeline = 0, Speed = original });
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 480, Timeline = 1, Speed = original });
        chart.Events.AppendChild(new U.NoteSpeedEvent { Tick = 0, Speed = original });
        chart.Notes.AppendChild(new U.Tap { Tick = 960, Lane = 0, Width = 4 });
        chart.Notes.AppendChild(new U.Tap { Tick = 1440, Timeline = 1, Lane = 8, Width = 4 });
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new UgcChartWriter(path, chart).WriteAsync(ct);
            Assert.True(written.Succeeded);
            Assert.All(chart.Events.Children.OfType<U.SpeedEventBase>(), e => Assert.Equal(original, e.Speed));
            Assert.Empty(chart.Extras.EditorNumberProjections);
            var lines = await File.ReadAllLinesAsync(path, ct);
            lines[Array.FindIndex(lines, l => l.StartsWith("@TITLE\t", StringComparison.Ordinal))] = "@TITLE\tEdited title";
            await File.WriteAllLinesAsync(path, lines, ct);
            var titleEdit = await Read();
            Assert.Equal("Edited title", titleEdit.Meta.Title);
            Assert.All(titleEdit.Events.Children.OfType<U.SpeedEventBase>(), e => Assert.Equal(original, e.Speed));

            lines[Array.FindIndex(lines, l => l.StartsWith("@TIL\t0\t", StringComparison.Ordinal))] = "@TIL\t0\t0'0\t2";
            await File.WriteAllLinesAsync(path, lines, ct);
            var speedEdit = await Read();
            Assert.Equal(2m, speedEdit.Events.Children.OfType<U.ScrollSpeedEvent>().Single(e => e.Timeline == 0).Speed);
            Assert.NotEqual(original, speedEdit.Events.Children.OfType<U.ScrollSpeedEvent>().Single(e => e.Timeline == 1).Speed);
            Assert.Equal(original, Assert.Single(speedEdit.Events.Children.OfType<U.NoteSpeedEvent>()).Speed);

            var index = Array.FindIndex(lines, l => l.StartsWith("@SPDMOD\t", StringComparison.Ordinal));
            var fields = lines[index].Split('\t');
            fields[1] = "0'480";
            lines[index] = string.Join('\t', fields);
            await File.WriteAllLinesAsync(path, lines, ct);
            var positionEdit = Assert.Single((await Read()).Events.Children.OfType<U.NoteSpeedEvent>());
            Assert.Equal(480, positionEdit.Tick.Original);
            Assert.NotEqual(original, positionEdit.Speed);
        }
        finally
        {
            File.Delete(path);
        }

        async Task<U.Chart> Read()
        {
            var result = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(result.Succeeded);
            return result.Value!;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtremeDecimal_WarnsAndWritesAnAdjacentFiniteValue(bool negative)
    {
        var original = negative ? decimal.MinValue : decimal.MaxValue;
        var chart = new U.Chart();
        chart.Meta.MainBpm = original;
        var ct = TestContext.Current.CancellationToken;
        foreach (var ugc in new[] { false, true })
        {
            var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
            try
            {
                await File.WriteAllTextAsync(path, "Existing output", ct);
                var written = ugc
                    ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                    : await new MgxcChartWriter(new(path, chart)).WriteAsync(ct);
                Assert.True(written.Succeeded);
                var warning = Assert.Single(written.Diagnostics.Diagnostics);
                Assert.Equal(MsgKeys.Warn_Number_adjusted, warning.Message.Key);
                Assert.Equal(PenguinTools.Core.Diagnostic.Severity.Warning, warning.Severity);
                Assert.NotEqual("Existing output", await File.ReadAllTextAsync(path, ct));
                Assert.Equal(original, chart.Meta.MainBpm);
                Assert.Empty(chart.Extras.EditorNumberProjections);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static int BpmValueOffset(byte[] bytes)
    {
        var offset = bytes.AsSpan().IndexOf("bpm "u8);
        Assert.True(offset >= 0);
        offset += 4;
        var tickType = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
        offset += tickType switch
        {
            0 or 1 => 4,
            2 => 8,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(3, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset)));
        return offset + 4;
    }
}
