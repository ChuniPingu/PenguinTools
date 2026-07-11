using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser.sus;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class SusParserTests
{
    private static async Task<OperationResult<Chart.Models.umgr.Chart>> Parse(string sus)
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = Path.GetTempFileName() + ".sus";
        await File.WriteAllTextAsync(tmp, sus, ct);
        try
        {
            var parser = new SusParser(
                new SusParseRequest(tmp, TestAssets.Load()),
                TestMediaTool.Instance);
            return await parser.ParseAsync(ct);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static async Task<OperationResult<Chart.Models.umgr.Chart>> ParseFile(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var parser = new SusParser(
            new SusParseRequest(path, TestAssets.Load()),
            TestMediaTool.Instance);
        return await parser.ParseAsync(ct);
    }

    public static IEnumerable<object[]> SampleSusFiles()
    {
        var samplesDir = ChartTestPaths.AssetsDirectory;
        if (!Directory.Exists(samplesDir))
            return [];

        return Directory.EnumerateFiles(samplesDir, "*.sus")
            .OrderBy(Path.GetFileName)
            .Select(path => new object[] { Path.GetFileName(path)!, path });
    }

    [Fact]
    public async Task MetaAndMeasureLength_AreParsed()
    {
        const string sus =
            "#TITLE \"Hello World\"\n" +
            "#ARTIST \"Alice\"\n" +
            "#DESIGNER \"Bob\"\n" +
            "#DIFFICULTY 3\n" +
            "#PLAYLEVEL 14+\n" +
            "#SONGID \"5097\"\n" +
            "#BPM01: 200\n" +
            "#00008: 01\n" +
            "#00102: 3\n" +
            "#00210: 14\n";

        var result = await Parse(sus);
        Assert.True(result.Succeeded, result.ToString());

        var chart = result.Value!;
        Assert.Equal("Hello World", chart.Meta.Title);
        Assert.Equal("Alice", chart.Meta.Artist);
        Assert.Equal("Bob", chart.Meta.Designer);
        Assert.Equal(Difficulty.Master, chart.Meta.Difficulty);
        Assert.Equal(14.5m, chart.Meta.Level);
        Assert.Equal("5097", chart.Meta.MgxcId);
        Assert.Equal(5097, chart.Meta.Id);

        var bpm = Assert.Single(chart.Events.Children.OfType<BpmEvent>());
        Assert.Equal(0, bpm.Tick.Original);
        Assert.Equal(200m, bpm.Bpm);

        var beats = chart.Events.Children.OfType<BeatEvent>().OrderBy(e => e.Bar).ToArray();
        Assert.Equal(2, beats.Length);
        Assert.Equal((0, 4, 4), (beats[0].Bar, beats[0].Numerator, beats[0].Denominator));
        Assert.Equal((1, 3, 4), (beats[1].Bar, beats[1].Numerator, beats[1].Denominator));
        Assert.Equal(1920, beats[1].Tick.Original);

        var tap = Assert.Single(chart.Notes.Children.OfType<Tap>());
        Assert.Equal(3360, tap.Tick.Original);
        Assert.Equal(0, tap.Lane);
        Assert.Equal(4, tap.Width);
    }

    [Fact]
    public async Task Nospeed_IsIgnored()
    {
        const string sus =
            "#BPM01: 120\n" +
            "#00008: 01\n" +
            "#TIL01: \"0'0:1.0, 0'480:2.0\"\n" +
            "#HISPEED 01\n" +
            "#NOSPEED\n" +
            "#00010: 14\n";

        var result = await Parse(sus);
        Assert.True(result.Succeeded, result.ToString());

        var tap = Assert.Single(result.Value!.Notes.Children.OfType<Tap>());
        Assert.Equal(1, tap.Timeline);
    }

    [Fact]
    public async Task ShortNoteKinds_MapToUmgrTypes()
    {
        const string sus =
            "#BPM01: 120\n" +
            "#00008: 01\n" +
            "#00010: 11213141\n";

        var result = await Parse(sus);
        Assert.True(result.Succeeded, result.ToString());

        var chart = result.Value!;
        Assert.Single(chart.Notes.Children.OfType<Tap>());
        Assert.Single(chart.Notes.Children.OfType<ExTap>());
        Assert.Single(chart.Notes.Children.OfType<Flick>());
        Assert.Single(chart.Notes.Children.OfType<Damage>());
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SampleSusFiles))]
    public async Task SampleCharts_ParseSuccessfully(string _, string path)
    {
        var result = await ParseFile(path);
        Assert.True(result.Succeeded, result.ToString());

        var chart = result.Value!;
        Assert.False(string.IsNullOrWhiteSpace(chart.Meta.Title));
        Assert.NotEmpty(chart.Events.Children.OfType<BpmEvent>());
        Assert.NotEmpty(chart.Notes.Children);
        Assert.DoesNotContain(
            result.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Severity == Severity.Error);
    }

    [Fact]
    public async Task AirWithoutCompatibleParent_IsIgnored()
    {
        const string sus =
            "#BPM01: 120\n" +
            "#00008: 01\n" +
            "#00010: 11\n" +
            "#00053: 11\n";

        var result = await Parse(sus);
        Assert.True(result.Succeeded, result.ToString());

        Assert.Empty(result.Value!.Notes.Children.OfType<Air>());
        Assert.Contains(
            result.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Severity == Severity.Warning &&
                          diagnostic.Message.Key == MsgKeys.Sus_Air_note_ignored);
    }

    [Fact]
    public async Task AirHold_RemovesAttachedAir_AndUsesParentShape()
    {
        const string sus =
            "#BPM01: 120\n" +
            "#00008: 01\n" +
            "#00010: 14\n" +
            "#00050: 14\n" +
            "#00040A: 14\n" +
            "#00140A: 24\n";

        var result = await Parse(sus);
        Assert.True(result.Succeeded, result.ToString());

        var chart = result.Value!;
        var tap = Assert.Single(chart.Notes.Children.OfType<Tap>());
        var airSlide = Assert.Single(chart.Notes.Children.OfType<AirSlide>());
        Assert.Empty(chart.Notes.Children.OfType<Air>());
        Assert.Same(tap, airSlide.PairNote);
        Assert.Equal(0, airSlide.Tick.Original);
        Assert.Equal(0m, airSlide.Height);

        var child = Assert.Single(airSlide.Children.OfType<AirSlideJoint>());
        Assert.Equal(1920, child.Tick.Original);
        Assert.Equal(airSlide.Lane, child.Lane);
        Assert.Equal(airSlide.Width, child.Width);
        Assert.Equal(0m, child.Height);
    }
}