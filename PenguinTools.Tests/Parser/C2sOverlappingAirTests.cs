using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sOverlappingAirTests
{
    [Theory]
    [InlineData("AIR", false)]
    [InlineData("AIR", true)]
    [InlineData("AHD", false)]
    [InlineData("AHD", true)]
    [InlineData("ASD", false)]
    [InlineData("ASD", true)]
    public async Task TouchingOutgoingPath_CannotCreateUnpairedParentFirst(string kind, bool noLine)
    {
        var chart = NewChart();
        AddSlide(chart, 0, 0, false, (240, 4), (960, 8));
        var intended = AddSlide(chart, 240, 4, noLine, (960, 8));
        AddSlide(chart, 960, 8, noLine, (1440, 12));
        Attach(chart, intended, kind);

        var restored = await ThroughC2s(chart);
        Assert.Equal(3, restored.Notes.Children.OfType<U.Slide>().Count());
        var air = Assert.Single(restored.Notes.Children.OfType<U.NegativeNote>());
        var parent = Assert.IsType<U.Slide>(air.PairNote!.Parent);
        Assert.Equal(240, parent.Tick.Original);
        Assert.Equal(4, parent.Lane);
        Assert.Equal(noLine, parent.NoLine);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlapAfterFinalQuantization_SelectsTheAttachedPath(bool offset)
    {
        var chart = NewChart();
        if (offset)
        {
            chart.Extras.FixedZeroOrigin = false;
            chart.Meta.BgmEnableBarOffset = true;
            chart.Meta.BgmInitialNumerator = 1;
            chart.Meta.BgmInitialDenominator = 7;
            var beat = chart.Events.Children.OfType<U.BeatEvent>().Single();
            beat.Numerator = 1;
            beat.Denominator = 7;
        }
        AddSlide(chart, 0, 0, false, (offset ? 482 : 481, 8));
        var intended = AddSlide(chart, 240, 4, false, (offset ? 483 : 482, 8));
        Attach(chart, intended, "AIR");

        var restored = await ThroughC2s(chart);
        var air = Assert.Single(restored.Notes.Children.OfType<U.Air>());
        Assert.Equal(4, Assert.IsType<U.Slide>(air.PairNote!.Parent).Lane);
    }

    [Fact]
    public async Task SharedControlsAndTerminalCell_DoNotExchangeContinuations()
    {
        var chart = NewChart();
        var first = AddSlide(chart, 240, 12, false, (480, 8), (720, 0));
        var second = AddSlide(chart, 480, 8, false, (720, 0), (960, 12), (1440, 8));
        Attach(chart, first, "AIR");
        Attach(chart, second, "AHD");

        var restored = await ThroughC2s(chart);
        var slides = restored.Notes.Children.OfType<U.Slide>().ToArray();
        var shortPath = Assert.Single(slides, s => s.Lane == 12);
        var longPath = Assert.Single(slides, s => s.Lane == 8);
        Assert.Equal([8, 0], shortPath.Children.Select(p => p.Lane));
        Assert.Equal([0, 12, 8], longPath.Children.Select(p => p.Lane));
        Assert.Same(shortPath.LastChild, Assert.Single(restored.Notes.Children.OfType<U.Air>()).PairNote);
        Assert.Same(longPath.LastChild, Assert.Single(restored.Notes.Children.OfType<U.AirHold>()).PairNote);
    }

    [Fact]
    public async Task MultipleAttachmentsAtSharedEndpoint_KeepEachGroundOwner()
    {
        var chart = NewChart();
        AddSlide(chart, 0, 0, false, (240, 4), (960, 8));
        var first = AddSlide(chart, 240, 4, false, (960, 8));
        var second = AddSlide(chart, 480, 6, true, (960, 8));
        AddSlide(chart, 960, 8, false, (1440, 12));
        Attach(chart, second, "AHD");
        Attach(chart, first, "AIR");

        var restored = await ThroughC2s(chart);
        Assert.Equal(4, restored.Notes.Children.OfType<U.Slide>().Count());
        Assert.Equal(4, Assert.IsType<U.Slide>(Assert.Single(restored.Notes.Children.OfType<U.Air>()).PairNote!.Parent).Lane);
        Assert.Equal(6, Assert.IsType<U.Slide>(Assert.Single(restored.Notes.Children.OfType<U.AirHold>()).PairNote!.Parent).Lane);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ShortAirAtSharedGroundEndpoint_CannotCaptureLongAirContinuation(bool holdParent, bool airSlide)
    {
        var chart = NewChart();
        U.PositiveNote Parent(int start, int lane)
        {
            if (!holdParent)
            {
                return (U.SlideJoint)AddSlide(chart, start, lane, false, (960, 8)).LastChild!;
            }

            var hold = new U.Hold { Tick = start, Lane = 8, Width = 4 };
            var tail = new U.HoldJoint { Tick = 960 };
            hold.AppendChild(tail);
            chart.Notes.AppendChild(hold);
            return tail;
        }
        var shortParent = Parent(0, 0);
        var longParent = Parent(240, 4);
        AttachChain(chart, shortParent, airSlide, 1200);
        AttachChain(chart, longParent, airSlide, 1200, 1440);

        var restored = await ThroughC2s(chart);
        var airs = restored.Notes.Children.OfType<U.NegativeNote>().ToDictionary(n => n.PairNote!.Parent!.Tick.Original);
        Assert.Equal([1200], airs[0].Children.Select(n => n.Tick.Original));
        Assert.Equal([1200, 1440], airs[240].Children.Select(n => n.Tick.Original));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedAirControlAndTerminalCells_KeepCompletePaths(bool airSlide)
    {
        var chart = NewChart();
        var shortParent = AddSlide(chart, 240, 4, false, (960, 0), (1440, 12));
        var longParent = AddSlide(chart, 0, 4, false, (1200, 12));
        AttachChain(chart, (U.SlideJoint)shortParent.LastChild!, airSlide, 1920, 2160, 2640);
        AttachChain(chart, (U.SlideJoint)longParent.LastChild!, airSlide, 1680, 2160, 2640, 2880);

        var restored = await ThroughC2s(chart);
        var airs = restored.Notes.Children.OfType<U.NegativeNote>().ToDictionary(n => n.PairNote!.Parent!.Tick.Original);
        Assert.Equal([1920, 2160, 2640], airs[240].Children.Select(n => n.Tick.Original));
        Assert.Equal([1680, 2160, 2640, 2880], airs[0].Children.Select(n => n.Tick.Original));
    }

    private static void AttachChain(U.Chart chart, U.PositiveNote parent, bool airSlide, params int[] ticks)
    {
        U.NegativeNote air = airSlide ? new U.AirSlide() : new U.AirHold();
        parent.MakePair(air);
        foreach (var tick in ticks)
        {
            air.AppendChild(airSlide
                ? new U.AirSlideJoint { Tick = tick, Lane = parent.Lane, Width = parent.Width }
                : new U.AirHoldJoint { Tick = tick });
        }
        chart.Notes.AppendChild(air);
    }

    [Fact]
    public void ZeroLengthOverlaps_ReportShortLengthsAtTheActualTime()
    {
        var chart = NewChart();
        AddSlide(chart, 1920, 0, false, (1920, 0));
        AddSlide(chart, 1920, 0, false, (1920, 0));

        var result = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();

        Assert.True(result.Succeeded, result.ToString());
        var diagnostics = result.Diagnostics.Diagnostics;
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(MsgKeys.Mg_Length_smaller_than_unit, diagnostic.Message.Key);
            Assert.Equal(1920, diagnostic.Time);
        });
    }

    private static U.Chart NewChart()
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent());
        return chart;
    }

    private static U.Slide AddSlide(U.Chart chart, int tick, int lane, bool noLine, params (int Tick, int Lane)[] joints)
    {
        var slide = new U.Slide { Tick = tick, Lane = lane, Width = 4, NoLine = noLine };
        foreach (var joint in joints)
        {
            slide.AppendChild(new U.SlideJoint { Tick = joint.Tick, Lane = joint.Lane, Width = 4, NoLine = noLine, Joint = Joint.C });
        }
        ((U.SlideJoint)slide.LastChild!).Joint = Joint.D;
        chart.Notes.AppendChild(slide);
        return slide;
    }

    private static void Attach(U.Chart chart, U.Slide slide, string kind)
    {
        var tail = (U.SlideJoint)slide.LastChild!;
        U.NegativeNote air = kind switch
        {
            "AHD" => new U.AirHold(),
            "ASD" => new U.AirSlide(),
            _ => new U.Air()
        };
        if (air is U.AirHold hold)
        {
            hold.AppendChild(new U.AirHoldJoint { Tick = tail.Tick.Original + 480 });
        }
        if (air is U.AirSlide airSlide)
        {
            airSlide.AppendChild(new U.AirSlideJoint { Tick = tail.Tick.Original + 480, Lane = 4, Width = 4 });
        }
        tail.MakePair(air);
        chart.Notes.AppendChild(air);
    }

    private static async Task<U.Chart> ThroughC2s(U.Chart chart)
    {
        var result = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        Assert.DoesNotContain(result.Diagnostics.Diagnostics, d => d.Message.Key == "diag.mg.overlapping_air_parent_slide");
        Assert.DoesNotContain(result.Diagnostics.Diagnostics, d => d.Message.Key == "diag.mg.ambiguous_c2s_slide_path");
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, result.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
            var restored = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
            Assert.True(restored.Succeeded, restored.ToString());
            return restored.Value!;
        }
        finally { File.Delete(path); }
    }
}
