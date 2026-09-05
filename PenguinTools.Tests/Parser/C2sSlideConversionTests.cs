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

public sealed class C2sSlideConversionTests
{
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
}
