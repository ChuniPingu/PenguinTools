using System.Globalization;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class UgcTilTests
{
    private const string Header =
        "@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n";

    private static async Task<OperationResult<Chart.Models.umgr.Chart>> ParseResult(
        string body, ChartDialect mode = ChartDialect.Auto, string header = Header)
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        await File.WriteAllTextAsync(tmp, header + body, ct);
        try
        {
            return await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load(), mode), TestMediaTool.Instance)
                .ParseAsync(ct);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static async Task<Chart.Models.umgr.Chart> Parse(string body)
    {
        var r = await ParseResult(body);
        Assert.True(r.Succeeded, r.ToString());
        return r.Value!;
    }

    private static SoflanArea[] GetSoflanAreas(Chart.Models.umgr.Chart chart) =>
        chart.Notes.Children.OfType<SoflanArea>().ToArray();

    private static async Task<string[]> WriteC2s(string body, ChartDialect mode, string header = Header)
    {
        var parsed = await ParseResult(body, mode, header);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            return await File.ReadAllLinesAsync(path, ct);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static int[] TimelinesInFile(string[] lines, params (int Tick, int Lane, int Width)[] notes)
    {
        // Read serialized row order independently of the converter's timeline
        // model: a later matching SLA replaces the earlier assignment.
        var areas = lines.Where(line => line.StartsWith("SLA\t", StringComparison.Ordinal))
            .Select(line => line.Split('\t').Skip(1)
                .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        return notes.Select(note => areas
            .Where(area => note.Tick >= area[0] * 384 + area[1] &&
                           note.Tick < area[0] * 384 + area[1] + area[4] &&
                           note.Lane >= area[2] && note.Lane + note.Width <= area[2] + area[3])
            .Select(area => area[5]).LastOrDefault()).ToArray();
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task Til_SerializedRectanglesDoNotOverrideInterleavedTimelines(ChartDialect mode)
    {
        const string body =
            "@FLAG\tSOFFSET\tFALSE\n@TIL\t0\t0'0\t1\n@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t44\n#0'100:t44\n" +
            "@USETIL\t1\n#0'50:t04\n#0'150:t44\n@USETIL\t0\n#0'200:tC4\n";

        var lines = await WriteC2s(body, mode);

        Assert.Equal([2, 1, 2, 1, 0],
            TimelinesInFile(lines, (0, 4, 4), (10, 0, 4), (20, 4, 4), (30, 4, 4), (40, 12, 4)));
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task Til_SerializedWideAndNarrowOverlapRemainsConvertible(ChartDialect mode)
    {
        const string body =
            "@FLAG\tSOFFSET\tFALSE\n@TIL\t2\t0'0\t2\n@TIL\t3\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t04\n#1'0:t04\n@USETIL\t3\n#0'0:t12\n";

        var lines = await WriteC2s(body, mode);

        Assert.Equal([2, 3, 2], TimelinesInFile(lines, (0, 0, 4), (0, 1, 2), (384, 0, 4)));
        Assert.Equal(2, lines.Count(line => line.StartsWith("SLA\t", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task Til_SerializedRectanglesPreserveAirSlideChildTimeline(ChartDialect mode)
    {
        const string body =
            "@FLAG\tSOFFSET\tFALSE\n@TIL\t0\t0'0\t1\n@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t44\n#0'0:S440AN\n#100:s440A\n" +
            "@USETIL\t1\n#0'50:t04\n#0'150:t44\n";

        var lines = await WriteC2s(body, mode);

        var airSlide = Assert.Single(lines, line => line.StartsWith("ASD\t", StringComparison.Ordinal)).Split('\t');
        Assert.Equal(["0", "0", "4", "4"], airSlide[1..5]);
        Assert.Equal(["20", "4", "4"], airSlide[7..10]);
        Assert.Equal([2, 1, 2, 1], TimelinesInFile(lines, (0, 4, 4), (10, 0, 4), (20, 4, 4), (30, 4, 4)));
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task Til_SerializedRectangleExcludesNoteAtEndTick(ChartDialect mode)
    {
        const string body =
            "@FLAG\tSOFFSET\tFALSE\n@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t44\n#0'100:t44\n@USETIL\t0\n#0'105:t44\n";

        var lines = await WriteC2s(body, mode);

        Assert.Single(lines, line => line.StartsWith("SLA\t", StringComparison.Ordinal));
        Assert.Equal([2, 2, 0], TimelinesInFile(lines, (0, 4, 4), (20, 4, 4), (21, 4, 4)));
    }

    [Theory]
    [InlineData(ChartDialect.Original, 4)]
    [InlineData(ChartDialect.Original, 19)]
    [InlineData(ChartDialect.Original, 14)]
    [InlineData(ChartDialect.Original, 13)]
    [InlineData(ChartDialect.Original, 7)]
    [InlineData(ChartDialect.Extended, 7)]
    public async Task Til_SerializedRectangleDoesNotCaptureOffGridNoteAfterOpeningMeasure(
        ChartDialect mode, int denominator)
    {
        const string body =
            "@FLAG\tSOFFSET\tTRUE\n@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t04\n#0'100:t44\n@USETIL\t0\n#0'103:t04\n";
        var header = $"@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120\n@BEAT\t0\t1\t{denominator}\n";

        var lines = await WriteC2s(body, mode, header);

        var offset = mode == ChartDialect.Original ? (int)Math.Round(1920m / denominator) : 0;
        var start = (int)Math.Round(offset / 5m);
        var last = (int)Math.Round((offset + 100) / 5m);
        var outside = (int)Math.Round((offset + 103) / 5m);
        Assert.Equal([2, 2, 0], TimelinesInFile(lines, (start, 0, 4), (last, 4, 4), (outside, 0, 4)));
    }

    [Fact]
    public async Task Til_Definition_CreatesScrollSpeedEvent()
    {
        const string ugc =
            "@VER\t8\n@TICKS\t480\n" +
            "@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n" +
            "@TIL\t3\t0'240\t10000.0\n";
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        await File.WriteAllTextAsync(tmp, ugc, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded);
            var sse = r.Value!.Events.Children
                .OfType<ScrollSpeedEvent>()
                .SingleOrDefault(e => e.Speed == 10000.0m);
            Assert.NotNull(sse);
            Assert.Equal(240, sse!.Tick.Original);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task UseTil_AppliesToChildNoteLines()
    {
        const string ugc =
            "@USETIL\t0\n#0'0:h14\n@USETIL\t3\n#480:s\n" +
            "@USETIL\t0\n#1'0:s14\n@USETIL\t4\n#480:s24\n" +
            "@USETIL\t0\n#2'0:t14\n#2'0:H14N\n@USETIL\t5\n#480:c\n" +
            "@USETIL\t0\n#3'0:t24\n#3'0:S240AN\n@USETIL\t6\n#480:s34ZZ\n" +
            "@USETIL\t0\n#4'0:C340A1,24\n@USETIL\t7\n#480:c44ZZ\n";

        var chart = await Parse(ugc);

        Assert.Equal(3, Assert.Single(chart.Notes.Children.OfType<Hold>()).Children.Single().Timeline);
        Assert.Equal(4, Assert.Single(chart.Notes.Children.OfType<Slide>()).Children.Single().Timeline);

        var airHold = Assert.Single(chart.Notes.Children.OfType<AirHold>());
        Assert.Equal(5, airHold.Children.Single().Timeline);
        var airSlide = Assert.Single(chart.Notes.Children.OfType<AirSlide>());
        Assert.Equal(6, airSlide.Children.Single().Timeline);

        Assert.Equal(7, Assert.Single(chart.Notes.Children.OfType<AirCrash>()).Children.Single().Timeline);
    }

    [Fact]
    public async Task MainTil_SetsMetaMainTil()
    {
        const string ugc =
            "@VER\t8\n@TICKS\t480\n" +
            "@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n" +
            "@MAINTIL\t2\n";
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        await File.WriteAllTextAsync(tmp, ugc, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded);
            var errors = r.Diagnostics.Diagnostics.Where(d => d.Severity >= Severity.Warning).ToList();
            Assert.DoesNotContain(errors, d =>
                d.Message.Key == MsgKeys.Mg_Main_timeline_not_found);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Til_Overlap_WarnsWhenLaneSpanFullyContainsOtherOnDifferentTil()
    {
        // Wider note on TIL 2 fully contains narrower note on TIL 3 at the same tick.
        const string body =
            "@TIL\t2\t0'0\t2\n@TIL\t3\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t04\n@USETIL\t3\n#0'0:t12\n";
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        await File.WriteAllTextAsync(tmp, Header + body, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded);
            var overlap = Assert.Single(r.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
            var pair = Assert.IsType<NotePairDiagnosticTarget>(overlap.Target);
            Assert.Equal(0, pair.Left.Tick);
            Assert.Equal(0, pair.Right.Tick);
            Assert.NotNull(pair.TimePosition);
            Assert.Contains(new[] { pair.Left, pair.Right },
                n => n is { Type: "Tap", Lane: 0, Width: 4, Timeline: 2 });
            Assert.Contains(new[] { pair.Left, pair.Right },
                n => n is { Type: "Tap", Lane: 1, Width: 2, Timeline: 3 });
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Til_OverlapAtLongNoteTail_Warns()
    {
        // The hold tail is on TIL 2, so it receives an SLA that covers the main-TIL tap.
        const string body =
            "@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t0\n#0'0:h04\n@USETIL\t2\n#480:s\n" +
            "@USETIL\t0\n#0'480:t12\n";

        var r = await ParseResult(body);

        Assert.True(r.Succeeded, r.ToString());
        var overlap = Assert.Single(r.Diagnostics.Diagnostics,
            d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        var pair = Assert.IsType<NotePairDiagnosticTarget>(overlap.Target);
        Assert.Contains(new[] { pair.Left, pair.Right }, n => n is { Type: "HoldJoint", Timeline: 2 });
        Assert.Contains(new[] { pair.Left, pair.Right }, n => n is { Type: "Tap", Timeline: 0 });
    }

    [Fact]
    public async Task Til_OverlapCoveringLongNoteTail_Warns()
    {
        // The TIL-2 tap's SLA covers a main-TIL hold tail.
        const string body =
            "@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t0\n#0'0:h12\n#480:s\n" +
            "@USETIL\t2\n#0'480:t04\n";

        var r = await ParseResult(body);

        Assert.True(r.Succeeded, r.ToString());
        var overlap = Assert.Single(r.Diagnostics.Diagnostics,
            d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        var pair = Assert.IsType<NotePairDiagnosticTarget>(overlap.Target);
        Assert.Contains(new[] { pair.Left, pair.Right }, n => n is { Type: "Tap", Timeline: 2 });
        Assert.Contains(new[] { pair.Left, pair.Right }, n => n is { Type: "HoldJoint", Timeline: 0 });
    }

    [Fact]
    public async Task Til_PartialLaneOverlap_DoesNotWarn()
    {
        // Partial overlap only — neither note fully contains the other.
        const string body =
            "@TIL\t2\t0'0\t2\n@TIL\t3\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t04\n@USETIL\t3\n#0'0:t24\n";
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        await File.WriteAllTextAsync(tmp, Header + body, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded);
            Assert.DoesNotContain(r.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Til_MainTilContainingOther_DoesNotWarn()
    {
        // Main TIL never places SLA, so a wider main-TIL note cannot affect another TIL.
        const string body =
            "@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t0\n#0'0:t04\n@USETIL\t2\n#0'0:t12\n";
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        await File.WriteAllTextAsync(tmp, Header + body, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded);
            Assert.DoesNotContain(r.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Til_OtherContainingMainTil_Warns()
    {
        // Non-main note's SLA fully contains a main-TIL note — that can affect conversion.
        const string body =
            "@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t0\n#0'0:t12\n@USETIL\t2\n#0'0:t04\n";
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        await File.WriteAllTextAsync(tmp, Header + body, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded);
            var overlap = Assert.Single(r.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
            Assert.IsType<NotePairDiagnosticTarget>(overlap.Target);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Til_OverlapBeforeFirstTimelineEvent_UsesImplicitSpeedAndWarns()
    {
        // TIL 2 implicitly runs at speed 1 before its first explicit speed point.
        const string body =
            "@TIL\t0\t0'0\t1\n@TIL\t2\t0'960\t2\n" +
            "@USETIL\t2\n#0'0:t04\n@USETIL\t0\n#0'0:t12\n";

        var r = await ParseResult(body);

        Assert.True(r.Succeeded, r.ToString());
        Assert.Single(r.Diagnostics.Diagnostics,
            d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        Assert.Contains(GetSoflanAreas(r.Value!), area =>
            area is { Tick.Original: 0, Timeline: 2, Lane: 0, Width: 4 });
    }

    [Fact]
    public async Task Til_WithoutExplicitSpeedEvent_UsesImplicitSpeed()
    {
        const string body = "@USETIL\t2\n#0'0:t12\n";

        var chart = await Parse(body);

        Assert.Contains(GetSoflanAreas(chart), area =>
            area is { Tick.Original: 0, Timeline: 2, Lane: 1, Width: 2 });
    }

    [Fact]
    public async Task Til_TransparentAirCrashJointWithoutSla_DoesNotWarn()
    {
        // Transparent AirCrash control joints are intentionally not assigned SLA.
        const string body =
            "@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t0\n#0'0:C040AZ,$\n@USETIL\t2\n#480:c04ZZ\n" +
            "@USETIL\t0\n#0'480:t12\n";

        var r = await ParseResult(body);

        Assert.True(r.Succeeded, r.ToString());
        Assert.DoesNotContain(r.Diagnostics.Diagnostics,
            d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
    }

    [Fact]
    public async Task Til_SameLaneNotesGrowIntoOneSla()
    {
        const string body =
            "@TIL\t2\t0'0\t2\n" +
            "@USETIL\t2\n#0'0:t12\n#1'0:t12\n";

        var chart = await Parse(body);

        var area = Assert.Single(GetSoflanAreas(chart));
        Assert.Equal(2, area.Timeline);
        Assert.Equal(1, area.Lane);
        Assert.Equal(2, area.Width);
        Assert.Equal(0, area.Tick.Original);
        Assert.Equal(ChartResolution.UmiguriTick + ChartResolution.SingleTick,
            Assert.IsType<SoflanAreaJoint>(area.LastChild).Tick.Original);
    }

    [Fact]
    public async Task Til_SameTickNotesMergeAcrossEmptyLanes()
    {
        const string body =
            "@TIL\t2\t0'0\t2\n" +
            "@USETIL\t2\n#0'0:t01\n#0'0:t31\n";

        var chart = await Parse(body);

        var area = Assert.Single(GetSoflanAreas(chart));
        Assert.Equal(0, area.Lane);
        Assert.Equal(4, area.Width);
    }

    [Fact]
    public async Task Til_ForeignNoteInsideBoundingRectanglePreventsMerge()
    {
        const string body =
            "@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t2\n#0'0:t01\n#1'0:t31\n" +
            "@USETIL\t0\n#0'480:t11\n";

        var chart = await Parse(body);

        var areas = GetSoflanAreas(chart);
        Assert.Equal(2, areas.Length);
        Assert.All(areas, area => Assert.Equal(1, area.Width));
    }

    [Fact]
    public async Task Til_OptimizationDoesNotChangeDifferentTilOverlapWarning()
    {
        const string body =
            "@TIL\t2\t0'0\t2\n@TIL\t3\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t04\n#1'0:t04\n" +
            "@USETIL\t3\n#0'0:t12\n";

        var result = await ParseResult(body);

        Assert.True(result.Succeeded, result.ToString());
        Assert.Single(result.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        Assert.True(GetSoflanAreas(result.Value!).Length < 3);
    }
}
