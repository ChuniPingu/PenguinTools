using System.Globalization;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
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

    private static async Task<string[]> WriteC2s(string body, ChartDialect mode, string header = Header,
        bool throughMgxc = false)
    {
        var parsed = await ParseResult(body, mode, header);
        Assert.True(parsed.Succeeded, parsed.ToString());
        if (throughMgxc)
        {
            var mgxc = TestTempPaths.Create(".mgxc");
            try
            {
                var written = await new MgxcChartWriter(new MgxcWriteRequest(mgxc, parsed.Value!))
                    .WriteAsync(TestContext.Current.CancellationToken);
                Assert.True(written.Succeeded, written.ToString());
                parsed = await new MgxcParser(new MgxcParseRequest(mgxc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(parsed.Succeeded, parsed.ToString());
            }
            finally
            {
                File.Delete(mgxc);
            }
        }
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

    private static int[] TimelinesInFile(string[] lines, params (int Tick, decimal Lane, decimal Width)[] notes)
    {
        // Select the greatest matching id directly from the serialized rectangles.
        var areas = lines.Where(line => line.StartsWith("SLA\t", StringComparison.Ordinal))
            .Select(line => line.Split('\t').Skip(1)
                .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        return notes.Select(note => areas
            .Where(area => note.Tick >= area[0] * 384 + area[1] &&
                           note.Tick < area[0] * 384 + area[1] + area[4] &&
                           note.Lane >= area[2] && note.Lane + note.Width <= area[2] + area[3])
            .Select(area => area[5]).Append(0).Max()).ToArray();
    }

    [Fact]
    public async Task Til_SerializedRectanglesDoNotOverrideInterleavedTimelines()
    {
        const string body =
            "@FLAG\tSOFFSET\tFALSE\n@TIL\t0\t0'0\t1\n@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t44\n#0'100:t44\n" +
            "@USETIL\t1\n#0'50:t04\n#0'150:t44\n@USETIL\t0\n#0'200:tC4\n";

        var lines = await WriteC2s(body, ChartDialect.Original);

        Assert.Equal([2, 1, 2, 1, 0],
            TimelinesInFile(lines, (0, 4, 4), (10, 0, 4), (20, 4, 4), (30, 4, 4), (40, 12, 4)));
        Assert.Equal(2, lines.Count(line => line.StartsWith("SLA\t", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Til_SerializedWideAndNarrowOverlapRemainsConvertible()
    {
        const string body =
            "@FLAG\tSOFFSET\tFALSE\n@TIL\t2\t0'0\t2\n@TIL\t3\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t04\n#1'0:t04\n@USETIL\t3\n#0'0:t12\n";

        var lines = await WriteC2s(body, ChartDialect.Original);

        Assert.Equal([2, 3, 2], TimelinesInFile(lines, (0, 0, 4), (0, 1, 2), (384, 0, 4)));
        Assert.Equal(2, lines.Count(line => line.StartsWith("SLA\t", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Til_SerializedRectanglesPreserveAirSlideChildTimeline()
    {
        const string body =
            "@FLAG\tSOFFSET\tFALSE\n@TIL\t0\t0'0\t1\n@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t44\n#0'0:S440AN\n#100:s440A\n" +
            "@USETIL\t1\n#0'50:t04\n#0'150:t44\n";

        var lines = await WriteC2s(body, ChartDialect.Original);

        var airSlide = Assert.Single(lines, line => line.StartsWith("ASD\t", StringComparison.Ordinal)).Split('\t');
        Assert.Equal(["0", "0", "4", "4"], airSlide[1..5]);
        Assert.Equal(["20", "4", "4"], airSlide[7..10]);
        Assert.Equal([2, 1, 2, 1], TimelinesInFile(lines, (0, 4, 4), (10, 0, 4), (20, 4, 4), (30, 4, 4)));
    }

    [Fact]
    public async Task Til_SerializedRectangleExcludesNoteAtEndTick()
    {
        const string body =
            "@FLAG\tSOFFSET\tFALSE\n@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t44\n#0'100:t44\n@USETIL\t0\n#0'105:t44\n";

        var lines = await WriteC2s(body, ChartDialect.Original);

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


    [Theory]
    [InlineData(4, 0, false)]
    [InlineData(4, 0, true)]
    [InlineData(7, 2, false)]
    [InlineData(7, 2, true)]
    [InlineData(13, 2, false)]
    [InlineData(14, 2, false)]
    [InlineData(19, 2, false)]
    public async Task Til_AirCrashRungsKeepTimelineAcrossMainTimelineTap(int denominator, int startTick, bool throughMgxc)
    {
        var header = $"@VER\t9\n@TICKS\t480\n@BPM\t0'0\t120\n@BEAT\t0\t1\t{denominator}\n";
        var body = "@FLAG\tSOFFSET\tTRUE\n@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t3\n" +
            $"@USETIL\t2\n#0'{startTick}:CG40A1,24\n#{480 - startTick}:cG40A\n" +
            "@USETIL\t0\n#0'240:tH1\n";

        var lines = await WriteC2s(body, ChartDialect.Original, header, throughMgxc);

        var offset = (int)Math.Round(1920m / denominator);
        var start = (int)Math.Round((offset + startTick) / 5m);
        var end = (int)Math.Round((offset + 480) / 5m);
        var rungs = Enumerable.Range(0, (end - start) / 5 + 1)
            .Select(i => (start + i * 5, 0m, 4m)).ToArray();
        Assert.All(TimelinesInFile(lines, rungs), timeline => Assert.Equal(2, timeline));
        Assert.Equal([0], TimelinesInFile(lines, ((int)Math.Round((offset + 240) / 5m), 1, 1)));
        Assert.Single(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Til_MovingAirCrashRungsUseInterpolatedLaneAndWidth()
    {
        const string body = "@FLAG\tSOFFSET\tFALSE\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:CG20A1,30\n#240:cK30A\n#480:cO40A\n" +
            "@USETIL\t0\n#0'240:tI1\n";

        var lines = await WriteC2s(body, ChartDialect.Original, Header.Replace("@VER\t8", "@VER\t9"));

        var rungs = Enumerable.Range(0, 17)
            .Select(i => (i * 6, i / 2m, 2m + i / 8m)).ToArray();
        Assert.All(TimelinesInFile(lines, rungs), timeline => Assert.Equal(2, timeline));
        Assert.Equal([0], TimelinesInFile(lines, (48, 2, 1)));
    }

    [Fact]
    public async Task Til_MainTimelineSpeedCoversLongNoteChildWithoutMainTimelineRoot()
    {
        const string body = "@FLAG\tSOFFSET\tFALSE\n@TIL\t0\t0'0\t2\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:CG40A1,30\n@USETIL\t0\n#240:cG40A\n#480:cG40A\n";

        var lines = await WriteC2s(body, ChartDialect.Original, Header.Replace("@VER\t8", "@VER\t9"));

        var mainSpeed = Assert.Single(lines.Where(line => line.StartsWith("SLP\t", StringComparison.Ordinal))
            .Select(line => line.Split('\t')), fields => fields[5] == "0");
        Assert.Equal("2.000000", mainSpeed[4]);
        Assert.True(int.Parse(mainSpeed[3], CultureInfo.InvariantCulture) >= 96);
    }

    [Fact]
    public async Task Til_MainTimelineAirCrashRungsBlockUnrelatedRegions()
    {
        const string body = "@FLAG\tSOFFSET\tFALSE\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t0\n#0'0:CH20A1,25\n#480:cH20A\n" +
            "@USETIL\t2\n#0'103:tG4\n#0'403:tG4\n";

        var lines = await WriteC2s(body, ChartDialect.Original, Header.Replace("@VER\t8", "@VER\t9"));

        var rungs = Enumerable.Range(0, 20).Select(i => (i * 5, 1m, 2m)).ToArray();
        Assert.All(TimelinesInFile(lines, rungs), timeline => Assert.Equal(0, timeline));
        Assert.Equal([2, 2], TimelinesInFile(lines, (21, 0, 4), (81, 0, 4)));
    }

    [Fact]
    public async Task Til_AirCrashRungsFollowSegmentTimelineChanges()
    {
        const string body = "@FLAG\tSOFFSET\tFALSE\n@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:CG40A1,30\n@USETIL\t1\n#240:cG40A\n#480:cG40A\n";

        var lines = await WriteC2s(body, ChartDialect.Original, Header.Replace("@VER\t8", "@VER\t9"));

        var rungs = Enumerable.Range(0, 17).Select(i => (i * 6, 0m, 4m)).ToArray();
        Assert.Equal(Enumerable.Range(0, 17).Select(i => i < 8 ? 2 : 1), TimelinesInFile(lines, rungs));
    }

    [Fact]
    public async Task Til_UnrepresentableFullWidthMovingRungReportsApproximation()
    {
        const string body = "@FLAG\tSOFFSET\tFALSE\n@TIL\t2\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:CGG0A1,30\n#480:cHG0A\n";
        var parsed = await ParseResult(body, ChartDialect.Original, Header.Replace("@VER\t8", "@VER\t9"));

        Assert.True(parsed.Succeeded, parsed.ToString());
        Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_ald_timeline_changed);
        var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.Contains(converted.Diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Key == MsgKeys.Warn_Editor_ald_timeline_changed &&
            diagnostic.Target is NoteDiagnosticTarget { Type: "AirCrashJoint", Timeline: 2 });
        Assert.All(GetSoflanAreas(parsed.Value!), area => Assert.InRange(area.Width, 1, 16));
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

    [Theory]
    [InlineData("@TIL\t2\t0'0\t2\n@TIL\t3\t0'0\t3\n@USETIL\t2\n#0'0:t04\n@USETIL\t3\n#0'0:t12\n")]
    [InlineData("@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t2\n@USETIL\t0\n#0'0:h04\n@USETIL\t2\n#480:s\n@USETIL\t0\n#0'480:t12\n")]
    [InlineData("@TIL\t0\t0'0\t1\n@TIL\t2\t0'0\t2\n@USETIL\t0\n#0'0:h12\n#480:s\n@USETIL\t2\n#0'480:t04\n")]
    public async Task Til_NestedNotesAndLongTailsConvertWithoutOverlapWarnings(string body)
    {
        var parsed = await ParseResult(body);
        Assert.True(parsed.Succeeded, parsed.ToString());
        Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.DoesNotContain(converted.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
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
    public async Task Til_OtherContainingMainTil_ConvertsWithoutWarning()
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
            var converted = new C2SChartConverter(new C2SConvertRequest(r.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.DoesNotContain(converted.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Til_OverlapBeforeFirstTimelineEvent_UsesImplicitSpeedWithoutWarning()
    {
        // TIL 2 implicitly runs at speed 1 before its first explicit speed point.
        const string body =
            "@TIL\t0\t0'0\t1\n@TIL\t2\t0'960\t2\n" +
            "@USETIL\t2\n#0'0:t04\n@USETIL\t0\n#0'0:t12\n";

        var r = await ParseResult(body);

        Assert.True(r.Succeeded, r.ToString());
        var converted = new C2SChartConverter(new C2SConvertRequest(r.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.DoesNotContain(converted.Diagnostics.Diagnostics,
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
    public async Task Til_OptimizationPreservesConvertibleOverlapWithoutWarning()
    {
        const string body =
            "@TIL\t2\t0'0\t2\n@TIL\t3\t0'0\t3\n" +
            "@USETIL\t2\n#0'0:t04\n#1'0:t04\n" +
            "@USETIL\t3\n#0'0:t12\n";

        var result = await ParseResult(body);

        Assert.True(result.Succeeded, result.ToString());
        var converted = new C2SChartConverter(new C2SConvertRequest(result.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.DoesNotContain(converted.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
        Assert.True(GetSoflanAreas(result.Value!).Length < 3);
    }
}
