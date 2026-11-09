using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class EditorMainTimelineTests
{
    [Fact]
    public async Task SpeedEvents_AfterLastNoteAndOnUnusedTimelineAreRetained()
    {
        var source = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(source,
                "@VER\t9\n@TICKS\t480\n@MAINBPM\t120\n@FLAG\tSOFFSET\tFALSE\n@BEAT\t0\t4\t4\n" +
                "@TIL\t0\t0'0\t2\n@TIL\t0\t2'0\t1\n@TIL\t2\t0'0\t3\n#1'0:tG4\n", ct);
            var parsed = await new UgcParser(new(source, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.True((await new MgxcChartWriter(new(output, parsed.Value!)).WriteAsync(ct)).Succeeded);
            var saved = await new MgxcParser(new(output, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(saved.Succeeded, saved.ToString());
            var speeds = saved.Value!.Events.Children.OfType<U.ScrollSpeedEvent>().ToArray();
            Assert.Contains(speeds, e => e.Timeline == 0 && e.Tick.Original == 3840 && e.Speed == 1);
            Assert.Contains(speeds, e => e.Timeline == 2 && e.Tick.Original == 0 && e.Speed == 3);
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonzeroMainTimeline_RemainsTheMainCurveAcrossRepeatedSaves(bool ugc)
    {
        var source = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(source,
                "@VER\t9\n@TICKS\t480\n@MAINBPM\t120\n@FLAG\tSOFFSET\tFALSE\n" +
                "@BEAT\t0\t4\t4\n@MAINTIL\t2\n@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t3\n" +
                "@USETIL\t2\n#0'0:tG4\n@USETIL\t0\n#1'0:tK4\n", ct);
            var parsed = await new UgcParser(new UgcParseRequest(source, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var chart = parsed.Value!;

            for (var cycle = 0; cycle < 3; cycle++)
            {
                var main = chart.Meta.MainTil;
                var mainSpeed = Assert.Single(chart.Events.Children.OfType<U.ScrollSpeedEvent>(), e => e.Timeline == main);
                Assert.Equal(3, mainSpeed.Speed);
                var taps = chart.Notes.Children.OfType<U.Tap>().ToArray();
                Assert.Equal(main, taps[0].Timeline);
                Assert.NotEqual(main, taps[1].Timeline);
                Assert.Equal(1, Assert.Single(chart.Events.Children.OfType<U.ScrollSpeedEvent>(),
                    e => e.Timeline == taps[1].Timeline).Speed);

                var written = ugc
                    ? await new UgcChartWriter(output, chart).WriteAsync(ct)
                    : await new MgxcChartWriter(new MgxcWriteRequest(output, chart)).WriteAsync(ct);
                Assert.True(written.Succeeded, written.ToString());
                parsed = ugc
                    ? await new UgcParser(new UgcParseRequest(output, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                    : await new MgxcParser(new MgxcParseRequest(output, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                Assert.True(parsed.Succeeded, parsed.ToString());
                chart = parsed.Value!;
            }
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
        }
    }
}
