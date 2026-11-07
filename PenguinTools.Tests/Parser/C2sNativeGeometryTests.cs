using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Core;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sNativeGeometryTests
{
    [Theory]
    [InlineData(17, 16)]
    [InlineData(100, 16)]
    public async Task WidthIsClampedBeforeAirParentMatching(int raw, int width)
    {
        var chart = await Parse($"TAP\t0\t0\t0\t{raw}\nAIR\t0\t0\t0\t{width}\tTAP\tDEF");
        var tap = Assert.Single(chart.Notes.OfType<C.Tap>());
        Assert.Equal(width, tap.Width);
        Assert.Same(tap, Assert.Single(chart.Notes.OfType<C.Air>()).Parent);
    }

    [Theory]
    [InlineData("TAP 0 0 0 0")]
    [InlineData("TAP 0 0 0 -1")]
    [InlineData("CHR 0 0 0 0 UP")]
    [InlineData("MNE 0 0 0 0")]
    [InlineData("FLK 0 0 0 0 L")]
    [InlineData("HLD 0 0 0 0 96")]
    [InlineData("SLD 0 0 0 4 96 4 0 SLD")]
    [InlineData("SLC 0 0 0 4 96 4 -1 SLD")]
    [InlineData("SXD 0 0 0 4 96 4 0 SLD UP")]
    [InlineData("SLC 0 0 0 0 96 4 4 SLD")]
    [InlineData("SXC 0 0 0 0 96 4 4 SLD UP")]
    [InlineData("TAP 0 0 0 4\nAIR 0 0 0 0 TAP DEF")]
    [InlineData("TAP 0 0 0 4\nASD 0 0 0 4 TAP 1 96 4 0 2 DEF")]
    [InlineData("TAP 0 0 0 4\nASC 0 0 0 4 TAP 1 96 4 -1 2 DEF")]
    [InlineData("ASC 0 0 0 0 ASC 1 96 4 4 2 DEF")]
    [InlineData("TAP 0 0 0 4\nAHD 0 0 0 0 TAP 96 DEF")]
    [InlineData("ALD 0 0 0 4 24 1 96 4 0 2 DEF")]
    public async Task ZeroWidthPlayableNotes_AreRejectedInsteadOfBeingWidened(string body)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "VERSION 1.15.00 1.15.00\nRESOLUTION 384\nBPM_DEF 120 120 120 120\nMET_DEF 4 4\n" + body, ct);
            var parsed = await new C2SParser(new(path)).ParseAsync(ct);
            Assert.False(parsed.Succeeded);
            Assert.Contains(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Invalid_note_width);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LongNoteWidths_AreClampedBeforePathAndAirMatching()
    {
        var chart = await Parse("""
            SLD 0 0 0 17 96 4 17 SLD
            SLD 0 96 4 16 96 8 17 SLD
            ASD 0 192 8 16 SLD 5 96 4 17 10 DEF
            ASD 0 288 4 16 ASD 10 96 0 17 15 DEF
            """);
        var slides = chart.Notes.OfType<C.Slide>().ToArray();
        Assert.Equal([16, 16], slides.Select(n => n.Width));
        Assert.Equal([16, 16], slides.Select(n => n.EndWidth));
        var air = chart.Notes.OfType<C.AirSlide>().ToArray();
        Assert.Same(slides[1], air[0].Parent);
        Assert.Same(air[0], air[1].Parent);
        Assert.Equal([16, 16], air.Select(n => n.EndWidth));
        var editor = new UgcChartConverter(new(chart)).Convert();
        Assert.True(editor.Succeeded);
        Assert.Equal(2, Assert.Single(editor.Value!.Notes.Children.OfType<U.Slide>()).Children.Count);
        Assert.Equal(2, Assert.Single(editor.Value.Notes.Children.OfType<U.AirSlide>()).Children.Count);
    }

    [Theory]
    [InlineData("SLC", "SLD", "SLD")]
    [InlineData("SLC", "SLD", "NCL")]
    [InlineData("SXC", "SXD", "SLD")]
    [InlineData("SXC", "SXD", "NCL")]
    public async Task ZeroAndUnitWidthSlideTails_RemainDifferentPaths(string control, string end, string line)
    {
        var chart = await Parse($"""
            {control} 0 0 0 4 96 4 0 {line} UP
            {control} 0 0 8 4 96 4 1 {line} UP
            {end} 0 96 4 1 96 8 4 {line} UP
            {end} 0 96 4 0 96 0 4 {line} UP
            AIR 0 192 0 4 SLD DEF
            AIR 0 192 8 4 SLD PNK
            """);
        var paths = C2sSlidePaths.Build(chart.Notes.OfType<C.Slide>()).ToArray();
        Assert.Equal(2, paths.Length);
        Assert.All(paths, path => Assert.Equal(2, path.Length));
        Assert.Equal([0, 8], paths.Select(path => path[^1].EndLane));
        var converted = new UgcChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var roots = converted.Value!.Notes.Children.OfType<U.Slide>().ToArray();
        Assert.Equal([0, 1], roots.Select(n => n.FirstChild!.Width));
        Assert.Equal([0, 8], roots.Select(n => n.LastChild!.Lane));
        Assert.All(converted.Value.Notes.Children.OfType<U.Air>(), air =>
            Assert.Equal(air.Lane, Assert.IsType<U.SlideJoint>(air.PairNote).Parent!.Lane));
    }

    [Fact]
    public async Task ZeroAndUnitWidthAirSlideTails_RetainTheirParents()
    {
        var chart = await Parse("""
            TAP 0 0 0 4
            TAP 0 0 8 4
            ASC 0 0 0 4 TAP 5 96 4 0 5 DEF
            ASC 0 0 8 4 TAP 5 96 4 1 5 PNK
            ASD 0 96 4 1 ASC 5 96 8 4 5 PNK
            ASD 0 96 4 0 ASC 5 96 0 4 5 DEF
            """);
        var segments = chart.Notes.OfType<C.AirSlide>().ToArray();
        Assert.Same(segments[0], segments[3].Parent);
        Assert.Same(segments[1], segments[2].Parent);
        var converted = new UgcChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var roots = converted.Value!.Notes.Children.OfType<U.AirSlide>().ToArray();
        Assert.Equal([0, 1], roots.Select(n => n.FirstChild!.Width));
        Assert.Equal([0, 8], roots.Select(n => n.LastChild!.Lane));
        Assert.Equal([4, 4], roots.Select(n => n.LastChild!.Width));
        Assert.All(roots, n => Assert.Equal(n.PairNote!.Lane, n.LastChild!.Lane));
    }

    [Theory]
    [InlineData(-17, 4, 0, 3)]
    [InlineData(-20, 4, 0, 1)]
    [InlineData(-16, 16, -16, 16)]
    [InlineData(30, 4, 30, 2)]
    [InlineData(32, 4, 31, 1)]
    [InlineData(40, 4, 31, 1)]
    public async Task RootGeometry_UsesGameBoundsAndRetainsAir(int lane, int width, int expectedLane, int expectedWidth)
    {
        var source = await Parse($"TAP\t0\t0\t{lane}\t{width}\nAIR\t0\t0\t{lane}\t{width}\tTAP\tDEF");
        var result = new UgcChartConverter(new(source)).Convert();
        Assert.True(result.Succeeded);
        var editor = result.Value!;
        var tap = Assert.Single(editor.Notes.Children.OfType<U.Tap>());
        Assert.Equal((expectedLane, expectedWidth), (tap.Lane, tap.Width));
        Assert.Same(tap, Assert.Single(editor.Notes.Children.OfType<U.Air>()).PairNote);
        var converted = new C2SChartConverter(new(editor)).Convert();
        Assert.True(converted.Succeeded);
        var air = Assert.Single(converted.Value!.Notes.OfType<C.Air>());
        Assert.Equal((expectedLane, expectedWidth), (air.Lane, air.Width));
        Assert.Same(Assert.Single(converted.Value.Notes.OfType<C.Tap>()), air.Parent);
    }

    [Fact]
    public async Task LaneBounds_AreAppliedAfterSlideRelationshipsAreEstablished()
    {
        var source = await Parse("""
            SLD 0 0 -20 8 96 40 4 SLD
            SLD 0 96 40 4 96 -20 4 SLD
            SLD 0 96 28 4 96 16 4 SLD
            AIR 0 192 -20 4 SLD DEF
            """);
        var result = new UgcChartConverter(new(source)).Convert();
        Assert.True(result.Succeeded);
        var slides = result.Value!.Notes.Children.OfType<U.Slide>().ToArray();
        Assert.Equal(2, slides.Length);
        Assert.Equal((0, 4), (slides[0].Lane, slides[0].Width));
        Assert.Equal([28, -16], slides[0].Children.Select(n => n.Lane));
        Assert.Equal(28, slides[1].Lane);
        Assert.Same(slides[0].LastChild, Assert.Single(result.Value.Notes.Children.OfType<U.Air>()).PairNote);
        var converted = new C2SChartConverter(new(result.Value)).Convert();
        Assert.True(converted.Succeeded);
        Assert.Equal(-16, Assert.Single(converted.Value!.Notes.OfType<C.Air>()).Lane);
        Assert.Equal(2, C2sSlidePaths.Build(converted.Value.Notes.OfType<C.Slide>()).Count());
    }

    [Fact]
    public async Task SlaCoverage_UsesTheSameBoundsAsTheGame()
    {
        var source = await Parse("""
            SLP 0 0 384 2 1
            SLA 0 0 -20 8 384 1
            TAP 0 0 -20 8
            TAP 0 96 2 2
            """);
        var result = new UgcChartConverter(new(source)).Convert();
        Assert.True(result.Succeeded);
        Assert.All(result.Value!.Notes.Children.OfType<U.Tap>(), tap => Assert.Equal(1, tap.Timeline));
    }

    private static async Task<C.Chart> Parse(string body)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "VERSION\t1.15.00\t1.15.00\nBPM\t0\t0\t120\n" + body, ct);
            var parsed = await new C2SParser(new(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }
}
