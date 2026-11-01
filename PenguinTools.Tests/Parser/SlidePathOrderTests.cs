using PenguinTools.Chart;
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

public sealed class SlidePathOrderTests
{
    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task EditedAirParents_AreNotOverwrittenBySnapshots(string extension)
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            SLC 0 0 0 2 48 1 2 SLD
            SLC 0 0 0 2 48 13 2 SLD
            SLD 0 48 1 2 48 7 2 SLD
            SLD 0 48 13 2 48 7 2 SLD
            AIR 0 96 7 2 SLD DEF
            AUL 0 96 7 2 SLD DEF
            """);
        chart = await ThroughEditor(chart, extension);
        var airs = chart.Notes.Children.OfType<U.Air>().OrderBy(a => a.Direction).ToArray();
        var firstParent = airs[0].PairNote!;
        var secondParent = airs[1].PairNote!;
        var expected = airs.ToDictionary(a => a.Direction, a => a.PairNote!.Parent!.Children[0].Lane);
        airs[0].MakePair(secondParent);
        airs[1].MakePair(firstParent);
        var result = await ThroughC2s(chart);
        foreach (var air in result.Notes.Children.OfType<U.Air>())
        {
            Assert.NotEqual(expected[air.Direction], air.PairNote!.Parent!.Children[0].Lane);
        }
    }

    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task AirCrash_SharedPointKeepsCreatedPathOrder(string extension)
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            ALD 0 0 0 1 0 1.0 48 1 1 2.0 DEF DEF
            ALD 0 0 14 1 0 1.0 96 7 1 3.0 DEF DEF
            ALD 0 48 1 1 0 2.0 48 7 1 3.0 DEF DEF
            ALD 0 96 7 1 0 3.0 96 0 1 5.0 DEF DEF
            ALD 0 96 7 1 0 3.0 96 14 1 6.0 DEF DEF
            """);
        var result = await ThroughC2s(await ThroughEditor(chart, extension));
        var paths = result.Notes.Children.OfType<U.AirCrash>().OrderBy(c => c.Lane).ToArray();
        Assert.Equal(2, paths.Length);
        Assert.Equal([1, 7, 0], paths[0].Children.Select(n => n.Lane));
        Assert.Equal([7, 14], paths[1].Children.Select(n => n.Lane));
        Assert.Equal(80, Assert.IsType<U.AirCrashJoint>(paths[0].LastChild).Height);
        Assert.Equal(100, Assert.IsType<U.AirCrashJoint>(paths[1].LastChild).Height);
    }

    [Fact]
    public async Task AirSlide_HeightIsRequiredForContinuation()
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            TAP 0 0 0 2
            TAP 0 0 12 2
            ASC 0 0 0 2 TAP 1.0 96 7 2 2.0 DEF
            ASC 0 0 12 2 TAP 1.0 96 7 2 3.0 DEF
            ASD 0 96 7 2 ASC 3.0 96 12 2 5.0 DEF
            ASD 0 96 7 2 ASC 2.0 96 0 2 4.0 DEF
            """);
        var result = await ThroughC2s(chart);
        var paths = result.Notes.Children.OfType<U.AirSlide>().OrderBy(s => s.Lane).ToArray();
        Assert.Equal([0, 12], paths.Select(s => s.LastChild!.Lane));
        Assert.Equal<decimal>([60, 80], paths.Select(s => Assert.IsType<U.AirSlideJoint>(s.LastChild).Height));
    }

    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task SourceOrder_SelectsFirstCreatedPathBeforeLaneSorting(string extension)
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            SLC 0 0 12 2 96 7 2 SLD
            SLC 0 0 0 2 96 7 2 SLD
            SLD 0 96 7 2 96 0 2 SLD
            SLD 0 96 7 2 96 12 2 SLD
            """);
        void Check(U.Chart result)
        {
            var paths = result.Notes.Children.OfType<U.Slide>().OrderBy(s => s.Lane).ToArray();
            Assert.Equal([12, 0], paths.Select(s => s.Children[^1].Lane));
        }
        Check(chart);
        Check(await ThroughC2s(await ThroughEditor(chart, extension)));
    }

    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task AirPairSorting_PreservesRootCreationOrder(string extension)
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            SLC 0 0 0 2 96 7 2 SLD
            SLC 0 0 0 2 48 1 2 SLD
            SLD 0 48 1 2 48 7 2 SLD
            SLD 0 96 7 2 96 12 2 SLD
            AIR 0 192 12 2 SLD DEF
            """);
        void Check(U.Chart result)
        {
            var paths = result.Notes.Children.OfType<U.Slide>().ToArray();
            Assert.Equal(2, paths.Length);
            Assert.Contains(paths, s => s.Children.Select(n => n.Lane).SequenceEqual([7, 12]));
            Assert.Contains(paths, s => s.Children.Select(n => n.Lane).SequenceEqual([1, 7]));
            Assert.Equal(12, Assert.Single(result.Notes.Children.OfType<U.Air>()).PairNote!.Lane);
        }
        Check(chart);
        Check(await ThroughC2s(await ThroughEditor(chart, extension)));
    }

    [Fact]
    public async Task DifferentLineTypes_NeverContinueTheSamePath()
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            SLC 0 0 0 2 96 7 2 SLD
            SLD 0 96 7 2 96 12 2 NCL
            """);
        Assert.Equal(2, chart.Notes.Children.OfType<U.Slide>().Count());
        Assert.Equal(2, (await ThroughC2s(chart)).Notes.Children.OfType<U.Slide>().Count());
    }

    [Theory]
    [InlineData(".mgxc", false)]
    [InlineData(".ugc", false)]
    [InlineData(".mgxc", true)]
    [InlineData(".ugc", true)]
    public async Task AirAndHold_UseParentRootOrderAfterSnapshotRestoration(string extension, bool clearMetadata)
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            SLC 0 0 0 2 96 7 2 SLD
            SLD 0 0 6 2 96 7 2 SLD
            SLC 0 0 12 2 48 13 2 SLD
            SLD 0 48 13 2 48 7 2 SLD
            AIR 0 96 7 2 SLD DEF
            AHD 0 96 7 2 SLD 96 DEF
            AHD 0 96 7 2 SLD 192 DEF
            SLA 0 96 0 16 96 1
            """);
        chart = await ThroughEditor(chart, extension);
        if (clearMetadata)
        {
            chart.Extras = new() { FixedZeroOrigin = true };
            chart.Meta.C2sAirSnapshot = null;
            chart.Meta.C2sAirEditKey = null;
        }
        var result = await ThroughC2s(chart);
        Assert.Equal(0, Assert.Single(result.Notes.Children.OfType<U.Air>()).PairNote!.Parent!.Lane);
        Assert.Equal([6, 12], result.Notes.Children.OfType<U.AirHold>()
            .OrderBy(n => n.GetLastTick()).Select(n => n.PairNote!.Parent!.Lane));
    }

    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task AirSlideContinuation_UsesRootOrderAndParentJointType(string extension)
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            TAP 0 0 0 2
            TAP 0 0 12 2
            ASC 0 0 0 2 TAP 1.0 96 7 2 2.0 DEF
            ASC 0 0 12 2 TAP 1.0 48 13 2 2.0 DEF
            ASC 0 48 13 2 ASC 2.0 48 7 2 2.0 DEF
            ASD 0 96 7 2 ASC 2.0 96 0 2 3.0 DEF
            ASD 0 96 7 2 ASC 2.0 96 12 2 3.0 DEF
            """);
        chart.Extras = new() { FixedZeroOrigin = true };
        var result = await ThroughC2s(await ThroughEditor(chart, extension));
        var paths = result.Notes.Children.OfType<U.AirSlide>().OrderBy(s => s.Lane).ToArray();
        Assert.Equal(2, paths.Length);
        Assert.Equal([7, 0], paths[0].Children.Select(n => n.Lane));
        Assert.Equal([13, 7, 12], paths[1].Children.Select(n => n.Lane));
    }

    // Four paths meet at the same point. The invisible continuations occur
    // first in the file, although the visible paths started earlier.
    private const string Crossing = """
        VERSION 1.15.00 1.15.00
        RESOLUTION 384
        BPM_DEF 120 120 120 120
        MET_DEF 4 4
        SLC 0 0 1 2 96 1 2 SLD
        SLC 0 0 13 2 96 13 2 SLD
        SLC 0 96 1 2 96 7 2 SLD
        SLC 0 96 5 2 96 7 2 NCL
        SLC 0 96 9 2 96 7 2 NCL
        SLC 0 96 13 2 96 7 2 SLD
        SLD 0 192 7 2 96 4 8 NCL
        SLD 0 192 7 2 96 6 4 NCL
        SLD 0 192 7 2 96 0 4 SLD
        SLD 0 192 7 2 96 12 4 SLD
        """;

    [Fact]
    public async Task SharedJunction_PreservesLineGroupsAndOrderWithinEachGroup()
    {
        var chart = await ReadC2s(Crossing);
        AssertCrossing(chart);
    }

    [Fact]
    public void SharedJunction_JudgeCountsFollowTheSameLineGroups()
    {
        var chart = new C.Chart();
        chart.Notes.AddRange([
            new C.Slide { Tick = 0, Lane = 0, Width = 2, EndTick = 96, EndLane = 7, EndWidth = 2 },
            new C.Slide { Tick = 48, Lane = 14, Width = 2, EndTick = 96, EndLane = 7, EndWidth = 2, NoLine = true },
            new C.Slide { Tick = 96, Lane = 7, Width = 2, EndTick = 144, EndLane = 4, EndWidth = 2, NoLine = true },
            new C.Slide { Tick = 96, Lane = 7, Width = 2, EndTick = 192, EndLane = 12, EndWidth = 2 }
        ]);
        Assert.Equal(3, C2SJudgeSummaryCalculator.CalculateSlideProxy(chart));

        const string crossing = """
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            SLC 0 0 0 2 24 7 2 SLD
            SLC 0 12 14 2 12 7 2 NCL
            SLD 0 24 7 2 12 4 2 NCL
            SLD 0 24 7 2 24 12 2 SLD
            """;
        // Moving one path away from the shared point leaves its judges intact.
        var separated = crossing.Replace("12 7 2 NCL", "12 5 2 NCL", StringComparison.Ordinal)
            .Replace("24 7 2 12", "24 5 2 12", StringComparison.Ordinal);
        Assert.Equal(ChartStatistics.Calculate(separated), ChartStatistics.Calculate(crossing));
    }

    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task SharedJunction_SurvivesBothEditorFormatsWithoutSourceMetadata(string extension)
    {
        var chart = await ReadC2s(Crossing);
        chart.Extras = new();
        chart = await ThroughEditor(chart, extension);
        AssertCrossing(chart);
        AssertCrossing(await ThroughC2s(chart));
    }

    [Theory]
    [InlineData(".mgxc", true)]
    [InlineData(".ugc", true)]
    [InlineData(".mgxc", false)]
    [InlineData(".ugc", false)]
    public async Task EditedPaths_AreNotReconnectedBySavedSourceOrder(string extension, bool binarySnapshotValid)
    {
        var chart = await ReadC2s("""
            VERSION 1.15.00 1.15.00
            RESOLUTION 384
            BPM_DEF 120 120 120 120
            MET_DEF 4 4
            SLC 0 0 0 2 96 7 2 SLD
            SLC 0 0 14 2 96 7 2 SLD
            SLD 0 96 7 2 96 0 4 SLD
            SLD 0 96 7 2 96 12 4 SLD
            """);
        chart = await ThroughEditor(chart, extension);
        var slides = chart.Notes.Children.OfType<U.Slide>().OrderBy(s => s.Lane).ToArray();
        // Reconnect the two tails. The flat set of C2S records is unchanged;
        // only their order at the shared point represents this edit.
        slides[0].Children[^1].Lane = 12;
        slides[1].Children[^1].Lane = 0;
        chart.Extras.BinarySnapshotValid = binarySnapshotValid;
        var result = await ThroughC2s(chart);
        var actual = result.Notes.Children.OfType<U.Slide>().OrderBy(s => s.Lane).ToArray();
        Assert.Equal(2, actual.Length);
        Assert.Equal(12, actual[0].Children[^1].Lane);
        Assert.Equal(0, actual[1].Children[^1].Lane);
    }

    private static void AssertCrossing(U.Chart chart)
    {
        var slides = chart.Notes.Children.OfType<U.Slide>().OrderBy(s => s.Tick.Original).ThenBy(s => s.Lane).ToArray();
        Assert.Equal(4, slides.Length);
        Assert.Equal([1, 13, 5, 9], slides.Select(s => s.Lane));
        Assert.Equal([false, false, true, true], slides.Select(s => s.NoLine));
        Assert.Equal([0, 12, 4, 6], slides.Select(s => s.Children[^1].Lane));
        Assert.Equal([4, 4, 8, 4], slides.Select(s => s.Children[^1].Width));
        foreach (var slide in slides)
        {
            Assert.All(slide.Children.OfType<U.SlideJoint>().SkipLast(1), joint => Assert.Equal(slide.NoLine, joint.NoLine));
            Assert.Equal(Joint.D, Assert.IsType<U.SlideJoint>(slide.Children[^1]).Joint);
        }
    }

    private static async Task<U.Chart> ReadC2s(string text)
    {
        var path = TestTempPaths.Create(".c2s");
        try
        {
            await File.WriteAllTextAsync(path, text.Replace(' ', '\t'), TestContext.Current.CancellationToken);
            return await ReadC2sFile(path);
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> ReadC2sFile(string path)
    {
        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        return converted.Value!;
    }

    private static async Task<U.Chart> ThroughEditor(U.Chart chart, string extension)
    {
        var path = TestTempPaths.Create(extension);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = extension == ".mgxc"
                ? await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)
                : await new UgcChartWriter(path, chart).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = extension == ".mgxc"
                ? await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> ThroughC2s(U.Chart chart)
    {
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            return await ReadC2sFile(path);
        }
        finally { File.Delete(path); }
    }
}
