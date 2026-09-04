using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Core.Metadata;
using PenguinTools.Core;
using System.Text;
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
    public void SlcTail_RemainsControlJoint()
    {
        var source = new C2sChart();
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 120,
            EndLane = 6,
            EndWidth = 4,
            Joint = Joint.C
        });

        var result = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());

        var slide = Assert.IsType<PenguinTools.Chart.Models.umgr.Slide>(
            Assert.Single(result.Value!.Notes.Children));

        var tail = Assert.IsType<PenguinTools.Chart.Models.umgr.SlideJoint>(
            Assert.Single(slide.Children));

        Assert.Equal(Joint.C, tail.Joint);
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
    public async Task LaterSxd_MarksWholeSlideEx_AndPreservesNoLine()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "later-sxd.c2s");

        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.15.00	1.15.00
                RESOLUTION	384
                SLD	0	0	0	4	96	2	4	SLD
                SXD	0	96	2	4	96	4	4	NCL
                """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path))
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var segments = parsed.Value!.Notes
                .OfType<Slide>()
                .OrderBy(x => x.Tick.Original)
                .ToArray();

            Assert.Equal(2, segments.Length);
            Assert.Null(segments[0].Effect);
            Assert.Equal(ExEffect.UP, segments[1].Effect);
            Assert.False(segments[0].NoLine);
            Assert.True(segments[1].NoLine);

            var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();

            Assert.True(converted.Succeeded, converted.ToString());

            var slide = Assert.Single(
                converted.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Slide>());

            Assert.Equal(ExEffect.UP, slide.Effect);
            Assert.False(slide.NoLine);

            var joints = slide.Children
                .OfType<PenguinTools.Chart.Models.umgr.SlideJoint>()
                .ToArray();

            Assert.Equal(2, joints.Length);
            Assert.True(joints[0].NoLine);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task HxdWithoutExplicitEffect_RemainsExHold()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "hxd-default.c2s");

        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.15.00	1.15.00
                RESOLUTION	384
                HXD	0	0	0	4	96
                """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path))
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var hold = Assert.Single(parsed.Value!.Notes.OfType<Hold>());
            Assert.Equal(ExEffect.UP, hold.Effect);
            Assert.Equal("HXD", hold.Id);

            var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();

            Assert.True(converted.Succeeded, converted.ToString());

            var convertedHold = Assert.Single(
                converted.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Hold>());

            Assert.Equal(ExEffect.UP, convertedHold.Effect);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
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
    public async Task MgxcWriter_DeduplicatesGeneratedExLongCarriersAtSamePosition()
    {
        var source = new C2sChart();

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 0,
            EndWidth = 4,
            Effect = ExEffect.UP
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 960,
            EndLane = 0,
            EndWidth = 4,
            Effect = ExEffect.UP
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "stacked-ex-hold.mgxc");

        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var holds = parsed.Value!.Notes.Children
                .OfType<PenguinTools.Chart.Models.umgr.Hold>()
                .ToArray();

            var carriers = parsed.Value.Notes.Children
                .OfType<PenguinTools.Chart.Models.umgr.ExTap>()
                .ToArray();

            Assert.Equal(2, holds.Length);
            Assert.Single(carriers);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task HoldOnlyCarrier_DoesNotMarkOverlappingPlainSlideEx()
    {
        var source = new C2sChart();

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 0,
            EndWidth = 4,
            Effect = ExEffect.UP
        });

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "hold-only-carrier-overlapping-slide.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var hold = Assert.Single(
                parsed.Value!.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.Hold>());

            var slide = Assert.Single(
                parsed.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.Slide>());

            Assert.Equal(ExEffect.UP, hold.Effect);
            Assert.Null(slide.Effect);

            var carrier = Assert.Single(
                parsed.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.ExTap>());

            Assert.Equal(
                PenguinTools.Chart.Models.umgr.ExTapRole.HoldOnlyCarrier,
                carrier.Role);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ExplicitChr_DoesNotReplaceOverlappingExHoldCarrier()
    {
        var source = new C2sChart();

        source.Notes.Add(new ExTap
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Effect = ExEffect.UP
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 0,
            EndWidth = 4,
            Effect = ExEffect.UP
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var mgxcPath = Path.Combine(
            directory,
            "explicit-chr-overlapping-hxd.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(mgxcPath, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(mgxcPath, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var explicitChr = Assert.Single(
                parsed.Value!.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.ExTap>(),
                x => x.Role == PenguinTools.Chart.Models.umgr.ExTapRole.Explicit);

            Assert.Equal(ExEffect.UP, explicitChr.Effect);

            var hold = Assert.Single(
                parsed.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.Hold>());

            Assert.Equal(ExEffect.UP, hold.Effect);

            var roundtrip = new C2SChartConverter(
                new C2SConvertRequest(parsed.Value)).Convert();

            Assert.True(roundtrip.Succeeded, roundtrip.ToString());

            Assert.Single(roundtrip.Value!.Notes.OfType<ExTap>());

            var roundtripHold = Assert.Single(
                roundtrip.Value.Notes.OfType<Hold>());

            Assert.Equal(ExEffect.UP, roundtripHold.Effect);
            Assert.Equal("HXD", roundtripHold.Id);
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

    [Fact]
    public async Task Parser_AcceptsVersion114_WithNegativeLanes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "v114.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
            VERSION	1.14.00	1.14.00
            MUSIC	0
            SEQUENCEID	0
            DIFFICULT	03
            LEVEL	15.0
            CREATOR	test
            BPM_DEF	120.000	120.000	120.000	120.000
            MET_DEF	4	4
            RESOLUTION	384
            CLK_DEF	384
            PROGJUDGE_BPM	240.000
            PROGJUDGE_AER	0.999
            TUTORIAL	0

            MET	0	0	4	4
            BPM	0	0	120.000

            TAP	0	0	-2	4
            """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Unsupported_version);
            Assert.Contains(parsed.Value!.Notes.OfType<Tap>(), tap => tap is { Lane: -2, Width: 4 });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_AcceptsVersion115()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "v115.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
            VERSION	1.15.00	1.15.00
            MUSIC	0
            SEQUENCEID	0
            DIFFICULT	03
            LEVEL	15.0
            CREATOR	test
            BPM_DEF	120.000	120.000	120.000	120.000
            MET_DEF	4	4
            RESOLUTION	384
            CLK_DEF	384
            PROGJUDGE_BPM	240.000
            PROGJUDGE_AER	0.999
            TUTORIAL	0

            MET	0	0	4	4
            BPM	0	0	120.000

            TAP	0	0	0	4
            """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Unsupported_version);
            Assert.Contains(parsed.Value!.Notes.OfType<Tap>(), tap => tap is { Lane: 0, Width: 4 });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_LegacySevenTokenSlide_DefaultsEndWidthToStartWidth()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "legacy-slide.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
            VERSION	1.01.00	1.01.00
            MUSIC	0
            SEQUENCEID	0
            DIFFICULT	00
            LEVEL	0.0
            CREATOR	test
            BPM_DEF	165.000	165.000	165.000	165.000
            MET_DEF	4	4
            RESOLUTION	384
            CLK_DEF	384
            PROGJUDGE_BPM	240.000
            PROGJUDGE_AER	0.999
            TUTORIAL	0

            BPM	0	0	165.000
            MET	0	0	4	4

            SLD	19	0	0	8	384	8
            AIR	19	384	8	8	SLD
            """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics,
                d => d.Message.Key is MsgKeys.C2s_Invalid_field or MsgKeys.C2s_Parent_not_resolved);

            var slide = Assert.Single(parsed.Value!.Notes.OfType<Slide>());
            Assert.Equal(0, slide.Lane);
            Assert.Equal(8, slide.Width);
            Assert.Equal(8, slide.EndLane);
            Assert.Equal(8, slide.EndWidth);
            Assert.Equal(1920, slide.EndTick.Original - slide.Tick.Original); // length 384 @ RES 384 → 1920 umgr ticks

            Assert.Single(parsed.Value.Notes.OfType<Air>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_UnknownVersion_WarnsAndContinues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "unknown-ver.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
            VERSION	9.99.00	9.99.00
            MUSIC	0
            SEQUENCEID	0
            DIFFICULT	03
            LEVEL	15.0
            CREATOR	test
            BPM_DEF	120.000	120.000	120.000	120.000
            MET_DEF	4	4
            RESOLUTION	384
            CLK_DEF	384
            PROGJUDGE_BPM	240.000
            PROGJUDGE_AER	0.999
            TUTORIAL	0

            MET	0	0	4	4
            BPM	0	0	120.000

            TAP	0	0	0	4
            """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Contains(parsed.Diagnostics.Diagnostics,
                d => d is { Severity: PenguinTools.Core.Diagnostic.Severity.Warning, Message.Key: MsgKeys.C2s_Unsupported_version });
            Assert.Contains(parsed.Value!.Notes.OfType<Tap>(), tap => tap is { Lane: 0, Width: 4 });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(13.0, "13")]
    [InlineData(15.4, "15")]
    [InlineData(15.5, "15+")]
    [InlineData(15.6, "15+")]
    [InlineData(8.5, "8+")]
    [InlineData(16.0, "16")]
    public void FormatPlayLevel_MatchesMagreteConvention(decimal level, string expected)
    {
        Assert.Equal(expected, MgxcChartWriter.FormatPlayLevel(level));
    }

    [Fact]
    public async Task MgxcWriter_WritesPlayLevelAndConstant()
    {
        var source = new C2sChart();
        source.Meta.Id = 2999;
        source.Meta.Title = "Melodiniq";
        source.Meta.Difficulty = Difficulty.Ultima;
        source.Meta.Level = 16.0m;
        source.Meta.Designer = "test";
        source.Meta.MainBpm = 193m;
        source.Notes.Add(new Tap { Tick = 0, Lane = 0, Width = 4 });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "plvl.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            var ascii = System.Text.Encoding.ASCII.GetString(bytes);
            Assert.Contains("plvl", ascii);
            Assert.Contains("cnst", ascii);
            // Play level "16" is written as a UTF-8 string field after plvl.
            var plvlIndex = ascii.IndexOf("plvl", StringComparison.Ordinal);
            Assert.True(plvlIndex >= 0);
            var plvlSlice = System.Text.Encoding.UTF8.GetString(bytes, plvlIndex, Math.Min(32, bytes.Length - plvlIndex));
            Assert.Contains("16", plvlSlice);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task C2sWriter_WritesHeaderFromMeta()
    {
        var source = new C2sChart();
        source.Meta.Id = 2999;
        source.Meta.Difficulty = Difficulty.Master;
        source.Meta.Level = 15.6m;
        source.Meta.Designer = "Memoir";
        source.Meta.MainBpm = 193m;
        source.Meta.BgmInitialDenominator = 4;
        source.Meta.BgmInitialNumerator = 4;
        source.Events.Add(new Bpm { Tick = 0, Value = 193m });
        source.Events.Add(new Bpm { Tick = 480, Value = 240m });
        source.Events.Add(new Bpm { Tick = 960, Value = 120.625m });
        source.Notes.Add(new Tap { Tick = 0, Lane = 0, Width = 4 });

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "header.c2s");
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var header = (await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken))
                .Take(12).ToArray();
            Assert.Equal("VERSION\t1.14.00\t1.14.00", header[0]);
            Assert.Equal("MUSIC\t2999", header[1]);
            Assert.Equal("DIFFICULT\t03", header[3]);
            Assert.Equal("LEVEL\t15.6", header[4]);
            Assert.Equal("CREATOR\tMemoir", header[5]);
            Assert.Equal("BPM_DEF\t193.000\t193.000\t240.000\t120.625", header[6]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task C2sWriter_DoesNotReuseStaleJudgeSummaryAfterNoteEdit()
    {
        var source = new C2sChart();
        source.Meta.Id = 2999;
        source.Meta.Difficulty = Difficulty.Master;
        source.Meta.Level = 15.6m;
        source.Meta.Designer = "JudgeStats";
        source.Meta.MainBpm = 120m;
        source.Meta.BgmInitialBpm = 120m;
        source.Meta.BgmInitialDenominator = 4;
        source.Meta.BgmInitialNumerator = 4;

        // Simulate statistics inherited from the source C2S.
        source.Meta.C2sJudgeTap = 1;
        source.Meta.C2sJudgeHld = 0;
        source.Meta.C2sJudgeSld = 0;
        source.Meta.C2sJudgeAir = 0;
        source.Meta.C2sJudgeFlk = 0;
        source.Meta.C2sJudgeAll = 1;

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 120m
        });

        // Original chart had one TAP.
        source.Notes.Add(new Tap
        {
            Tick = 0,
            Lane = 0,
            Width = 2
        });

        // Simulate adding another TAP in Margrete.
        source.Notes.Add(new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 2
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "judge-stale.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var text = await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains("T_JUDGE_TAP\t2", text);
            Assert.Contains("T_JUDGE_HLD\t0", text);
            Assert.Contains("T_JUDGE_SLD\t0", text);
            Assert.Contains("T_JUDGE_AIR\t0", text);
            Assert.Contains("T_JUDGE_FLK\t0", text);
            Assert.Contains("T_JUDGE_ALL\t2", text);

            Assert.DoesNotContain("T_JUDGE_TAP\t1", text);
            Assert.DoesNotContain("T_JUDGE_ALL\t1", text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task C2sWriter_RecalculatesSimpleHoldJudgeCount()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 0,
                C2sJudgeSld = 0,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 1,
                C2sJudgeHldProxyBaseline = 0
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "simple-hold-judge.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t2",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_AirAtHoldEndReplacesHoldEndJudge()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 1,
                C2sJudgeSld = 0,
                C2sJudgeAir = 1,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 3,
                C2sJudgeHldProxyBaseline = 1
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        var hold = new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        };

        source.Notes.Add(hold);

        source.Notes.Add(new Air
        {
            Tick = 240,
            Lane = 4,
            Width = 4,
            Parent = hold
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "air-at-hold-end-judge.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t2",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_CountsTapHeadsAndSlideRoots()
    {
        var source = new C2sChart();

        source.Notes.Add(new Tap
        {
            Tick = 0,
            Lane = 0,
            Width = 2
        });

        source.Notes.Add(new ExTap
        {
            Tick = 0,
            Lane = 2,
            Width = 2
        });

        source.Notes.Add(new Damage
        {
            Tick = 0,
            Lane = 4,
            Width = 2
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 6,
            Width = 2,
            EndTick = 480,
            EndLane = 6,
            EndWidth = 2
        });

        source.Notes.Add(new Flick
        {
            Tick = 0,
            Lane = 8,
            Width = 2
        });

        // Slide path #1, segment 1.
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 2,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 2,
            Joint = Joint.D
        });

        // Slide path #2.
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 8,
            Width = 2,
            EndTick = 480,
            EndLane = 10,
            EndWidth = 2,
            Joint = Joint.D
        });

        // Continuation of slide path #1.
        // This must NOT be counted as another TAP head.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 2,
            Width = 2,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 2,
            Joint = Joint.D
        });

        Assert.Equal(
            6,
            C2SJudgeSummaryCalculator.CalculateTap(source));

        Assert.Equal(
            1,
            C2SJudgeSummaryCalculator.CalculateFlick(source));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_ReconstructsOverlappingSlidesWithFifo()
    {
        var source = new C2sChart();

        // Two independent slide roots with exactly the same geometry.
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        // Continuation for the first open slide.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            EndTick = 960,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        // Continuation for the second open slide.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            EndTick = 960,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            2,
            C2SJudgeSummaryCalculator.CalculateTap(source));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_DoesNotConsumeNewRootAtSharedSlideJunction()
    {
        var source = new C2sChart();

        // Existing slide path.
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        // Continuation of the existing path.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            EndTick = 960,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        // A completely new slide happens to begin at exactly
        // the same point as the continuation above.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            EndTick = 960,
            EndLane = 0,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            2,
            C2SJudgeSummaryCalculator.CalculateTap(source));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_DoesNotCountSharedLongCarrier()
    {
        var source = new C2sChart();

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 0,
            EndWidth = 4,
            Effect = ExEffect.UP
        });

        var toUmgr = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(toUmgr.Succeeded, toUmgr.ToString());

        // Simulate the auxiliary EX head used by overlapping EX long notes.
        // It must never become another real C2S CHR judgement head.
        toUmgr.Value!.Notes.AppendChild(
            new PenguinTools.Chart.Models.umgr.ExTap
            {
                Tick = 0,
                Lane = 0,
                Width = 4,
                Effect = ExEffect.UP,
                Role = PenguinTools.Chart.Models.umgr.ExTapRole.SharedLongCarrier
            });

        var backToC2s = new C2SChartConverter(
            new C2SConvertRequest(toUmgr.Value)).Convert();

        Assert.True(backToC2s.Succeeded, backToC2s.ToString());

        Assert.Equal(
            1,
            C2SJudgeSummaryCalculator.CalculateTap(backToC2s.Value!));

        Assert.Empty(
            backToC2s.Value!.Notes.OfType<ExTap>());
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_HoldProxyHandlesPairedAir()
    {
        var plainChart = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m
            }
        };

        plainChart.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        plainChart.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        });

        Assert.Equal(
            1,
            C2SJudgeSummaryCalculator.CalculateHoldProxy(
                plainChart));

        var airChart = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m
            }
        };

        airChart.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        var hold = new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        };

        airChart.Notes.Add(hold);

        airChart.Notes.Add(new Air
        {
            Tick = 240,
            Lane = 4,
            Width = 4,
            Parent = hold
        });

        Assert.Equal(
            0,
            C2SJudgeSummaryCalculator.CalculateHoldProxy(
                airChart));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_CountsAirRoots()
    {
        var chart = new C2sChart();
        var tap = new Tap { Tick = 0, Lane = 0, Width = 4 };
        chart.Notes.Add(tap);
        chart.Notes.Add(new Air
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Parent = tap
        });

        var hold = new Hold
        {
            Tick = 480,
            Lane = 0,
            Width = 4,
            EndTick = 960,
            EndLane = 0,
            EndWidth = 4
        };
        chart.Notes.Add(hold);

        var firstHold = new AirHold
        {
            Tick = 480,
            Lane = 0,
            Width = 4,
            EndTick = 720,
            EndLane = 0,
            EndWidth = 4,
            Parent = hold
        };
        var secondHold = new AirHold
        {
            Tick = 720,
            Lane = 0,
            Width = 4,
            EndTick = 960,
            EndLane = 0,
            EndWidth = 4,
            Parent = firstHold
        };
        chart.Notes.Add(firstHold);
        chart.Notes.Add(secondHold);

        Assert.Equal(2, C2SJudgeSummaryCalculator.CalculateAirProxy(chart));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_SlideProxyUsesWholePathDuration()
    {
        var singleSegmentChart = new C2sChart();

        singleSegmentChart.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 480,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            5,
            C2SJudgeSummaryCalculator.CalculateSlideProxy(
                singleSegmentChart));

        var splitSegmentChart = new C2sChart();

        splitSegmentChart.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 6,
            EndWidth = 4,
            Joint = Joint.D
        });

        splitSegmentChart.Notes.Add(new Slide
        {
            Tick = 240,
            Lane = 6,
            Width = 4,
            EndTick = 480,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            5,
            C2SJudgeSummaryCalculator.CalculateSlideProxy(
                splitSegmentChart));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_SlideProxyUsesFifoAtSharedJunction()
    {
        var chart = new C2sChart();

        chart.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 96,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        chart.Notes.Add(new Slide
        {
            Tick = 12,
            Lane = 8,
            Width = 4,
            EndTick = 96,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        chart.Notes.Add(new Slide
        {
            Tick = 96,
            Lane = 4,
            Width = 4,
            EndTick = 108,
            EndLane = 2,
            EndWidth = 4,
            Joint = Joint.D
        });

        chart.Notes.Add(new Slide
        {
            Tick = 96,
            Lane = 4,
            Width = 4,
            EndTick = 120,
            EndLane = 6,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            4,
            C2SJudgeSummaryCalculator.CalculateSlideProxy(
                chart));
    }

    [Fact]
    public void UgcChartConverter_InitializesJudgeProxyBaselinesOnce()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 2,
                C2sJudgeSld = 3,
                C2sJudgeAir = 4,
                C2sJudgeFlk = 5,
                C2sJudgeAll = 15
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        });

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 8,
            Width = 4,
            EndTick = 480,
            EndLane = 12,
            EndWidth = 4,
            Joint = Joint.D
        });

        var first = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(
            first.Succeeded,
            first.ToString());

        Assert.Equal(
            1,
            first.Value!.Meta.C2sJudgeHldProxyBaseline);

        Assert.Equal(
            5,
            first.Value.Meta.C2sJudgeSldProxyBaseline);

        source.Meta.C2sJudgeHldProxyBaseline = 77;
        source.Meta.C2sJudgeSldProxyBaseline = 88;

        var second = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(
            second.Succeeded,
            second.ToString());

        Assert.Equal(
            77,
            second.Value!.Meta.C2sJudgeHldProxyBaseline);

        Assert.Equal(
            88,
            second.Value.Meta.C2sJudgeSldProxyBaseline);
    }

    [Fact]
    public async Task C2sWriter_AppliesHoldProxyDeltaToSourceJudgeSummary()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 500,
                C2sJudgeSld = 0,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 501,
                C2sJudgeHldProxyBaseline = 1
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 1440,
            EndLane = 4,
            EndWidth = 4
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "hold-proxy-delta.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t505",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t506",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_PreservesSourceHoldJudgeWithoutProxyBaseline()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 500,
                C2sJudgeSld = 0,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 501
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 1440,
            EndLane = 4,
            EndWidth = 4
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "hold-without-proxy-baseline.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t500",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t501",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_RecalculatesFlickJudgeAfterNoteEdit()
    {
        var source = new C2sChart();
        source.Meta.Id = 1;
        source.Meta.Difficulty = Difficulty.Master;
        source.Meta.Level = 1m;
        source.Meta.Designer = "JudgeStats";
        source.Meta.MainBpm = 120m;
        source.Meta.BgmInitialBpm = 120m;
        source.Meta.BgmInitialDenominator = 4;
        source.Meta.BgmInitialNumerator = 4;

        // Deliberately stale source statistics.
        source.Meta.C2sJudgeTap = 99;
        source.Meta.C2sJudgeHld = 0;
        source.Meta.C2sJudgeSld = 0;
        source.Meta.C2sJudgeAir = 0;
        source.Meta.C2sJudgeFlk = 99;
        source.Meta.C2sJudgeAll = 198;

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 120m
        });

        source.Notes.Add(new Flick
        {
            Tick = 0,
            Lane = 4,
            Width = 2
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "judge-flick-edit.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var text = await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains("T_JUDGE_TAP\t0", text);
            Assert.Contains("T_JUDGE_HLD\t0", text);
            Assert.Contains("T_JUDGE_SLD\t0", text);
            Assert.Contains("T_JUDGE_AIR\t0", text);
            Assert.Contains("T_JUDGE_FLK\t1", text);
            Assert.Contains("T_JUDGE_ALL\t1", text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task C2sWriter_AppliesSlideProxyDeltaToSourceJudgeSummary()
    {
        var source = new C2sChart
        {
            Meta =
            {
                C2sJudgeTap = 1,
                C2sJudgeHld = 0,
                C2sJudgeSld = 1000,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 1001,
                C2sJudgeSldProxyBaseline = 5
            }
        };

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 8,
            Width = 4,
            EndTick = 480,
            EndLane = 12,
            EndWidth = 4,
            Joint = Joint.D
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "slide-proxy-delta.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t2",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t1005",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t1007",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_PreservesSourceSlideJudgeWithoutProxyBaseline()
    {
        var source = new C2sChart
        {
            Meta =
            {
                C2sJudgeTap = 1,
                C2sJudgeHld = 0,
                C2sJudgeSld = 1000,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 1001
            }
        };

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 480,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "slide-without-proxy-baseline.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t1000",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t1001",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task MgxcWriter_WritesNeutralJudgeCopyright()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart
        {
            Meta =
            {
                Comment = "User comment",
                C2sJudgeTap = 3000,
                C2sJudgeHld = 500,
                C2sJudgeSld = 1000,
                C2sJudgeAir = 1000,
                C2sJudgeFlk = 216,
                C2sJudgeAll = 5716
            }
        };

        chart.Events.AppendChild(
            new PenguinTools.Chart.Models.umgr.BpmEvent
            {
                Tick = 0,
                Bpm = 120m
            });

        chart.Events.AppendChild(
            new PenguinTools.Chart.Models.umgr.BeatEvent
            {
                Tick = 0,
                Bar = 0,
                Numerator = 4,
                Denominator = 4
            });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "neutral-judge-copyright.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var bytes = await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken);

            var text = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains(
                "GJ2:00000BB8000001F4000003E8000003E8000000D800001654;",
                text);

            Assert.Contains(
                "T_JUDGE_TAP=3000;HLD=500;SLD=1000;AIR=1000;FLK=216;ALL=5716",
                text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_StoresLargeRoundTripMetadataInBookmarks()
    {
        var snapshot = new string('a', 40_000);

        var chart = new PenguinTools.Chart.Models.umgr.Chart
        {
            Meta =
            {
                Comment = "User comment",
                C2sJudgeTap = 1,
                C2sJudgeHld = 2,
                C2sJudgeSld = 3,
                C2sJudgeAir = 4,
                C2sJudgeFlk = 5,
                C2sJudgeAll = 15,
                C2sSlaSnapshot = snapshot
            }
        };

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "large-roundtrip-metadata.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var rawMgxc = Encoding.UTF8.GetString(
                await File.ReadAllBytesAsync(
                    path,
                    TestContext.Current.CancellationToken));

            Assert.Contains("#meta c2ssla " + snapshot, rawMgxc);

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(
                        path,
                        TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(
                parsed.Succeeded,
                parsed.ToString());

            Assert.Equal(
                "User comment",
                parsed.Value!.Meta.Comment);

            Assert.Equal(
                snapshot,
                parsed.Value.Meta.C2sSlaSnapshot);

            Assert.Equal(1, parsed.Value.Meta.C2sJudgeTap);
            Assert.Equal(2, parsed.Value.Meta.C2sJudgeHld);
            Assert.Equal(3, parsed.Value.Meta.C2sJudgeSld);
            Assert.Equal(4, parsed.Value.Meta.C2sJudgeAir);
            Assert.Equal(5, parsed.Value.Meta.C2sJudgeFlk);
            Assert.Equal(15, parsed.Value.Meta.C2sJudgeAll);

            Assert.DoesNotContain(
                parsed.Value.Events.Children
                    .OfType<PenguinTools.Chart.Models.umgr.BookmarkEvent>(),
                bookmark =>
                    C2sRoundTripComment.IsRoundTripLine(bookmark.Tag));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcJudgeSummary_PreservesCommentAndJudgeSummary()
    {
        var source = new C2sChart();

        source.Notes.Add(new Tap
        {
            Tick = 0,
            Lane = 4,
            Width = 2
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var chart = converted.Value!;

        chart.Meta.Comment = "Original comment text";
        chart.Meta.C2sJudgeTap = 1;
        chart.Meta.C2sJudgeHld = 2;
        chart.Meta.C2sJudgeSld = 3;
        chart.Meta.C2sJudgeAir = 4;
        chart.Meta.C2sJudgeFlk = 5;
        chart.Meta.C2sJudgeAll = 15;

        chart.Meta.C2sJudgeHldProxyBaseline = 11;
        chart.Meta.C2sJudgeSldProxyBaseline = 22;
        chart.Meta.C2sJudgeAirProxyBaseline = 33;

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "judge-summary.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var meta = parsed.Value!.Meta;

            Assert.Equal(
                "Original comment text",
                meta.Comment);
            Assert.DoesNotContain(
                "#meta c2sjudge",
                meta.Comment);

            Assert.Equal(1, meta.C2sJudgeTap);
            Assert.Equal(2, meta.C2sJudgeHld);
            Assert.Equal(3, meta.C2sJudgeSld);
            Assert.Equal(4, meta.C2sJudgeAir);
            Assert.Equal(5, meta.C2sJudgeFlk);
            Assert.Equal(15, meta.C2sJudgeAll);

            Assert.Equal(
                11,
                meta.C2sJudgeHldProxyBaseline);

            Assert.Equal(
                22,
                meta.C2sJudgeSldProxyBaseline);

            Assert.Equal(
                33,
                meta.C2sJudgeAirProxyBaseline);

            Assert.True(
                meta.TryGetC2sJudgeSummary(
                    out var tap,
                    out var hld,
                    out var sld,
                    out var air,
                    out var flk,
                    out var all));

            Assert.Equal(1, tap);
            Assert.Equal(2, hld);
            Assert.Equal(3, sld);
            Assert.Equal(4, air);
            Assert.Equal(5, flk);
            Assert.Equal(15, all);

            Assert.DoesNotContain(
                parsed.Value.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BookmarkEvent>(),
                bookmark => C2sRoundTripComment.IsRoundTripLine(bookmark.Tag));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcMetadataSnapshots_PreserveWithoutJudgeSummary()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();

        chart.Meta.Comment = "Original comment text";
        chart.Meta.C2sMeterDefDenominator = 4;
        chart.Meta.C2sMeterDefNumerator = 4;
        chart.Meta.C2sSlpSnapshot = "0,0,480,1.25";
        chart.Meta.C2sSlaSnapshot = "0,1,4,2,480";

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "metadata-without-judge-summary.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var meta = parsed.Value!.Meta;

            Assert.Equal(
                "Original comment text",
                meta.Comment);
            Assert.DoesNotContain(
                "#meta c2ssla",
                meta.Comment);

            Assert.Equal(4, meta.C2sMeterDefDenominator);
            Assert.Equal(4, meta.C2sMeterDefNumerator);
            Assert.Equal("0,0,480,1.25", meta.C2sSlpSnapshot);
            Assert.Equal("0,1,4,2,480", meta.C2sSlaSnapshot);

            Assert.False(
                meta.TryGetC2sJudgeSummary(
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FormatBookmarks_EmitsOneBookmarkPerConverterTag()
    {
        var snapshot = new string('a', 40_000);
        var meta = new Meta
        {
            C2sJudgeTap = 1,
            C2sJudgeHld = 2,
            C2sJudgeSld = 3,
            C2sJudgeAir = 4,
            C2sJudgeFlk = 5,
            C2sJudgeAll = 15,
            C2sSlaSnapshot = snapshot
        };

        var bookmarks = C2sRoundTripComment.FormatBookmarks(meta);

        Assert.Equal(2, bookmarks.Count);
        Assert.Equal("#meta c2sjudge 1 2 3 4 5 15", bookmarks[0]);
        Assert.Equal("#meta c2ssla " + snapshot, bookmarks[1]);
    }

    [Fact]
    public async Task MgxcWriter_PreservesUserBookmarksAndUserMetaComment()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();
        chart.Meta.Comment = "#meta date 20260813\nChart notes";
        chart.Meta.C2sJudgeTap = 1;
        chart.Meta.C2sJudgeHld = 2;
        chart.Meta.C2sJudgeSld = 3;
        chart.Meta.C2sJudgeAir = 4;
        chart.Meta.C2sJudgeFlk = 5;
        chart.Meta.C2sJudgeAll = 15;

        chart.Events.AppendChild(new PenguinTools.Chart.Models.umgr.BookmarkEvent
        {
            Id = "95B9B99580D6425BBEEF17C820542F6C",
            Tick = 1920,
            Tag = "BOOKMARK AT BAR 2",
            Rgb = "FF00AA"
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "user-bookmarks.mgxc");

        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());

            var meta = parsed.Value!.Meta;
            Assert.Contains("#meta date 20260813", meta.Comment);
            Assert.Contains("Chart notes", meta.Comment);
            Assert.DoesNotContain("#meta c2sjudge", meta.Comment);
            Assert.Equal(1, meta.C2sJudgeTap);
            Assert.Equal(15, meta.C2sJudgeAll);

            var bookmark = Assert.Single(
                parsed.Value.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BookmarkEvent>());
            Assert.Equal("BOOKMARK AT BAR 2", bookmark.Tag);
            Assert.Equal(1920, bookmark.Tick.Original);
            Assert.Equal("FF00AA", bookmark.Rgb);
            Assert.Equal("95B9B99580D6425BBEEF17C820542F6C", bookmark.Id);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcJudgeSummary_PreservesPartialProxyBaselines()
    {
        var source = new PenguinTools.Chart.Models.umgr.Chart
        {
            Meta =
            {
                C2sJudgeTap = 3000,
                C2sJudgeHld = 500,
                C2sJudgeSld = 1000,
                C2sJudgeAir = 1000,
                C2sJudgeFlk = 216,
                C2sJudgeAll = 5716,
                C2sJudgeHldProxyBaseline = 509,
                C2sJudgeSldProxyBaseline = 1005,
                C2sJudgeAirProxyBaseline = null
            }
        };

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "partial-proxy-baseline.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(
                        path,
                        TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(
                parsed.Succeeded,
                parsed.ToString());

            Assert.Equal(
                509,
                parsed.Value!.Meta.C2sJudgeHldProxyBaseline);

            Assert.Equal(
                1005,
                parsed.Value.Meta.C2sJudgeSldProxyBaseline);

            Assert.Null(
                parsed.Value.Meta.C2sJudgeAirProxyBaseline);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_AppliesAirProxyDeltaToSourceJudgeSummary()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 120m,
                BgmInitialBpm = 120m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 0,
                C2sJudgeSld = 0,
                C2sJudgeAir = 10,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 11,
                C2sJudgeAirProxyBaseline = 1
            }
        };

        var tap = new Tap
        {
            Tick = 0,
            Lane = 0,
            Width = 4
        };
        source.Notes.Add(tap);
        source.Notes.Add(new Air
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Parent = tap
        });
        source.Notes.Add(new Air
        {
            Tick = 480,
            Lane = 0,
            Width = 4,
            Parent = tap
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "air-proxy-delta.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains("T_JUDGE_AIR\t11", lines);
            Assert.Contains("T_JUDGE_ALL\t12", lines);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SlaSnapshot_IsDroppedWhenNonZeroTimelinesChange()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Timeline = 1
        });
        source.Notes.Add(new Sla
        {
            Tick = 0,
            Timeline = 1,
            Lane = 0,
            Width = 4,
            Length = 480
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());
        Assert.NotNull(converted.Value!.Meta.C2sSlaSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sSlaEditKey);

        var tap = Assert.Single(
            converted.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Tap>());
        tap.Timeline = 2;

        var roundTrip = new C2SChartConverter(
            new C2SConvertRequest(converted.Value)).Convert();

        Assert.True(roundTrip.Succeeded, roundTrip.ToString());
        Assert.Null(roundTrip.Value!.Meta.C2sSlaSnapshot);
        Assert.Null(roundTrip.Value.Meta.C2sSlaEditKey);
    }

    [Fact]
    public void SlpSnapshot_IsPreservedWhenSpeedScaleChangesOnly()
    {
        var source = new C2sChart();
        source.Events.Add(new Slp
        {
            Tick = 0,
            Timeline = 0,
            Length = 480,
            Speed = 0.750000m
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());
        Assert.NotNull(converted.Value!.Meta.C2sSlpSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sSlpEditKey);

        var speed = Assert.Single(
            converted.Value.Events.Children
                .OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>(),
            x => x.Tick.Original == 0 &&
                 x.Timeline == 0);

        speed.Speed = 0.75m;

        var roundTrip = new C2SChartConverter(
            new C2SConvertRequest(converted.Value)).Convert();

        Assert.True(roundTrip.Succeeded, roundTrip.ToString());
        Assert.NotNull(roundTrip.Value!.Meta.C2sSlpSnapshot);
        Assert.NotNull(roundTrip.Value.Meta.C2sSlpEditKey);

        var slp = Assert.Single(
            roundTrip.Value.Events.OfType<Slp>());

        Assert.Equal(0.75m, slp.Speed);
        Assert.Equal(480, slp.Length.Original);
    }

    [Fact]
    public void SlpSnapshot_IsDroppedWhenSpeedValueChanges()
    {
        var source = new C2sChart();
        source.Events.Add(new Slp
        {
            Tick = 0,
            Timeline = 0,
            Length = 480,
            Speed = 0.750000m
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());
        Assert.NotNull(converted.Value!.Meta.C2sSlpSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sSlpEditKey);

        var speed = Assert.Single(
            converted.Value.Events.Children
                .OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>(),
            x => x.Tick.Original == 0 &&
                 x.Timeline == 0);

        speed.Speed = 0.751m;

        var roundTrip = new C2SChartConverter(
            new C2SConvertRequest(converted.Value)).Convert();

        Assert.True(roundTrip.Succeeded, roundTrip.ToString());
        Assert.Null(roundTrip.Value!.Meta.C2sSlpSnapshot);
        Assert.Null(roundTrip.Value.Meta.C2sSlpEditKey);
    }

    [Fact]
    public async Task MgxcWriter_EmitsAirBeforeAirHold()
    {
        var source = new C2sChart();
        source.Meta.MainBpm = 120m;
        var hold = new Hold { Tick = 0, Lane = 2, Width = 2, EndTick = 480 };
        source.Notes.Add(hold);
        source.Notes.Add(new AirHold
        {
            Tick = 0, Lane = 2, Width = 2, EndTick = 480, EndLane = 2, EndWidth = 2,
            Parent = hold, Color = Color.DEF, Joint = Joint.D
        });

        source.Notes.Add(new Air
        {
            Tick = 0,
            Lane = 2,
            Width = 2,
            Parent = hold,
            Direction = AirDirection.IR,
            Color = Color.DEF
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "air-hold.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            await using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            Assert.Equal("MGXC", System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)));
            reader.ReadInt32();
            reader.ReadInt32();

            sbyte? previousType = null;
            var sawAirBeforeHold = false;
            while (stream.Position + 8 <= stream.Length)
            {
                var block = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                var size = reader.ReadInt32();
                var end = stream.Position + size;
                if (block == "dat2")
                {
                    while (stream.Position + 24 <= end)
                    {
                        var type = reader.ReadSByte();
                        var longAttr = reader.ReadSByte();
                        reader.ReadSByte();
                        reader.ReadSByte();
                        reader.ReadSByte();
                        reader.ReadSByte();
                        reader.ReadInt16();
                        reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadInt32();
                        if (type == 0x0A && longAttr == 0x01) reader.ReadInt32();
                        if (type == 0x08 && longAttr == 0x01 && previousType == 0x07)
                            sawAirBeforeHold = true;
                        previousType = type;
                    }
                }
                else
                {
                    stream.Position = end;
                }
            }

            Assert.True(sawAirBeforeHold);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_PairsUnresolvedAirHoldWithCarrier()
    {
        var source = new C2sChart();
        source.Meta.MainBpm = 120m;
        source.Notes.Add(new AirHold
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 3,
            Color = Color.DEF,
            Joint = Joint.D
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        Assert.Equal(
            string.Empty,
            converted.Value!.Meta.C2sAirSnapshot);

        var airHold = Assert.Single(
            converted.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirHold>());
        Assert.NotNull(airHold.PairNote);
        Assert.Equal(480, airHold.Tick.Original);
        Assert.Equal(4, airHold.Lane);
        Assert.Equal(3, airHold.Width);

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "unresolved-air-hold.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());

            var parsedAirHold = Assert.Single(
                parsed.Value!.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirHold>());

            Assert.NotNull(parsedAirHold.PairNote);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_WritesDisplayAirForAirHoldAndAirSlide()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();
        chart.Meta.MainBpm = 120m;

        Assert.Null(chart.Meta.C2sAirSnapshot);

        var holdParent = new PenguinTools.Chart.Models.umgr.Tap
        {
            Tick = 0,
            Lane = 2,
            Width = 2
        };

        var airHold = new PenguinTools.Chart.Models.umgr.AirHold
        {
            Tick = 0,
            Direction = AirDirection.UL,
            Color = Color.PNK
        };

        airHold.AppendChild(
            new PenguinTools.Chart.Models.umgr.AirHoldJoint
            {
                Tick = 480,
                Joint = Joint.D
            });

        holdParent.MakePair(airHold);

        chart.Notes.AppendChild(holdParent);
        chart.Notes.AppendChild(airHold);

        var slideParent = new PenguinTools.Chart.Models.umgr.Tap
        {
            Tick = 960,
            Lane = 4,
            Width = 3
        };

        var airSlide = new PenguinTools.Chart.Models.umgr.AirSlide
        {
            Tick = 960,
            Direction = AirDirection.UR,
            Color = Color.PNK,
            Height = 4m
        };

        airSlide.AppendChild(
            new PenguinTools.Chart.Models.umgr.AirSlideJoint
            {
                Tick = 1440,
                Lane = 4,
                Width = 3,
                Height = 4m,
                Joint = Joint.D
            });

        slideParent.MakePair(airSlide);

        chart.Notes.AppendChild(slideParent);
        chart.Notes.AppendChild(airSlide);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "no-air-arrow.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(
                        path,
                        chart))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(
                        path,
                        TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(
                parsed.Succeeded,
                parsed.ToString());

            var parsedAirHold = Assert.Single(
                parsed.Value!.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirHold>());

            var parsedAirSlide = Assert.Single(
                parsed.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirSlide>());

            Assert.Equal(AirDirection.UL, parsedAirHold.Direction);
            Assert.Equal(Color.PNK, parsedAirHold.Color);
            Assert.Equal(AirDirection.UR, parsedAirSlide.Direction);
            Assert.Equal(Color.PNK, parsedAirSlide.Color);
            Assert.Empty(
                parsed.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.Air>());
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public void OverlappingTapAirAndHoldAirSlide_EmitsAirOnlyOnTap()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();
        chart.Meta.MainBpm = 120m;

        var tap = new PenguinTools.Chart.Models.umgr.Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 4
        };
        var air = new PenguinTools.Chart.Models.umgr.Air
        {
            Direction = AirDirection.UL,
            Color = Color.DEF
        };
        tap.MakePair(air);

        var hold = new PenguinTools.Chart.Models.umgr.Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4
        };
        var tail = new PenguinTools.Chart.Models.umgr.HoldJoint
        {
            Tick = 480
        };
        hold.AppendChild(tail);

        var airSlide = new PenguinTools.Chart.Models.umgr.AirSlide
        {
            Direction = AirDirection.IR,
            Color = Color.DEF,
            Height = 80m
        };
        airSlide.AppendChild(
            new PenguinTools.Chart.Models.umgr.AirSlideJoint
            {
                Tick = 960,
                Lane = 4,
                Width = 4,
                Height = 80m,
                Joint = Joint.D
            });
        tail.MakePair(airSlide);

        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        chart.Notes.AppendChild(hold);
        chart.Notes.AppendChild(airSlide);

        var convert = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(convert.Succeeded, convert.ToString());

        var c2sAir = Assert.Single(convert.Value!.Notes.OfType<Air>());
        Assert.IsType<Tap>(c2sAir.Parent);
        Assert.Equal(AirDirection.UL, c2sAir.Direction);

        var c2sSlide = Assert.Single(convert.Value.Notes.OfType<AirSlide>());
        Assert.IsType<Hold>(c2sSlide.Parent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void C2sRoundTrip_PreservesAirCountAcrossSharedParentActions(
        int airCount)
    {
        var source = new C2sChart();

        var tap = new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 3
        };
        source.Notes.Add(tap);

        source.Notes.Add(new AirHold
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 3,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new AirHold
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            EndTick = 1440,
            EndLane = 4,
            EndWidth = 3,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        for (var i = 0; i < airCount; i++)
        {
            source.Notes.Add(new Air
            {
                Tick = 480,
                Lane = 4,
                Width = 3,
                Direction = i == 0
                    ? AirDirection.IR
                    : AirDirection.DW,
                Color = Color.DEF,
                Parent = tap
            });
        }

        var converted =
            new UgcChartConverter(
                new UgcConvertRequest(source))
                .Convert();

        Assert.True(
            converted.Succeeded,
            converted.ToString());

        var roundTrip =
            new C2SChartConverter(
                new C2SConvertRequest(converted.Value!))
                .Convert();

        Assert.True(
            roundTrip.Succeeded,
            roundTrip.ToString());

        Assert.Equal(
            airCount,
            roundTrip.Value!.Notes.OfType<Air>().Count());
    }

    [Fact]
    public void C2sRoundTrip_DoesNotDuplicateSingleAirAcrossMixedSharedParentActions()
    {
        var source = new C2sChart();

        var tap = new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 3
        };
        source.Notes.Add(tap);

        source.Notes.Add(new AirHold
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 3,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new AirSlide
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Height = 4,
            EndTick = 1440,
            EndLane = 8,
            EndWidth = 3,
            EndHeight = 8,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new Air
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Direction = AirDirection.IR,
            Color = Color.DEF,
            Parent = tap
        });

        var converted =
            new UgcChartConverter(
                new UgcConvertRequest(source))
                .Convert();

        Assert.True(
            converted.Succeeded,
            converted.ToString());

        var roundTrip =
            new C2SChartConverter(
                new C2SConvertRequest(converted.Value!))
                .Convert();

        Assert.True(
            roundTrip.Succeeded,
            roundTrip.ToString());

        Assert.Single(
            roundTrip.Value!.Notes.OfType<Air>());
    }

    [Fact]
    public async Task C2sRoundTrip_PreservesAirSnapshotAcrossMgxcSerialization()
    {
        var source = new C2sChart();

        var tap = new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 3
        };
        source.Notes.Add(tap);

        source.Notes.Add(new AirSlide
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Height = 4,
            EndTick = 1440,
            EndLane = 8,
            EndWidth = 3,
            EndHeight = 8,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new Air
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Direction = AirDirection.IR,
            Color = Color.DEF,
            Parent = tap
        });

        var converted =
            new UgcChartConverter(
                new UgcConvertRequest(source))
                .Convert();

        Assert.True(
            converted.Succeeded,
            converted.ToString());

        Assert.NotNull(converted.Value!.Meta.C2sAirSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sAirEditKey);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "air-snapshot-roundtrip.mgxc");

        try
        {
            var written =
                await new MgxcChartWriter(
                        new MgxcWriteRequest(path, converted.Value))
                    .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var parsed =
                await new MgxcParser(
                        new MgxcParseRequest(path, TestAssets.Load()),
                        TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(
                parsed.Succeeded,
                parsed.ToString());

            Assert.Equal(
                converted.Value.Meta.C2sAirSnapshot,
                parsed.Value!.Meta.C2sAirSnapshot);

            Assert.Equal(
                converted.Value.Meta.C2sAirEditKey,
                parsed.Value.Meta.C2sAirEditKey);

            var roundTrip =
                new C2SChartConverter(
                    new C2SConvertRequest(parsed.Value))
                    .Convert();

            Assert.True(
                roundTrip.Succeeded,
                roundTrip.ToString());

            var air = Assert.Single(
                roundTrip.Value!.Notes.OfType<Air>());

            Assert.Equal(480, air.Tick.Original);
            Assert.Equal(4, air.Lane);
            Assert.Equal(3, air.Width);
            Assert.Equal(AirDirection.IR, air.Direction);
            Assert.Equal(Color.DEF, air.Color);
            Assert.IsType<Tap>(air.Parent);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AirHold_WithDuplicateSlideParents_UsesUniqueTerminalSlide()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "duplicate-slide-airhold.c2s");

        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                VERSION     1.14.00        1.14.00
                RESOLUTION  384
                SLD         67      56      3       8       88      4       8
                SLD         67      56      3       8       88      4       8
                SLD         67      56      3       8       88      4       8
                SLC         67      144     4       8       240     3       8
                SLC         67      144     4       8       240     5       8
                AHD         67      144     4       8       SLD     624
                """,
                TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(
                    new C2SParseRequest(path))
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(
                parsed.Succeeded,
                parsed.ToString());

            var converted = new UgcChartConverter(
                    new UgcConvertRequest(parsed.Value!))
                .Convert();

            Assert.True(
                converted.Succeeded,
                converted.ToString());

            var roots = converted.Value!.Notes.Children;

            Assert.DoesNotContain(
                roots,
                note => note is PenguinTools.Chart.Models.umgr.ExTap
                {
                    Role: PenguinTools.Chart.Models.umgr.ExTapRole.AirActionCarrier
                });

            var slides = roots
                .OfType<PenguinTools.Chart.Models.umgr.Slide>()
                .ToArray();

            Assert.Equal(3, slides.Length);

            var pairedJoint = Assert.Single(
                slides.SelectMany(slide =>
                    slide.Children
                        .OfType<PenguinTools.Chart.Models.umgr.SlideJoint>()),
                joint =>
                    joint.PairNote is PenguinTools.Chart.Models.umgr.AirHold);

            var pairedSlide =
                Assert.IsType<PenguinTools.Chart.Models.umgr.Slide>(
                    pairedJoint.Parent);

            Assert.Same(
                pairedJoint,
                pairedSlide.LastChild);

            Assert.IsType<PenguinTools.Chart.Models.umgr.AirHold>(
                pairedJoint.PairNote);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AirSnapshot_IsDroppedWhenAirSlideChanges()
    {
        var source = new C2sChart();

        var tap = new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 3
        };
        source.Notes.Add(tap);

        source.Notes.Add(new AirSlide
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Height = 4,
            EndTick = 1440,
            EndLane = 8,
            EndWidth = 3,
            EndHeight = 8,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new Air
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Direction = AirDirection.IR,
            Color = Color.DEF,
            Parent = tap
        });

        var converted =
            new UgcChartConverter(
                new UgcConvertRequest(source))
                .Convert();

        Assert.True(
            converted.Succeeded,
            converted.ToString());

        Assert.NotNull(converted.Value!.Meta.C2sAirSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sAirEditKey);

        var airSlide = Assert.Single(
            converted.Value.Notes.Children
                .OfType<PenguinTools.Chart.Models.umgr.AirSlide>());

        airSlide.Height = 5m;

        var roundTrip =
            new C2SChartConverter(
                new C2SConvertRequest(converted.Value))
                .Convert();

        Assert.True(
            roundTrip.Succeeded,
            roundTrip.ToString());

        Assert.Null(roundTrip.Value!.Meta.C2sAirSnapshot);
        Assert.Null(roundTrip.Value.Meta.C2sAirEditKey);
    }

    [Fact]
    public async Task MgxcWriter_DoesNotOverflowBookmarksOnDenseTilCharts()
    {
        var source = new C2sChart();
        source.Meta.MainBpm = 120m;

        for (var i = 0; i < 4000; i++)
        {
            source.Notes.Add(new Tap
            {
                Tick = i * 48,
                Lane = i % 16,
                Width = 1,
                Timeline = 1
            });
        }

        source.Notes.Add(new Sla
        {
            Tick = 0,
            Timeline = 1,
            Lane = 0,
            Width = 16,
            Length = 4000 * 48
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.NotNull(converted.Value!.Meta.C2sSlaEditKey);

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "dense-til.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            Assert.True(File.Exists(path));

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(converted.Value.Meta.C2sSlaEditKey, parsed.Value!.Meta.C2sSlaEditKey);
            Assert.Equal(converted.Value.Meta.C2sSlaSnapshot, parsed.Value.Meta.C2sSlaSnapshot);
            Assert.DoesNotContain("#meta c2s", parsed.Value.Meta.Comment);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_PreservesAirCrashAxis()
    {
        var source = new C2sChart();

        source.Notes.Add(new AirCrash
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 4,
            EndHeight = 90m
        });

        source.Notes.Add(new AirCrash
        {
            Tick = 960,
            Lane = 4,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisZ,
            EndTick = 1440,
            EndLane = 6,
            EndWidth = 4,
            EndHeight = 90m
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "air-crash-axis.mgxc");

        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var crashes = parsed.Value!.Notes.Children
                .OfType<PenguinTools.Chart.Models.umgr.AirCrash>()
                .OrderBy(x => x.Tick.Original)
                .ToArray();

            Assert.Equal(2, crashes.Length);
            Assert.Equal(AirLadderAttr.AxisY, crashes[0].Attr);
            Assert.Equal(AirLadderAttr.AxisZ, crashes[1].Attr);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_WritesOnlyFinalAirCrashJointAsEnd()
    {
        var source = new C2sChart();

        source.Notes.Add(new AirCrash
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 4,
            EndHeight = 90m
        });

        source.Notes.Add(new AirCrash
        {
            Tick = 480,
            Lane = 2,
            Width = 4,
            Height = 90m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 4,
            EndHeight = 100m
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var convertedCrash = Assert.Single(
            converted.Value!.Notes.Children
                .OfType<PenguinTools.Chart.Models.umgr.AirCrash>());

        Assert.Equal(
            2,
            convertedCrash.Children
                .OfType<PenguinTools.Chart.Models.umgr.AirCrashJoint>()
                .Count());

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "air-crash-joints.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var airCrashLongAttrs = new List<sbyte>();

            await using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            Assert.Equal(
                "MGXC",
                System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)));

            reader.ReadInt32(); // 文件大小
            reader.ReadInt32(); // MGXC 版本

            while (stream.Position + 8 <= stream.Length)
            {
                var blockName = System.Text.Encoding.ASCII.GetString(
                    reader.ReadBytes(4));

                var blockSize = reader.ReadInt32();
                var blockEnd = stream.Position + blockSize;

                if (blockName != "dat2")
                {
                    stream.Position = blockEnd;
                    continue;
                }

                while (stream.Position + 20 <= blockEnd)
                {
                    var noteType = reader.ReadSByte();
                    var longAttr = reader.ReadSByte();

                    reader.ReadSByte(); // direction
                    reader.ReadSByte(); // exAttr
                    reader.ReadSByte(); // variation
                    reader.ReadSByte(); // lane
                    reader.ReadInt16(); // width
                    reader.ReadInt32(); // height
                    reader.ReadInt32(); // tick
                    reader.ReadInt32(); // timeline

                    if (noteType == 0x0A)
                        airCrashLongAttrs.Add(longAttr);

                    // AirCrush Begin 比普通记录多一个 density/option 字段。
                    if (noteType == 0x0A && longAttr == 0x01)
                        reader.ReadInt32();
                }

                stream.Position = blockEnd;
            }

            Assert.Equal(
                new sbyte[] { 0x01, 0x03, 0x05 },
                airCrashLongAttrs.ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_ReadsAirLadderAttr_AndWriterRoundTrips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ald-attr.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.15.00	1.15.00
                MUSIC	0
                SEQUENCEID	0
                DIFFICULT	03
                LEVEL	15.0
                CREATOR	test
                BPM_DEF	120.000	120.000	120.000	120.000
                MET_DEF	4	4
                RESOLUTION	384
                CLK_DEF	384
                PROGJUDGE_BPM	240.000
                PROGJUDGE_AER	0.999
                TUTORIAL	0

                MET	0	0	4	4
                BPM	0	0	120.000

                ALD	0	0	0	4	0	1.0	96	0	4	1.0	DEF	DEF
                ALD	1	0	2	2	38400	5.0	48	3	2	5.0	NON	AxisY
                ALD	2	0	4	4	1	2.0	24	5	4	3.0	RED	Trace
                ALD	3	0	6	2	0	1.0	12	7	2	1.0	CYN	AxisZ
                """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var crashes = parsed.Value!.Notes.OfType<AirCrash>().OrderBy(x => x.Tick.Original).ToArray();
            Assert.Equal(4, crashes.Length);
            Assert.Equal(AirLadderAttr.DEF, crashes[0].Attr);
            Assert.Equal(AirLadderAttr.AxisY, crashes[1].Attr);
            Assert.Equal(Color.NON, crashes[1].Color);
            Assert.Equal(AirLadderAttr.Trace, crashes[2].Attr);
            Assert.Equal(AirLadderAttr.AxisZ, crashes[3].Attr);

            var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Contains(converted.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>(),
                x => x is { Attr: AirLadderAttr.AxisY, Color: Color.NON });
            Assert.Contains(converted.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>(),
                x => x.Attr == AirLadderAttr.Trace);

            var roundtripPath = Path.Combine(directory, "ald-attr-out.c2s");
            var back = new C2SChartConverter(new C2SConvertRequest(converted.Value)).Convert();
            Assert.True(back.Succeeded, back.ToString());
            var written = await new C2SChartWriter(new C2SWriteRequest(roundtripPath, back.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(roundtripPath, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.15.00\t1.15.00", lines[0]);
            // Global 1.15: every ALD line carries ATTR, including DEF.
            Assert.Contains(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal) && line.EndsWith("\tDEF\tDEF", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal) && line.EndsWith("\tNON\tAxisY", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal) && line.EndsWith("\tRED\tTrace", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal) && line.EndsWith("\tCYN\tAxisZ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
    [Fact]
    public void AirCrashSegments_AreReconstructedAsSinglePath()
    {
        var source = new C2sChart();

        source.Notes.Add(new AirCrash
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 4,
            EndHeight = 90m
        });

        source.Notes.Add(new AirCrash
        {
            Tick = 480,
            Lane = 2,
            Width = 4,
            Height = 90m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 4,
            EndHeight = 100m
        });

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());

        var crash = Assert.Single(
            result.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>());

        Assert.Equal(AirLadderAttr.AxisY, crash.Attr);

        var joints = crash.Children
            .OfType<PenguinTools.Chart.Models.umgr.AirCrashJoint>()
            .ToArray();

        Assert.Equal(2, joints.Length);
        Assert.Equal(480, joints[0].Tick.Original);
        Assert.Equal(960, joints[1].Tick.Original);
    }
    [Fact]
    public void AirCrashSegments_WithDifferentAxis_StartSeparatePaths()
    {
        var source = new C2sChart();

        source.Notes.Add(new AirCrash
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 4,
            EndHeight = 90m
        });

        source.Notes.Add(new AirCrash
        {
            Tick = 480,
            Lane = 2,
            Width = 4,
            Height = 90m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisZ,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 4,
            EndHeight = 100m
        });

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());

        var crashes = result.Value!.Notes.Children
            .OfType<PenguinTools.Chart.Models.umgr.AirCrash>()
            .ToArray();

        Assert.Equal(2, crashes.Length);
    }
    [Fact]
    public void C2sBaseHeads_AreReconstructedByPaths()
    {
        var source = new C2sChart();

        source.Notes.Add(new Tap
        {
            Tick = 0, Lane = 0, Width = 1
        });

        source.Notes.Add(new ExTap
        {
            Tick = 100, Lane = 1, Width = 1, Effect = ExEffect.UP
        });

        source.Notes.Add(new Damage
        {
            Tick = 200, Lane = 2, Width = 1
        });

        source.Notes.Add(new Hold
        {
            Tick = 300, Lane = 3, Width = 1,
            EndTick = 400, EndLane = 3, EndWidth = 1
        });

        source.Notes.Add(new Hold
        {
            Tick = 500, Lane = 4, Width = 1,
            EndTick = 600, EndLane = 4, EndWidth = 1,
            Effect = ExEffect.UP
        });

        source.Notes.Add(new Slide
        {
            Tick = 700, Lane = 5, Width = 1,
            EndTick = 800, EndLane = 6, EndWidth = 1,
            Joint = Joint.D
        });

        source.Notes.Add(new Slide
        {
            Tick = 800, Lane = 6, Width = 1,
            EndTick = 900, EndLane = 7, EndWidth = 1,
            Joint = Joint.D
        });

        source.Notes.Add(new Slide
        {
            Tick = 1000, Lane = 9, Width = 1,
            EndTick = 1100, EndLane = 10, EndWidth = 1,
            Joint = Joint.D
        });

        source.Notes.Add(new Flick
        {
            Tick = 1200, Lane = 11, Width = 1
        });

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());

        var roots = result.Value!.Notes.Children;

        var baseHeadCount = roots.Count(note =>
            note is PenguinTools.Chart.Models.umgr.Tap
                or PenguinTools.Chart.Models.umgr.ExTap
                or PenguinTools.Chart.Models.umgr.Damage
                or PenguinTools.Chart.Models.umgr.Hold
                or PenguinTools.Chart.Models.umgr.Slide);

        Assert.Equal(7, baseHeadCount);
    }
    [Fact]
    public async Task Writer_KeepsV114_WhenAldAttrIsDefault()
    {
        var source = new C2sChart { Meta = new Meta { MainBpm = 120m } };
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Notes.Add(new AirCrash
        {
            Tick = 0, Lane = 0, Width = 4, EndTick = 480, EndLane = 0, EndWidth = 4,
            Height = 0m, EndHeight = 0m, Density = 0, Color = Color.CYN, Attr = AirLadderAttr.DEF
        });

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ald-v114.c2s");
        try
        {
            Assert.False(C2SChartWriter.NeedsV115(source));
            var written = await new C2SChartWriter(new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.14.00\t1.14.00", lines[0]);
            var ald = Assert.Single(lines, l => l.StartsWith("ALD\t", StringComparison.Ordinal));
            Assert.EndsWith("\tCYN", ald);
            Assert.DoesNotContain("AxisY", ald);
            Assert.False(ald.EndsWith("\tDEF", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Writer_SwitchesToV115_ForNoLineSlide()
    {
        var source = new C2sChart { Meta = new Meta { MainBpm = 120m } };
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Notes.Add(new Slide
        {
            Tick = 0, Lane = 0, Width = 4, EndTick = 480, EndLane = 2, EndWidth = 4,
            Joint = Joint.D, NoLine = true
        });
        source.Notes.Add(new Slide
        {
            Tick = 480, Lane = 2, Width = 4, EndTick = 960, EndLane = 4, EndWidth = 4,
            Joint = Joint.D, NoLine = false
        });
        source.Notes.Add(new AirCrash
        {
            Tick = 0, Lane = 4, Width = 2, EndTick = 96, EndLane = 4, EndWidth = 2,
            Height = 0m, EndHeight = 0m, Density = 0, Color = Color.DEF, Attr = AirLadderAttr.DEF
        });

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "noline-v115.c2s");
        try
        {
            Assert.True(C2SChartWriter.NeedsV115(source));
            var written = await new C2SChartWriter(new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.15.00\t1.15.00", lines[0]);
            Assert.Contains(lines, l => l.Contains("\tNCL", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("\tSLD", StringComparison.Ordinal));
            // Global 1.15: default ALD still emits ATTR.
            Assert.Contains(lines, l => l.StartsWith("ALD\t", StringComparison.Ordinal) && l.EndsWith("\tDEF\tDEF", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Writer_OmitsSlideLinkMarker_OnV114()
    {
        var source = new C2sChart { Meta = new Meta { MainBpm = 120m } };
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Notes.Add(new Slide
        {
            Tick = 0, Lane = 0, Width = 4, EndTick = 480, EndLane = 2, EndWidth = 4,
            Joint = Joint.D, Effect = ExEffect.UP
        });

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "slide-v114.c2s");
        try
        {
            Assert.False(C2SChartWriter.NeedsV115(source));
            var written = await new C2SChartWriter(new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.14.00\t1.14.00", lines[0]);
            var slide = Assert.Single(lines, l => l.StartsWith("SXD\t", StringComparison.Ordinal) || l.StartsWith("SLD\t", StringComparison.Ordinal));
            Assert.DoesNotContain("\tSLD\t", slide);
            Assert.DoesNotContain("\tNCL", slide);
            Assert.EndsWith("\tUP", slide);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var note = Assert.Single(parsed.Value!.Notes.OfType<Slide>());
            Assert.False(note.NoLine);
            Assert.Equal(ExEffect.UP, note.Effect);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Writer_EmitsOfficialFlickTrailingMarker()
    {
        var source = new C2sChart { Meta = new Meta { MainBpm = 120m } };
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Notes.Add(new Flick
        {
            Tick = 0,
            Lane = 3,
            Width = 5
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "flick-marker.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            var flick = Assert.Single(
                lines,
                line => line.StartsWith("FLK\t", StringComparison.Ordinal));

            Assert.Equal("FLK\t0\t0\t3\t5\tL", flick);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_LegacyAldWithoutAttr_DefaultsToDef()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ald-legacy.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.13.00	1.13.00
                RESOLUTION	384
                ALD	0	0	0	4	0	1.0	96	0	4	1.0	CYN
                """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var crash = Assert.Single(parsed.Value!.Notes.OfType<AirCrash>());
            Assert.Equal(Color.CYN, crash.Color);
            Assert.Equal(AirLadderAttr.DEF, crash.Attr);

            var outPath = Path.Combine(directory, "ald-legacy-out.c2s");
            var written = await new C2SChartWriter(new C2SWriteRequest(outPath, parsed.Value))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            var lines = await File.ReadAllLinesAsync(outPath, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.14.00\t1.14.00", lines[0]);
            var ald = Assert.Single(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal));
            Assert.EndsWith("\tCYN", ald);
            Assert.DoesNotContain("AxisY", ald);
            Assert.False(ald.EndsWith("\tDEF", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AirActions_OnSameParent_PreserveSourceAirOrder()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "air-action-order.c2s");

        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.13.00	1.13.00
                RESOLUTION	384
                TAP	0	0	4	4
                AHD	0	0	4	4	TAP	96
                ASD	0	0	4	4	TAP	5.0	96	6	4	5.0	DEF
                AUL	0	0	4	4	TAP
                ADR	0	0	4	4	TAP
                """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(
                new C2SParseRequest(path))
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var sourceAirs = parsed.Value!.Notes
                .OfType<Air>()
                .ToArray();

            Assert.Equal(2, sourceAirs.Length);

            var converted = new UgcChartConverter(
                new UgcConvertRequest(parsed.Value))
                .Convert();

            Assert.True(converted.Succeeded, converted.ToString());

            var airHold = Assert.Single(
                converted.Value!.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirHold>());

            var airSlide = Assert.Single(
                converted.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirSlide>());

            Assert.Equal(sourceAirs[0].Direction, airHold.Direction);
            Assert.Equal(sourceAirs[1].Direction, airSlide.Direction);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
