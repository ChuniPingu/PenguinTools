using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Core.Metadata;
using PenguinTools.Core;
using Xunit;
using C2sChart = PenguinTools.Chart.Models.c2s.Chart;

namespace PenguinTools.Tests.Parser;

public sealed class C2sReverseConversionTests
{
    [Fact]
    public void Sla_UsesHighestTimelineForNotesAndLongJoints()
    {
        var source = new C2sChart();
        source.Notes.Add(new Slide { Tick = 0, Lane = 2, Width = 3, EndTick = 480, EndLane = 4, EndWidth = 2 });
        source.Notes.Add(new Sla { Tick = 0, Length = 480, Lane = 0, Width = 8, Timeline = 2 });
        source.Notes.Add(new Sla { Tick = 480, Length = 100, Lane = 4, Width = 2, Timeline = 7 });

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded);
        var slide = Assert.IsType<PenguinTools.Chart.Models.umgr.Slide>(Assert.Single(result.Value!.Notes.Children));
        Assert.Equal(2, slide.Timeline);
        Assert.Equal(7, Assert.Single(slide.Children).Timeline);
    }

    [Fact]
    public void Sla_IsHalfOpen_EndTickIsExcluded()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap { Tick = 0, Lane = 2, Width = 2 });
        source.Notes.Add(new Tap { Tick = 480, Lane = 2, Width = 2 });
        source.Notes.Add(new Sla { Tick = 0, Length = 480, Lane = 0, Width = 8, Timeline = 5 });

        var notes = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!.Notes.Children
            .OfType<PenguinTools.Chart.Models.umgr.Tap>().OrderBy(n => n.Tick.Original).ToArray();

        Assert.Equal(5, notes[0].Timeline);
        Assert.Equal(0, notes[1].Timeline);
    }

    [Fact]
    public void Sla_RequiresNoteFullyContainedInLanes()
    {
        var source = new C2sChart();
        // Fully inside lanes 2..6
        source.Notes.Add(new Tap { Tick = 0, Lane = 2, Width = 2 });
        // Overlaps left edge but starts outside
        source.Notes.Add(new Tap { Tick = 0, Lane = 0, Width = 3 });
        // Overlaps right edge but extends past
        source.Notes.Add(new Tap { Tick = 0, Lane = 4, Width = 4 });
        source.Notes.Add(new Sla { Tick = 0, Length = 480, Lane = 2, Width = 4, Timeline = 9 });

        var notes = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!.Notes.Children
            .OfType<PenguinTools.Chart.Models.umgr.Tap>().OrderBy(n => n.Lane).ToArray();

        Assert.Equal(0, notes[0].Timeline); // lane 0..3 only overlaps
        Assert.Equal(9, notes[1].Timeline); // lane 2..4 fully inside
        Assert.Equal(0, notes[2].Timeline); // lane 4..8 only overlaps
    }

    [Fact]
    public void DebugTil_EmitsTransparentAirCrushForOriginalSla()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap { Tick = 0, Lane = 1, Width = 2 });
        source.Notes.Add(new Sla { Tick = 100, Length = 200, Lane = 2, Width = 4, Timeline = 3 });

        var chart = new UgcChartConverter(new UgcConvertRequest(source, DebugTil: true)).Convert().Value!;
        var crash = Assert.Single(chart.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>());

        Assert.Equal(Color.NON, crash.Color);
        Assert.Equal(0m, crash.Height);
        Assert.Equal(0, crash.Timeline);
        Assert.Equal(0, crash.Density.Original);
        Assert.Equal(100, crash.Tick.Original);
        Assert.Equal(2, crash.Lane);
        Assert.Equal(4, crash.Width);

        var joint = Assert.Single(crash.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrashJoint>());
        Assert.Equal(300, joint.Tick.Original);
        Assert.Equal(0m, joint.Height);
        Assert.Equal(0, joint.Timeline);
    }

    [Fact]
    public void DebugTil_Off_DoesNotEmitSlaMarkers()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap { Tick = 0, Lane = 1, Width = 2 });
        source.Notes.Add(new Sla { Tick = 100, Length = 200, Lane = 2, Width = 4, Timeline = 3 });

        var chart = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        Assert.Empty(chart.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>());
    }

    [Fact]
    public void SlideSegments_AreChainedWhenNextStartsAtPreviousEnd()
    {
        var source = new C2sChart();
        for (var i = 0; i < 4; i++)
            source.Notes.Add(new Slide
            {
                Tick = 0, Lane = 4, Width = 8, EndTick = 1920, EndLane = 0, EndWidth = 2,
                Joint = Joint.D, Effect = ExEffect.UP
            });
        source.Notes.Add(new Slide { Tick = 1920, Lane = 0, Width = 2, EndTick = 2400, EndLane = 0, EndWidth = 5, Joint = Joint.D, Effect = ExEffect.UP });
        source.Notes.Add(new Slide { Tick = 1920, Lane = 0, Width = 2, EndTick = 2880, EndLane = 0, EndWidth = 6, Joint = Joint.D, Effect = ExEffect.UP });
        source.Notes.Add(new Slide { Tick = 1920, Lane = 0, Width = 2, EndTick = 3360, EndLane = 0, EndWidth = 7, Joint = Joint.D, Effect = ExEffect.UP });
        source.Notes.Add(new Slide { Tick = 1920, Lane = 0, Width = 2, EndTick = 3840, EndLane = 0, EndWidth = 8, Joint = Joint.D, Effect = ExEffect.UP });

        var slides = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!.Notes.Children
            .OfType<PenguinTools.Chart.Models.umgr.Slide>().ToArray();

        Assert.Equal(4, slides.Length);
        Assert.All(slides, slide => Assert.Equal(2, slide.Children.Count));
        var firstJoints = slides[0].Children.OfType<PenguinTools.Chart.Models.umgr.SlideJoint>().ToArray();
        // SXD (Joint.D) intermediates stay step even when an Ex effect is present.
        Assert.Equal(Joint.D, firstJoints[0].Joint);
        Assert.Equal(Joint.D, firstJoints[1].Joint);
        Assert.Equal(0, firstJoints[1].Lane);
        Assert.Equal(5, firstJoints[1].Width);
        Assert.Equal(8, slides[3].Children.OfType<PenguinTools.Chart.Models.umgr.SlideJoint>().Last().Width);
    }

    [Fact]
    public void SlcIntermediate_IsControlJoint()
    {
        var source = new C2sChart();
        source.Notes.Add(new Slide
        {
            Tick = 0, Lane = 0, Width = 4, EndTick = 60, EndLane = 4, EndWidth = 4,
            Joint = Joint.C, Effect = ExEffect.UP
        });
        source.Notes.Add(new Slide
        {
            Tick = 60, Lane = 4, Width = 4, EndTick = 120, EndLane = 6, EndWidth = 4, Joint = Joint.D
        });

        var slide = Assert.IsType<PenguinTools.Chart.Models.umgr.Slide>(
            Assert.Single(new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!.Notes.Children));
        var joints = slide.Children.OfType<PenguinTools.Chart.Models.umgr.SlideJoint>().ToArray();

        Assert.Equal(Joint.C, joints[0].Joint);
        Assert.Equal(Joint.D, joints[1].Joint);
    }

    [Fact]
    public void PlainSldIntermediate_StaysStepJoint()
    {
        var source = new C2sChart();
        source.Notes.Add(new Slide { Tick = 0, Lane = 0, Width = 4, EndTick = 60, EndLane = 4, EndWidth = 4, Joint = Joint.D });
        source.Notes.Add(new Slide { Tick = 60, Lane = 4, Width = 4, EndTick = 120, EndLane = 6, EndWidth = 4, Joint = Joint.D });

        var slide = Assert.IsType<PenguinTools.Chart.Models.umgr.Slide>(
            Assert.Single(new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!.Notes.Children));
        var joints = slide.Children.OfType<PenguinTools.Chart.Models.umgr.SlideJoint>().ToArray();

        Assert.Equal(Joint.D, joints[0].Joint);
        Assert.Equal(Joint.D, joints[1].Joint);
    }

    [Fact]
    public async Task HxdHold_AirParentHld_PairsAtHoldEnd()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "hxd-air.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            HXD	0	0	0	4	96	UP
            AIR	0	96	0	4	HLD	DEF
            """, TestContext.Current.CancellationToken);

        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded);
        Assert.DoesNotContain(parsed.Diagnostics.Diagnostics,
            d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);

        var ugc = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert().Value!;
        var air = Assert.Single(ugc.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Air>());
        Assert.Equal(480, air.Tick.Original);
        Assert.NotNull(air.PairNote);
    }

    [Fact]
    public void SpeedDurations_RestoreDefaultOrActiveSpeed()
    {
        var source = new C2sChart();
        source.Events.Add(new Slp { Tick = 0, Length = 960, Speed = 2m, Timeline = 3 });
        source.Events.Add(new Slp { Tick = 240, Length = 240, Speed = 4m, Timeline = 3 });
        source.Events.Add(new Dcm { Tick = 100, Length = 200, Speed = .5m });

        var target = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        var scroll = target.Events.Children.OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>().ToArray();
        Assert.Contains(scroll, x => x.Tick.Original == 480 && x.Speed == 2m);
        Assert.Contains(scroll, x => x.Tick.Original == 960 && x.Speed == 1m);
        Assert.Contains(target.Events.Children.OfType<PenguinTools.Chart.Models.umgr.NoteSpeedEvent>(),
            x => x.Tick.Original == 300 && x.Speed == 1m);
    }

    [Fact]
    public void Converter_KeepsSharedTickSpeedPointsOnDistinctTimelines()
    {
        var source = new C2sChart();
        source.Events.Add(new Met { Tick = 0, Numerator = 4, Denominator = 4 });
        source.Events.Add(new Bpm { Tick = 0, Value = 160m });
        source.Events.Add(new Slp { Tick = 0, Length = 480, Speed = 1000m, Timeline = 14 });
        source.Events.Add(new Slp { Tick = 480, Length = 960, Speed = 0m, Timeline = 14 });
        source.Events.Add(new Slp { Tick = 1439, Length = 1, Speed = 24m, Timeline = 14 });
        source.Events.Add(new Slp { Tick = 0, Length = 480, Speed = 1000m, Timeline = 15 });
        source.Events.Add(new Slp { Tick = 480, Length = 1440, Speed = 0m, Timeline = 15 });
        source.Events.Add(new Slp { Tick = 1919, Length = 1, Speed = 48m, Timeline = 15 });
        source.Notes.Add(new Tap { Tick = 1440, Lane = 6, Width = 4 });
        source.Notes.Add(new Tap { Tick = 1920, Lane = 3, Width = 4 });
        source.Notes.Add(new Sla { Tick = 1200, Length = 480, Lane = 6, Width = 4, Timeline = 14 });
        source.Notes.Add(new Sla { Tick = 1680, Length = 480, Lane = 3, Width = 4, Timeline = 15 });

        var chart = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        var scrolls = chart.Events.Children.OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>().ToArray();

        Assert.Contains(scrolls, x => x is { Timeline: 14, Tick.Original: 0, Speed: 1000m });
        Assert.Contains(scrolls, x => x is { Timeline: 15, Tick.Original: 0, Speed: 1000m });
        Assert.Contains(scrolls, x => x is { Timeline: 14, Tick.Original: 480, Speed: 0m });
        Assert.Contains(scrolls, x => x is { Timeline: 15, Tick.Original: 480, Speed: 0m });
        Assert.Contains(scrolls, x => x is { Timeline: 14, Tick.Original: 1439, Speed: 24m });
        Assert.Contains(scrolls, x => x is { Timeline: 15, Tick.Original: 1919, Speed: 48m });
    }

    [Fact]
    public async Task MgxcWriter_IsDeterministicAndEmitsTimelines()
    {
        var source = new C2sChart { Meta = new Meta { Title = "Reverse", MainBpm = 120m } };
        source.Events.Add(new Met { Tick = 0, Numerator = 4, Denominator = 4 });
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Events.Add(new Slp { Tick = 0, Length = 480, Speed = 2m, Timeline = 4 });
        source.Notes.Add(new Tap { Tick = 0, Lane = 1, Width = 2 });
        source.Notes.Add(new Flick { Tick = 480, Lane = 1, Width = 2 });
        source.Notes.Add(new AirCrash
        {
            Tick = 480, Lane = 2, Width = 2, EndTick = 960, EndLane = 3, EndWidth = 2,
            Height = 80m, EndHeight = 60m, Density = 20
        });
        source.Notes.Add(new Sla { Tick = 0, Length = 10, Lane = 1, Width = 2, Timeline = 4 });
        var chart = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "first.mgxc");
        var second = Path.Combine(directory, "second.mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new MgxcWriteRequest(first, chart)).WriteAsync(ct);
            await new MgxcChartWriter(new MgxcWriteRequest(second, chart)).WriteAsync(ct);

            Assert.Equal(await File.ReadAllBytesAsync(first, ct), await File.ReadAllBytesAsync(second, ct));

            var parsed = await new MgxcParser(new MgxcParseRequest(first, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Contains(parsed.Value!.Events.Children.OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>(),
                x => x is { Timeline: 4, Tick.Original: 0, Speed: 2m });
            Assert.Contains(parsed.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Flick>(),
                x => x is { Lane: 1, Width: 2 });
            Assert.Contains(parsed.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>(),
                x => x.Children.Count > 0);
            Assert.Contains(parsed.Value.Notes.Children, n => n.Timeline == 4);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LegacySldMarker_IsNotAnEffect_AndParentsAreAssignedOneToOne()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "legacy.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            SLC	0	0	0	4	384	0	4	SLD
            SLC	0	0	0	4	384	0	4	SLD
            AUL	1	0	0	4	SLD	DEF
            AUR	1	0	0	4	SLD	DEF
            """, TestContext.Current.CancellationToken);

        var result = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        var slides = result.Value!.Notes.OfType<Slide>().ToArray();
        var airs = result.Value.Notes.OfType<Air>().ToArray();
        Assert.All(slides, slide => Assert.Null(slide.Effect));
        Assert.Equal(2, airs.Length);
        Assert.NotSame(airs[0].Parent, airs[1].Parent);
        Assert.DoesNotContain(result.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Key == MsgKeys.C2s_Unknown_ex_effect);
    }

    [Fact]
    public async Task Ahd_ParsesChainsAndConvertsToUgcAirHold()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "ahd.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            TAP	0	0	4	4
            AHD	0	0	4	4	TAP	96
            AHD	0	96	4	4	AHD	96
            TAP	1	0	0	4
            AHX	1	0	0	4	TAP	48	DEF
            """, TestContext.Current.CancellationToken);

        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var holds = parsed.Value!.Notes.OfType<AirHold>().OrderBy(x => x.Tick.Original).ToArray();
        Assert.Equal(3, holds.Length);
        Assert.Equal("AHD", holds[0].Id);
        Assert.Same(holds[0], holds[1].Parent);
        Assert.Equal("AHX", holds[2].Id);
        Assert.Equal(Joint.C, holds[2].Joint);

        var ugc = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(ugc.Succeeded, ugc.ToString());
        var airHolds = ugc.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirHold>()
            .OrderBy(x => x.Tick.Original).ToArray();
        Assert.Equal(2, airHolds.Length);
        Assert.Equal(2, airHolds[0].Children.Count);
        Assert.Equal(Joint.D, airHolds[0].Children.OfType<PenguinTools.Chart.Models.umgr.AirHoldJoint>().Last().Joint);
        Assert.Equal(Joint.C, airHolds[1].Children.OfType<PenguinTools.Chart.Models.umgr.AirHoldJoint>().Single().Joint);
    }

    [Fact]
    public async Task Asc_EndpointWithoutAirAction_ConvertsToUgcControlJoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "asc.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            TAP	0	0	4	4
            ASD	0	0	4	4	TAP	5.0	96	6	4	5.0	DEF
            ASC	0	96	6	4	ASD	5.0	96	8	4	5.0	DEF
            TAP	1	0	0	4
            ASC	1	0	0	4	TAP	5.0	48	2	4	5.0	DEF
            """, TestContext.Current.CancellationToken);

        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var slides = parsed.Value!.Notes.OfType<AirSlide>().OrderBy(x => x.Tick.Original).ToArray();
        Assert.Equal(3, slides.Length);
        Assert.Equal("ASD", slides[0].Id);
        Assert.Same(slides[0], slides[1].Parent);
        Assert.Equal("ASC", slides[1].Id);
        Assert.Equal(Joint.C, slides[1].Joint);
        Assert.Equal("ASC", slides[2].Id);
        Assert.Equal(Joint.C, slides[2].Joint);

        var ugc = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(ugc.Succeeded, ugc.ToString());
        var airSlides = ugc.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirSlide>()
            .OrderBy(x => x.Tick.Original).ToArray();
        Assert.Equal(2, airSlides.Length);

        var chained = airSlides[0].Children.OfType<PenguinTools.Chart.Models.umgr.AirSlideJoint>().ToArray();
        Assert.Equal(2, chained.Length);
        Assert.Equal(Joint.D, chained[0].Joint);
        Assert.Equal(Joint.C, chained[1].Joint);
        Assert.Equal(Joint.C, airSlides[1].Children.OfType<PenguinTools.Chart.Models.umgr.AirSlideJoint>().Single().Joint);
    }

    [Fact]
    public async Task ZeroNumeratorMet_IsSkippedAndMgxcStillWrites()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "zero-met.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            MET	0	0	4	4
            MET	1	0	4	0
            BPM	0	0	120.000
            SLP	1	0	96	0.500000	0
            TAP	0	0	0	4
            TAP	1	48	0	4
            """, TestContext.Current.CancellationToken);

        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        Assert.DoesNotContain(parsed.Value!.Events.OfType<Met>(), met => met.Numerator <= 0 || met.Denominator <= 0);
        Assert.Contains(parsed.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Key == MsgKeys.C2s_Invalid_field);

        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.DoesNotContain(converted.Value!.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BeatEvent>(),
            beat => beat.Numerator <= 0 || beat.Denominator <= 0);

        var outPath = Path.Combine(directory, "zero-met.mgxc");
        var written = await new MgxcChartWriter(new MgxcWriteRequest(outPath, converted.Value))
            .WriteAsync(TestContext.Current.CancellationToken);
        Assert.True(written.Succeeded, written.ToString());

        var reparsed = await new MgxcParser(new MgxcParseRequest(outPath, TestAssets.Load()), TestMediaTool.Instance)
            .ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(reparsed.Succeeded, reparsed.ToString());
        Assert.Contains(reparsed.Value!.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BeatEvent>(),
            beat => beat is { Bar: 0, Numerator: 4, Denominator: 4 });
        Assert.DoesNotContain(reparsed.Value.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BeatEvent>(),
            beat => beat.Denominator == 0);
        Assert.Contains(reparsed.Value.Events.Children.OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>(),
            til => til.Timeline == 0);
    }

    [Fact]
    public async Task MgxcWriter_PreservesNegativeLanes()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap { Tick = 0, Lane = -2, Width = 4 });
        source.Notes.Add(new Tap { Tick = 480, Lane = 0, Width = 2 });
        source.Notes.Add(new Tap { Tick = 960, Lane = 16, Width = 3 });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "negative-lanes.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());

            var taps = parsed.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Tap>()
                .OrderBy(n => n.Tick.Original).ToArray();
            Assert.Equal(3, taps.Length);
            Assert.Equal(-2, taps[0].Lane);
            Assert.Equal(4, taps[0].Width);
            Assert.Equal(0, taps[1].Lane);
            Assert.Equal(16, taps[2].Lane);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
