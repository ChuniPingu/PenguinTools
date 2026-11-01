using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sTouchingSlideTests
{
    [Theory]
    [InlineData(".mgxc", false)]
    [InlineData(".ugc", false)]
    [InlineData(".mgxc", true)]
    [InlineData(".ugc", true)]
    public async Task IndependentTouchingSlides_KeepHeadsEffectsAndAirParents(string extension, bool noLine)
    {
        var chart = NewChart();
        var first = AddSlide(chart, 0, 0, (480, 4, 4));
        var second = AddSlide(chart, 480, 4, (960, 8, 4));
        first.NoLine = second.NoLine = noLine;
        second.Effect = ExEffect.UP;
        AddAirHold(chart, first, 960);
        AddAirHold(chart, second, 1440);

        var editorPath = TestTempPaths.Create(extension);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = extension == ".mgxc"
                ? await new MgxcChartWriter(new MgxcWriteRequest(editorPath, chart)).WriteAsync(ct)
                : await new UgcChartWriter(editorPath, chart).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = extension == ".mgxc"
                ? await new MgxcParser(new MgxcParseRequest(editorPath, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new UgcParseRequest(editorPath, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());

            var result = await ThroughC2s(parsed.Value!);
            result = await ThroughC2s(result);
            var slides = result.Notes.Children.OfType<U.Slide>().OrderBy(s => s.Tick.Original).ToArray();
            Assert.Equal(2, slides.Length);
            Assert.All(slides, s => Assert.Single(s.Children));
            Assert.Equal([0, 480], slides.Select(s => s.Tick.Original));
            Assert.Equal([4, 8], slides.Select(s => s.LastChild!.Lane));
            Assert.All(slides, s => Assert.Equal(noLine, s.NoLine));
            Assert.Null(slides[0].Effect);
            Assert.Equal(ExEffect.UP, slides[1].Effect);
            var airs = result.Notes.Children.OfType<U.AirHold>().OrderBy(a => a.Tick.Original).ToArray();
            Assert.Equal(2, airs.Length);
            Assert.Same(slides[0].LastChild, airs[0].PairNote);
            Assert.Same(slides[1].LastChild, airs[1].PairNote);
        }
        finally { File.Delete(editorPath); }
    }

    [Fact]
    public async Task ThreeTouchingSlides_WriteEveryParentBeforeItsAir()
    {
        var chart = NewChart();
        for (var i = 0; i < 3; i++)
        {
            var slide = AddSlide(chart, i * 480, i * 4, ((i + 1) * 480, (i + 1) * 4, 4));
            AddAirHold(chart, slide, (i + 2) * 480);
        }
        var result = await ThroughC2s(chart);
        Assert.Equal(3, result.Notes.Children.OfType<U.Slide>().Count());
        Assert.Equal(3, result.Notes.Children.OfType<U.AirHold>().Count());
    }

    [Fact]
    public async Task PromotedRoot_KeepsSharedContinuationsAndAirInCreationOrder()
    {
        var chart = NewChart();
        AddSlide(chart, 0, 0, (480, 4, 4));
        var crossing = AddSlide(chart, 0, 12, (960, 7, 2), (1440, 0, 4));
        var touching = AddSlide(chart, 480, 4, (960, 7, 2), (1440, 0, 4));
        AddAirHold(chart, crossing, 1920);
        AddAirHold(chart, touching, 2400);

        var result = await ThroughC2s(chart);
        var slides = result.Notes.Children.OfType<U.Slide>().ToArray();
        Assert.Equal(3, slides.Length);
        var actualCrossing = Assert.Single(slides, s => s.Lane == 12);
        var actualTouching = Assert.Single(slides, s => s.Tick.Original == 480);
        Assert.Equal([7, 0], actualCrossing.Children.Select(n => n.Lane));
        Assert.Equal([7, 0], actualTouching.Children.Select(n => n.Lane));
        var airs = result.Notes.Children.OfType<U.AirHold>().OrderBy(a => a.LastChild!.Tick.Original).ToArray();
        Assert.Same(actualCrossing.LastChild, airs[0].PairNote);
        Assert.Same(actualTouching.LastChild, airs[1].PairNote);
    }

    [Fact]
    public async Task TouchingRoot_DoesNotStealAnotherPathsLaterContinuation()
    {
        var chart = NewChart();
        AddSlide(chart, 0, 0, (480, 4, 4));
        AddSlide(chart, 480, 4, (960, 7, 2));
        var crossing = AddSlide(chart, 0, 12, (960, 7, 2), (1440, 0, 4));
        AddAirHold(chart, crossing, 1920);

        var result = await ThroughC2s(chart);
        var slides = result.Notes.Children.OfType<U.Slide>().ToArray();
        Assert.Equal(3, slides.Length);
        Assert.Equal([7, 0], Assert.Single(slides, s => s.Lane == 12).Children.Select(n => n.Lane));
        Assert.Single(Assert.Single(slides, s => s.Tick.Original == 480).Children);
        Assert.Equal(12, Assert.Single(result.Notes.Children.OfType<U.AirHold>()).PairNote!.Parent!.Lane);
    }

    [Fact]
    public async Task C2sWriter_KeepsNonChronologicalSlideOrder()
    {
        var chart = new C.Chart();
        chart.Notes.AddRange([
            new C.Slide { Tick = 480, Lane = 4, Width = 4, EndTick = 960, EndLane = 8, EndWidth = 4, Joint = Joint.D },
            new C.Slide { Tick = 0, Lane = 0, Width = 4, EndTick = 480, EndLane = 4, EndWidth = 4, Joint = Joint.D }
        ]);
        var result = await WriteAndRead(chart);
        Assert.Equal(2, result.Notes.Children.OfType<U.Slide>().Count());
    }

    private static U.Chart NewChart()
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Meta.ExVersion = 1;
        chart.Meta.ExLongEnabled = true;
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent());
        return chart;
    }

    private static U.Slide AddSlide(U.Chart chart, int tick, int lane, params (int Tick, int Lane, int Width)[] points)
    {
        var slide = new U.Slide { Tick = tick, Lane = lane, Width = 4 };
        foreach (var (endTick, endLane, endWidth) in points)
        {
            slide.AppendChild(new U.SlideJoint { Tick = endTick, Lane = endLane, Width = endWidth });
        }
        chart.Notes.AppendChild(slide);
        return slide;
    }

    private static void AddAirHold(U.Chart chart, U.Slide parent, int endTick)
    {
        var air = new U.AirHold();
        air.AppendChild(new U.AirHoldJoint { Tick = endTick, Joint = Joint.C });
        Assert.IsType<U.SlideJoint>(parent.LastChild).MakePair(air);
        chart.Notes.AppendChild(air);
    }

    private static async Task<U.Chart> ThroughC2s(U.Chart chart)
    {
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.DoesNotContain(converted.Diagnostics.Diagnostics, d => d.Message.Key == "diag.mg.ambiguous_c2s_slide_path");
        return await WriteAndRead(converted.Value!);
    }

    private static async Task<U.Chart> WriteAndRead(C.Chart chart)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);

            // Parent records must already exist in a streaming C2S reader.
            var indices = parsed.Value!.Notes.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
            foreach (var air in parsed.Value.Notes.OfType<Chart.Models.c2s.IPairable>())
            {
                Assert.NotNull(air.Parent);
                Assert.True(indices[air.Parent] < indices[(Chart.Models.c2s.Note)air]);
            }
            var result = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
            Assert.True(result.Succeeded, result.ToString());
            return result.Value!;
        }
        finally { File.Delete(path); }
    }
}
