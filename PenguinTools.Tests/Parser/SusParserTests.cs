using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.sus;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
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
        var tmp = TestTempPaths.Create(".sus");
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

    public static IEnumerable<TheoryDataRow<string, string>> SampleSusFiles()
    {
        var samplesDir = ChartTestPaths.AssetsDirectory;
        if (!Directory.Exists(samplesDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(samplesDir, "*.sus")
            .OrderBy(Path.GetFileName)
            .Select(path => new TheoryDataRow<string, string>(Path.GetFileName(path)!, path));
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
    public async Task Nospeed_ResetsFollowingNotesWithoutChangingEarlierNotes()
    {
        const string sus =
            "#BPM01: 120\n" +
            "#00008: 01\n" +
            "#TIL01: \"0'0:1.0, 0'480:2.0\"\n" +
            "#HISPEED 01\n" +
            "#00010: 14\n" +
            "#NOSPEED\n" +
            "#00110: 14\n" +
            "#HISPEED 01\n#00210: 14\n";

        var result = await Parse(sus);
        Assert.True(result.Succeeded, result.ToString());

        var taps = result.Value!.Notes.Children.OfType<Tap>().OrderBy(note => note.Tick).ToArray();
        Assert.Equal([1, 0, 1], taps.Select(note => note.Timeline));
        Assert.DoesNotContain(result.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Key == MsgKeys.Mg_Unrecognized_meta);
    }

    [Fact]
    public async Task OverlappingAirNotes_UseSeparateAvailableParents()
    {
        var result = await Parse("#BPM01: 120\n#00008: 01\n" +
                                 "#00010: 14\n#00010: 14\n#00050: 14\n#00050: 14\n");
        Assert.True(result.Succeeded, result.ToString());
        var airs = result.Value!.Notes.Children.OfType<Air>().ToArray();
        Assert.Equal(2, airs.Length);
        Assert.NotSame(airs[0].PairNote, airs[1].PairNote);
        Assert.All(airs, air => Assert.Same(air, air.PairNote!.PairNote));
        Assert.True(new C2SChartConverter(new C2SConvertRequest(result.Value)).Convert().Succeeded);
    }

    [Fact]
    public async Task RoundedAirWidth_DoesNotLoseFollowingAirHold()
    {
        var result = await Parse("#BPM01: 120\n#00008: 01\n" +
                                 "#00010: 15\n#00050: 15\n#00040A: 15\n#00140A: 25\n");
        Assert.True(result.Succeeded, result.ToString());
        var chart = result.Value!;
        var tap = Assert.Single(chart.Notes.Children.OfType<Tap>());
        var hold = Assert.Single(chart.Notes.Children.OfType<AirHold>());
        Assert.Empty(chart.Notes.Children.OfType<Air>());
        Assert.Same(tap, hold.PairNote);
        Assert.Equal(4, hold.Width);
        Assert.Equal(1920, Assert.Single(hold.Children).Tick.Original);
        Assert.DoesNotContain(result.Diagnostics.Diagnostics, diagnostic => diagnostic.Severity >= Severity.Warning);
    }

    [Theory]
    [InlineData("#00010: 111", 4)]
    [InlineData("#00008: 010", 4)]
    [InlineData("#BPM02: 0", 4)]
    [InlineData("#BPM02: -1", 4)]
    public async Task InvalidTimingData_IsRejectedWithSourceLocation(string body, int line)
    {
        var result = await Parse("#TITLE \"Invalid\"\n#BPM01: 120\n#00008: 01\n" + body + "\n");
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var error = Assert.Single(result.Diagnostics.Diagnostics, diagnostic => diagnostic.Severity == Severity.Error);
        Assert.Equal(MsgKeys.Sus_Malformed_line, error.Message.Key);
        Assert.Equal(line, Assert.IsType<LocationDiagnostic>(error).Line);
    }

    [Fact]
    public async Task ZeroKind_IsEmptyAndKeepsTheSubdivisionPosition()
    {
        var result = await Parse("#BPM01: 120\n#00008: 01\n#00010: 0111\n");
        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(960, Assert.Single(result.Value!.Notes.Children.OfType<Tap>()).Tick.Original);
        Assert.DoesNotContain(result.Diagnostics.Diagnostics, diagnostic => diagnostic.Severity >= Severity.Warning);
    }

    [Theory]
    [InlineData(480)]
    [InlineData(960)]
    [InlineData(1920)]
    public async Task TimelineTickResolution_IsScaledIndependentlyOfNoteSubdivision(int ticksPerBeat)
    {
        var result = await Parse($"#REQUEST \"ticks_per_beat {ticksPerBeat}\"\n" +
                                 "#BPM01: 120\n#00008: 01\n" +
                                 $"#TIL01: \"0'0:1, 0'{ticksPerBeat}:2\"\n#HISPEED 01\n" +
                                 "#00010: 11111111111111\n");
        Assert.True(result.Succeeded, result.ToString());
        var changes = result.Value!.Events.Children.OfType<ScrollSpeedEvent>().Where(note => note.Timeline == 1).ToArray();
        Assert.Equal([0, 480], changes.Select(note => note.Tick.Original));
        Assert.Equal([0, 274, 549, 823, 1097, 1371, 1646],
            result.Value.Notes.Children.OfType<Tap>().Select(note => note.Tick.Original));
    }

    [Theory]
    [InlineData(ChartDialect.Original, false)]
    [InlineData(ChartDialect.Original, true)]
    [InlineData(ChartDialect.Extended, false)]
    [InlineData(ChartDialect.Extended, true)]
    public async Task SupportedNotesAndTiming_SurviveNativeEncodingAndThreeC2sCycles(ChartDialect dialect, bool ugc)
    {
        const string source = "#TITLE \"SUS roundtrip\"\n#ARTIST \"Artist\"\n#DESIGNER \"Designer\"\n" +
                              "#DIFFICULTY 3\n#PLAYLEVEL 14+\n#SONGID \"5097\"\n#WAVEOFFSET 0.125\n" +
                              "#BPM01: 150\n#BPM02: 225\n#00008: 01\n#00108: 0002\n" +
                              "#00102: 3\n#00202: 2.5\n#00010: 11213141\n" +
                              "#00120A: 14002400\n#00134B: 14440024\n#00154: 00000014\n" +
                              "#00218: 14\n#00258: 14\n#00248C: 14000024\n";
        var parsed = await Parse(source);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var chart = parsed.Value!;
        var baseline = await WriteC2s(chart);
        Assert.Contains("HLD\t1\t0\t0\t4\t144", baseline);
        Assert.Contains("SLC\t1\t0\t4\t4\t72\t4\t4\tSLD", baseline);
        Assert.Contains("SLD\t1\t72\t4\t4\t144\t4\t4\tSLD", baseline);
        Assert.Contains("BPM\t1\t144\t225.000", baseline);
        Assert.Contains("MET\t1\t0\t4\t3", baseline);
        Assert.Contains("MET\t1\t288\t8\t5", baseline);

        for (var cycle = 0; cycle < 4; cycle++)
        {
            var native = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
            var ct = TestContext.Current.CancellationToken;
            try
            {
                var written = ugc
                    ? await new UgcChartWriter(native, chart, dialect).WriteAsync(ct)
                    : await new MgxcChartWriter(new MgxcWriteRequest(native, chart, dialect)).WriteAsync(ct);
                Assert.True(written.Succeeded, written.ToString());
                var restored = ugc
                    ? await new UgcParser(new UgcParseRequest(native, TestAssets.Load(), dialect), TestMediaTool.Instance).ParseAsync(ct)
                    : await new MgxcParser(new MgxcParseRequest(native, TestAssets.Load(), dialect), TestMediaTool.Instance).ParseAsync(ct);
                Assert.True(restored.Succeeded, restored.ToString());
                if (cycle == 0)
                {
                    // Native formats retain these SUS fields; C2S has no title,
                    // artist or audio-offset field to carry them through a cycle.
                    Assert.Equal("SUS roundtrip", restored.Value!.Meta.Title);
                    Assert.Equal("Artist", restored.Value.Meta.Artist);
                    Assert.Equal("Designer", restored.Value.Meta.Designer);
                    Assert.Equal(0.125m, restored.Value.Meta.BgmManualOffset);
                }
                var actual = await WriteC2s(restored.Value!);
                Assert.Equal(baseline, actual);
                chart = await ReadC2s(actual);
            }
            finally
            {
                File.Delete(native);
            }
        }
    }

    private static async Task<string[]> WriteC2s(Chart.Models.umgr.Chart chart)
    {
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            return (await File.ReadAllLinesAsync(path, ct)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<Chart.Models.umgr.Chart> ReadC2s(string[] lines)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllLinesAsync(path, lines, ct);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            return converted.Value!;
        }
        finally
        {
            File.Delete(path);
        }
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
        var airHold = Assert.Single(chart.Notes.Children.OfType<AirHold>());
        Assert.Empty(chart.Notes.Children.OfType<Air>());
        Assert.Same(tap, airHold.PairNote);
        Assert.Equal(0, airHold.Tick.Original);

        var child = Assert.Single(airHold.Children.OfType<AirHoldJoint>());
        Assert.Equal(1920, child.Tick.Original);
        Assert.Equal(airHold.Lane, child.Lane);
        Assert.Equal(airHold.Width, child.Width);
    }

    [Fact]
    public async Task Meta_Ignore_SkipsParse_AndSuppressesOtherDiagnostics()
    {
        const string sus =
            "#TITLE \"Ignored\"\n" +
            "#meta ignore\n" +
            "#BPM01: 120\n" +
            "#00008: 01\n" +
            "#00010: 11\n" +
            "#00053: 11\n";

        var result = await Parse(sus);
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics.Diagnostics);
        Assert.Equal(Severity.Information, diagnostic.Severity);
        Assert.Equal(MsgKeys.Mg_Meta_Ignored, diagnostic.Message.Key);
        Assert.DoesNotContain(
            result.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Key == MsgKeys.Sus_Air_note_ignored);
    }

    [Fact]
    public async Task Meta_Ignore_InCommentHeader_SkipsParse()
    {
        const string sus =
            "#COMMENT \"#meta ignore\"\n" +
            "#BPM01: 120\n" +
            "#00008: 01\n" +
            "#00010: 14\n";

        var result = await Parse(sus);
        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics.Diagnostics);
        Assert.Equal(MsgKeys.Mg_Meta_Ignored, diagnostic.Message.Key);
    }
}
