using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sZeroNoteSpeedTests
{
    [Fact]
    public async Task ZeroModifier_WarnsOnceForC2sAndSurvivesBothEditorFormats()
    {
        var source = TestTempPaths.Create(".ugc");
        var ugc = TestTempPaths.Create(".ugc");
        var mgxc = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(source,
                "@VER\t9\n@TICKS\t480\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n" +
                "@FLAG\tSOFFSET\tFALSE\n@TIL\t0\t0'0\t1\n" +
                "@SPDMOD\t0'480\t0\n@SPDMOD\t0'960\t1\n#0'640:tG4\n#0'800:tK4\n", ct);
            var parsed = await new UgcParser(new(source, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.Empty(Warnings(parsed));
            var converted = new C2SChartConverter(new(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded);
            var warning = Assert.Single(Warnings(converted));
            Assert.Equal(Severity.Warning, warning.Severity);
            Assert.Equal(640, warning.Time);

            var savedUgc = await new UgcChartWriter(ugc, parsed.Value!).WriteAsync(ct);
            var savedMgxc = await new MgxcChartWriter(new(mgxc, parsed.Value!)).WriteAsync(ct);
            Assert.True(savedUgc.Succeeded);
            Assert.True(savedMgxc.Succeeded);
            Assert.Empty(Warnings(savedUgc));
            Assert.Empty(Warnings(savedMgxc));
            var readUgc = await new UgcParser(new(ugc, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            var readMgxc = await new MgxcParser(new(mgxc, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(readUgc.Succeeded);
            Assert.True(readMgxc.Succeeded);
            Assert.Single(readUgc.Value!.Events.Children.OfType<U.NoteSpeedEvent>(), e => e.Speed == 0);
            Assert.Single(readMgxc.Value!.Events.Children.OfType<U.NoteSpeedEvent>(), e => e.Speed == 0);
        }
        finally
        {
            File.Delete(source);
            File.Delete(ugc);
            File.Delete(mgxc);
        }
    }

    [Theory]
    [InlineData(479, 0, true, 1, false)]
    [InlineData(480, 0, true, 1, true)]
    [InlineData(959, 0, true, 1, true)]
    [InlineData(960, 0, true, 1, false)]
    [InlineData(640, 1, true, 1, false)]
    [InlineData(640, 0, false, 1, false)]
    [InlineData(640, 0, true, 0, false)]
    public void Warning_RequiresAnActiveModifierAndNonzeroDefinedTimeline(
        int tick, int timeline, bool defineTimeline, int scrollSpeed, bool expectedWarning)
    {
        var chart = Chart();
        if (defineTimeline)
        {
            chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Timeline = 0, Speed = scrollSpeed });
        }
        chart.Notes.AppendChild(new U.Tap { Tick = tick, Timeline = timeline, Lane = 0, Width = 4 });
        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded);
        Assert.Equal(expectedWarning ? 1 : 0, Warnings(converted).Count());
    }

    [Fact]
    public void Warning_IncludesLongEndpointsAndHonorsSameTickOverrides()
    {
        var chart = Chart();
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Speed = 1 });
        var hold = new U.Hold { Tick = 0, Lane = 0, Width = 4 };
        hold.AppendChild(new U.HoldJoint { Tick = 480 });
        chart.Notes.AppendChild(hold);
        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded);
        Assert.Equal(480, Assert.Single(Warnings(converted)).Time);

        chart.Events.AppendChild(new U.NoteSpeedEvent { Tick = 480, Speed = 1 });
        converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded);
        Assert.Empty(Warnings(converted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Warning_IncludesGeneratedAirCrashRungsOnTheirSegmentTimeline(bool defineRungTimeline)
    {
        var chart = Chart();
        var modifiers = chart.Events.Children.OfType<U.NoteSpeedEvent>().ToArray();
        modifiers[0].Tick = 400;
        modifiers[1].Tick = 600;
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Timeline = defineRungTimeline ? 1 : 0, Speed = 1 });
        var crash = new U.AirCrash { Tick = 0, Lane = 0, Width = 4, Height = 80, Density = 240 };
        crash.AppendChild(new U.AirCrashJoint { Tick = 240, Timeline = 1, Lane = 4, Width = 4, Height = 80 });
        crash.AppendChild(new U.AirCrashJoint { Tick = 720, Timeline = 1, Lane = 8, Width = 4, Height = 80 });
        chart.Notes.AppendChild(crash);
        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded);
        if (defineRungTimeline)
        {
            Assert.Equal(480, Assert.Single(Warnings(converted)).Time);
        }
        else
        {
            Assert.Empty(Warnings(converted));
        }
    }

    [Fact]
    public void ZeroTimelineSegment_MakesTheModifierIneffectiveEvenWithEarlierMotion()
    {
        var chart = Chart();
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Speed = 1 });
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 480, Speed = 0 });
        chart.Notes.AppendChild(new U.Tap { Tick = 640, Lane = 0, Width = 4 });
        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded);
        Assert.Empty(Warnings(converted));
    }

    [Theory]
    [InlineData("ugc", "2", true)]
    [InlineData("ugc", "1.000000059604644775390625", false)]
    [InlineData("mgxc", "2", true)]
    [InlineData("mgxc", "1.000000059604644775390625", false)]
    public void UndefinedEditorTimeline_WarnsOnlyWhenC2sWouldApplyANonunitModifier(string extension, string value, bool expectedWarning)
    {
        var chart = Chart();
        chart.Meta.FilePath = $"native.{extension}";
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Timeline = 0, Speed = 1 });
        var speed = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        chart.Events.Children.OfType<U.NoteSpeedEvent>().First().Speed = speed;
        chart.Notes.AppendChild(new U.Tap { Tick = 640, Timeline = 1, Lane = 0, Width = 4 });
        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded);
        Assert.Equal(expectedWarning, converted.Diagnostics.Diagnostics.Any(d =>
            d.Message.Key == MsgKeys.Warn_Editor_undefined_timeline_speed));

        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Timeline = 1, Speed = 1 });
        converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded);
        Assert.DoesNotContain(converted.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_undefined_timeline_speed);
    }

    private static U.Chart Chart()
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        chart.Events.AppendChild(new U.NoteSpeedEvent { Tick = 480, Speed = 0 });
        chart.Events.AppendChild(new U.NoteSpeedEvent { Tick = 960, Speed = 1 });
        return chart;
    }

    private static IEnumerable<Diagnostic> Warnings(OperationResult result) =>
        result.Diagnostics.Diagnostics.Where(d => d.Message.Key == MsgKeys.Warn_Zero_note_speed_not_reproduced);

    private static IEnumerable<Diagnostic> Warnings<T>(OperationResult<T> result) => Warnings(result.ToResult());
}
