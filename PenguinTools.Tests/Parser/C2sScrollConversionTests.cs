using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sScrollConversionTests
{
    private static async Task<U.Chart> Convert(string rows)
    {
        var file = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(file,
                "VERSION\t1.15.00\t1.15.00\nRESOLUTION\t384\nBPM\t0\t0\t120\n" +
                "SLA\t0\t0\t0\t4\t385\t1\nTAP\t0\t0\t0\t4\nTAP\t1\t0\t0\t4\n" + rows, ct);
            var parsed = await new C2SParser(new C2SParseRequest(file)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            return converted.Value!;
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingScrollIntervalsSumTheirSpeedDeltas(bool reversed)
    {
        string[] rows = ["SLP\t0\t0\t96\t2\t1", "SLP\t0\t48\t96\t3\t1"];
        if (reversed)
        {
            Array.Reverse(rows);
        }

        var chart = await Convert(string.Join('\n', rows) + "\nSLP\t0\t24\t0\t100\t1\n");

        var events = chart.Events.Children.OfType<U.ScrollSpeedEvent>().OrderBy(e => e.Tick).ToArray();
        Assert.Equal([0, 240, 480, 720], events.Select(e => e.Tick.Original));
        Assert.Equal([2m, 4m, 3m, 1m], events.Select(e => e.Speed));
        Assert.All(events, e => Assert.Equal(1, e.Timeline));
    }

    [Fact]
    public async Task DuplicateAndNestedScrollIntervalsRestoreCombinedSpeed()
    {
        var chart = await Convert("SLP\t0\t0\t192\t0.1\t1\nSLP\t0\t0\t192\t0.1\t1\n" +
            "SLP\t0\t48\t48\t-1\t1\n");

        var events = chart.Events.Children.OfType<U.ScrollSpeedEvent>().OrderBy(e => e.Tick).ToArray();
        Assert.Equal([0, 240, 480, 960], events.Select(e => e.Tick.Original));
        Assert.Equal([-0.8m, -2.8m, -0.8m, 1m], events.Select(e => e.Speed));
    }

    [Fact]
    public async Task LegacyScrollAndStopShareMainTimelineButDcmStaysSeparate()
    {
        var chart = await Convert("SLP\t0\t0\t96\t2\t0\nSFL\t0\t24\t48\t3\nSTP\t0\t48\t48\n" +
            "DCM\t0\t0\t96\t7\nSLP\t0\t0\t96\t5\t2\n");

        var main = chart.Events.Children.OfType<U.ScrollSpeedEvent>().Where(e => e.Timeline == 0)
            .OrderBy(e => e.Tick).ToArray();
        Assert.Equal([0, 120, 240, 360], main.Select(e => e.Tick.Original));
        Assert.Equal([2m, 4m, 3m, 1m], main.Select(e => e.Speed));
        Assert.Equal([5m, 1m], chart.Events.Children.OfType<U.ScrollSpeedEvent>()
            .Where(e => e.Timeline == 2).OrderBy(e => e.Tick).Select(e => e.Speed));
        Assert.Equal([7m, 1m], chart.Events.Children.OfType<U.NoteSpeedEvent>()
            .OrderBy(e => e.Tick).Select(e => e.Speed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UgcWritesCombinedSpeedAndRestoresOriginalIntervalsOnlyWhileUnedited(bool edited)
    {
        var chart = await Convert("SLP\t0\t0\t96\t2\t1\nSLP\t0\t48\t96\t3\t1\n");
        var file = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new UgcChartWriter(file, chart).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            Assert.Contains("@TIL\t1\t0'240\t4", await File.ReadAllLinesAsync(file, ct));
            var parsed = await new UgcParser(new UgcParseRequest(file, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            if (edited)
            {
                parsed.Value!.Events.Children.OfType<U.ScrollSpeedEvent>()
                    .Single(e => e.Timeline == 1 && e.Tick.Original == 240).Speed = 5;
            }

            var result = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
            Assert.True(result.Succeeded, result.ToString());
            var intervals = result.Value!.Events.OfType<Slp>().Where(e => e.Timeline == 1).ToArray();
            if (edited)
            {
                Assert.Equal(5m, 1m + intervals.Where(e =>
                    e.Tick.Original <= 360 && 360 < e.Tick.Original + e.Length.Original).Sum(e => e.Speed - 1m));
            }
            else
            {
                Assert.Equal(2, intervals.Length);
                Assert.Contains(intervals, e => e.Tick.Original == 0 && e.Length.Original == 480 && e.Speed == 2);
                Assert.Contains(intervals, e => e.Tick.Original == 240 && e.Length.Original == 480 && e.Speed == 3);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }
}
