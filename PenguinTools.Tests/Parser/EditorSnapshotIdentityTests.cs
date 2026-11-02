using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class EditorSnapshotIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceWithoutResolutionHeader_RoundTripsAtTheDefaultResolution(bool ugc)
    {
        var path = TestTempPaths.Create(".c2s");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "VERSION\t1.15.00\t1.15.00\nBPM\t0\t0\t120\nTAP\t0\t96\t0\t4\n", ct);
            var source = await new C2SParser(new(path)).ParseAsync(ct);
            Assert.True(source.Succeeded);
            var editor = new UgcChartConverter(new(source.Value!)).Convert();
            Assert.True(editor.Succeeded);
            var returned = await RoundTrip(editor.Value!, ugc);
            var converted = new C2SChartConverter(new(returned)).Convert();
            Assert.True(converted.Succeeded);
            Assert.True((await new C2SChartWriter(new(output, converted.Value!)).WriteAsync(ct)).Succeeded);
            var final = await new C2SParser(new(output)).ParseAsync(ct);
            Assert.True(final.Succeeded);
            Assert.Equal(480, Assert.Single(final.Value!.Notes.OfType<C.Tap>()).Tick.Original);
        }
        finally { File.Delete(path); File.Delete(output); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoincidentAirProjections_FollowParentsWhenAirDeclarationsAreReversed(bool ugc)
    {
        var chart = Chart();
        var first = new U.Tap { Tick = 0, Lane = 0, Width = 4 };
        var second = new U.Tap { Tick = 0, Lane = 0, Width = 4 };
        var firstAir = new U.Air { Color = Color.GRN };
        var secondAir = new U.Air { Color = Color.DEF };
        first.MakePair(firstAir);
        second.MakePair(secondAir);
        chart.Notes.AppendChild(first);
        chart.Notes.AppendChild(second);
        chart.Notes.AppendChild(secondAir);
        chart.Notes.AppendChild(firstAir);

        var returned = await RoundTrip(chart, ugc);
        Assert.Equal([Color.GRN, Color.DEF], returned.Notes.Children.OfType<U.Tap>()
            .Select(t => Assert.IsType<U.Air>(t.PairNote).Color));
        Assert.Same(secondAir, chart.Notes.Children[2]);
        Assert.Same(firstAir, chart.Notes.Children[3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UgcWrite_AfterMgxcAirColorEdit_KeepsTheEditedColor(bool slide)
    {
        var chart = Chart();
        var tap = new U.Tap { Tick = 0, Lane = 0, Width = 4 };
        U.NegativeNote air = slide
            ? new U.AirSlide { Height = 80, Color = Color.PNK }
            : new U.AirHold { Color = Color.PNK };
        tap.MakePair(air);
        air.AppendChild(slide
            ? new U.AirSlideJoint { Tick = 480, Lane = 4, Width = 4, Height = 160 }
            : new U.AirHoldJoint { Tick = 480 });
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);

        var loaded = await RoundTrip(chart, ugc: false);
        var editable = Assert.Single(loaded.Notes.Children.OfType<U.NegativeNote>());
        if (editable is U.AirSlide airSlide)
        {
            airSlide.Color = Color.DEF;
        }
        else
        {
            Assert.IsType<U.AirHold>(editable).Color = Color.DEF;
        }

        var returned = await RoundTrip(loaded, ugc: true);
        var converted = new C2SChartConverter(new(returned)).Convert();
        Assert.True(converted.Succeeded);
        Assert.Equal(Color.DEF, slide
            ? Assert.Single(converted.Value!.Notes.OfType<C.AirSlide>()).Color
            : Assert.Single(converted.Value!.Notes.OfType<C.AirHold>()).Color);
        Assert.Equal(Color.PNK, Assert.Single(loaded.Extras.AirAppearances).Color);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OverlappingSlidePaths_RetainTheirOwnSegmentEffects(bool ugc, bool firstOverride)
    {
        var chart = Chart();
        var first = Slide();
        var second = Slide();
        if (firstOverride)
        {
            var joint = Assert.IsType<U.SlideJoint>(first.FirstChild);
            joint.HasEffectOverride = true;
            joint.SegmentEffect = ExEffect.DW;
        }
        var secondJoint = Assert.IsType<U.SlideJoint>(second.FirstChild);
        secondJoint.HasEffectOverride = true;
        secondJoint.SegmentEffect = ExEffect.UP;
        chart.Notes.AppendChild(first);
        chart.Notes.AppendChild(second);

        for (var pass = 0; pass < 2; pass++)
        {
            chart = await RoundTrip(chart, ugc);
            var slides = chart.Notes.Children.OfType<U.Slide>().ToArray();
            Assert.Equal(2, slides.Length);
            var firstJoint = Assert.IsType<U.SlideJoint>(slides[0].FirstChild);
            Assert.Equal(firstOverride, firstJoint.HasEffectOverride);
            Assert.Equal(firstOverride ? ExEffect.DW : null, firstJoint.SegmentEffect);
            secondJoint = Assert.IsType<U.SlideJoint>(slides[1].FirstChild);
            Assert.True(secondJoint.HasEffectOverride);
            Assert.Equal(ExEffect.UP, secondJoint.SegmentEffect);

            var converted = new C2SChartConverter(new(chart)).Convert();
            Assert.True(converted.Succeeded);
            Assert.Equal<ExEffect?>([firstOverride ? ExEffect.DW : null, ExEffect.UP],
                converted.Value!.Notes.OfType<C.Slide>().Where(n => n.Tick.Original == 480).Select(n => n.Effect));
        }
    }

    private static U.Slide Slide()
    {
        var slide = new U.Slide { Tick = 0, Lane = 0, Width = 4 };
        slide.AppendChild(new U.SlideJoint { Tick = 480, Lane = 4, Width = 4 });
        slide.AppendChild(new U.SlideJoint { Tick = 960, Lane = 8, Width = 4 });
        return slide;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OverlappingCrashes_RetainTheirOwnTraceAttribute(bool ugc, bool traceFirst)
    {
        var chart = Chart();
        var attributes = traceFirst
            ? new[] { AirLadderAttr.Trace, AirLadderAttr.DEF }
            : new[] { AirLadderAttr.DEF, AirLadderAttr.Trace };
        foreach (var attribute in attributes)
        {
            var crash = new U.AirCrash { Tick = 0, Lane = 0, Width = 4, Height = 80, Density = 120, Attr = attribute };
            crash.AppendChild(new U.AirCrashJoint { Tick = 480, Lane = 4, Width = 4, Height = 160 });
            chart.Notes.AppendChild(crash);
        }

        for (var pass = 0; pass < 2; pass++)
        {
            chart = await RoundTrip(chart, ugc);
            Assert.Equal(attributes, chart.Notes.Children.OfType<U.AirCrash>().Select(c => c.Attr));
            var converted = new C2SChartConverter(new(chart)).Convert();
            Assert.True(converted.Succeeded);
            Assert.Equal(attributes, converted.Value!.Notes.OfType<C.AirCrash>().Select(c => c.Attr));
        }
    }

    private static U.Chart Chart()
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        return chart;
    }

    private static async Task<U.Chart> RoundTrip(U.Chart chart, bool ugc)
    {
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = ugc ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                : await new MgxcChartWriter(new(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded);
            var parsed = ugc ? await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }
}
