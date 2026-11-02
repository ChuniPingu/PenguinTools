using System.Globalization;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sTimelineOverlapTests
{
    private static readonly int[] MainSampleTicks = [0, 24, 48, 72, 84];
    private static readonly int[] AliasSampleTicks = [0, 120, 144];

    private static async Task<U.Chart> ReadUgc(string body, int denominator = 4, bool offset = false, int version = 8)
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path,
                $"@VER\t{version}\n@TICKS\t480\n@BPM\t0'0\t120\n@BEAT\t0\t1\t{denominator}\n@FLAG\tSOFFSET\t{offset}\n" + body,
                TestContext.Current.CancellationToken);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()),
                TestMediaTool.Instance).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }

    private static C.Chart Convert(U.Chart source, bool expectWarning = false)
    {
        var result = new C2SChartConverter(new C2SConvertRequest(source)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(expectWarning, result.Diagnostics.Diagnostics.Any(d => d.Severity >= Severity.Warning));
        return result.Value!;
    }

    private static async Task<string[][]> Serialize(C.Chart chart)
    {
        var path = TestTempPaths.Create(".c2s");
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            return (await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken))
                .Where(s => s.Length > 0).Select(s => s.Split('\t')).ToArray();
        }
        finally { File.Delete(path); }
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    private static int Tick(string[] row) => Int(row[1]) * 384 + Int(row[2]);
    private static int Selected(string[][] rows, int tick, decimal lane, decimal width) =>
        rows.Where(r => r[0] == "SLA" && Tick(r) <= tick && tick < Tick(r) + Int(r[5])
            && Int(r[3]) <= lane && lane + width <= Int(r[3]) + Int(r[4]))
            .Select(r => Int(r[6])).Append(0).Max();

    private static decimal Speed(string[][] rows, int timeline, int tick) =>
        1m + rows.Where(r => r[0] == "SLP" && Int(r[5]) == timeline && Tick(r) <= tick && tick < Tick(r) + Int(r[3]))
            .Sum(r => decimal.Parse(r[4], CultureInfo.InvariantCulture) - 1m);

    [Fact]
    public async Task NestedAndReversedContainmentPreserveEachScrollCurve()
    {
        var source = await ReadUgc(
            "@TIL\t0\t0'0\t5\n@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t3\n@TIL\t3\t0'0\t4\n" +
            "@USETIL\t3\n#0'480:t08\n@USETIL\t2\n#0'480:t16\n@USETIL\t1\n#0'480:t24\n@USETIL\t0\n#0'480:t31\n" +
            "@USETIL\t1\n#0'960:t08\n@USETIL\t3\n#0'960:t12\n" +
            "@USETIL\t3\n#0'1440:t08\n@USETIL\t2\n#0'1440:t16\n");
        var before = source.Notes.Children.OfType<U.Tap>().Select(n => n.Timeline).ToArray();

        var rows = await Serialize(Convert(source));

        foreach (var (tick, lane, width, speed) in new[]
                 { (96, 0, 8, 4m), (96, 1, 6, 3m), (96, 2, 4, 2m), (96, 3, 1, 5m), (192, 0, 8, 2m), (192, 1, 2, 4m) })
        {
            Assert.Equal(speed, Speed(rows, Selected(rows, tick, lane, width), 48));
        }
        Assert.Equal(Selected(rows, 96, 1, 6), Selected(rows, 288, 1, 6));
        Assert.Equal(before, source.Notes.Children.OfType<U.Tap>().Select(n => n.Timeline));
        Assert.Equal([0, 1, 2, 3], source.Events.Children.OfType<U.ScrollSpeedEvent>().Select(e => e.Timeline).Order());
    }

    [Fact]
    public async Task MainTimelineAliasCopiesStopsNegativeSpeedsAndImplicitDefault()
    {
        var source = await ReadUgc(
            "@TIL\t0\t0'120\t2\n@TIL\t0\t0'240\t0\n@TIL\t0\t0'360\t-1\n@TIL\t0\t0'420\t1\n" +
            "@TIL\t2\t0'0\t3\n@USETIL\t2\n#0'480:t04\n@USETIL\t0\n#0'480:t12\n");

        var rows = await Serialize(Convert(source));
        var inner = Selected(rows, 96, 1, 2);

        Assert.True(inner > Selected(rows, 96, 0, 4));
        Assert.Equal([1m, 2m, 0m, -1m, 1m], MainSampleTicks.Select(tick => Speed(rows, inner, tick)));
    }

    [Theory]
    [InlineData(4, 0, 0)]
    [InlineData(7, 10, 11)]
    [InlineData(13, 3, 4)]
    public async Task NestingUsesSerializedTicksAfterOpeningMeasure(int denominator, int outerTick, int innerTick)
    {
        var source = await ReadUgc(
            "@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t3\n" +
            $"@USETIL\t2\n#0'{outerTick}:t04\n@USETIL\t1\n#0'{innerTick}:t12\n",
            denominator, offset: true);

        var rows = await Serialize(Convert(source));
        var taps = rows.Where(r => r[0] == "TAP").ToArray();
        var tick = Tick(taps[0]);
        Assert.Equal(tick, Tick(taps[1]));
        Assert.Equal(3m, Speed(rows, Selected(rows, tick, 0, 4), 1));
        Assert.Equal(2m, Speed(rows, Selected(rows, tick, 1, 2), 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoincidentNotesWarnOnlyForDifferentWholeScrollFunctions(bool equivalent)
    {
        var source = await ReadUgc(
            $"@TIL\t1\t0'0\t2\n@TIL\t1\t0'120\t2\n@TIL\t1\t0'240\t3\n@TIL\t2\t0'0\t{(equivalent ? 2 : 4)}\n@TIL\t2\t0'240\t3\n" +
            "@USETIL\t1\n#0'480:t12\n@USETIL\t2\n#0'482:t12\n");
        var converted = new C2SChartConverter(new C2SConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var warnings = converted.Diagnostics.Diagnostics.Where(d => d.Severity >= Severity.Warning).ToArray();
        if (equivalent)
        {
            Assert.Empty(warnings);
        }
        else
        {
            var warning = Assert.Single(warnings);
            Assert.Equal(MsgKeys.Mg_Note_overlapped_in_different_TIL, warning.Message.Key);
            var pair = Assert.IsType<NotePairDiagnosticTarget>(warning.Target);
            Assert.Equal(1, pair.Left.Timeline);
            Assert.Equal(2, pair.Right.Timeline);
            Assert.Equal(480, pair.Left.Tick);
            Assert.Equal(482, pair.Right.Tick);
            Assert.NotNull(pair.TimePosition);
        }

        var rows = await Serialize(converted.Value!);
        var timeline = Selected(rows, 96, 1, 2);
        Assert.Equal(equivalent ? 2m : 4m, Speed(rows, timeline, 0));
        Assert.Equal(3m, Speed(rows, timeline, 48));
    }

    [Fact]
    public async Task EqualCurvesRemainEquivalentWhenOtherNotesExtendOneTimeline()
    {
        var source = await ReadUgc(
            "@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t1\n#0'480:t12\n#0'960:t84\n@USETIL\t2\n#0'480:t12\n");
        var rows = await Serialize(Convert(source));
        Assert.Equal(2m, Speed(rows, Selected(rows, 96, 1, 2), 48));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AirCrashRungsParticipateAsInnerAndOuterObjects(bool mainInside)
    {
        var source = await ReadUgc(
            "@TIL\t2\t0'0\t3\n" +
            (mainInside
                ? "@USETIL\t0\n#0'0:CH20A1,30\n#480:cH20A\n@USETIL\t2\n#0'240:tG4\n"
                : "@USETIL\t2\n#0'0:CG40A1,30\n#480:cG40A\n@USETIL\t0\n#0'240:tH2\n"),
            version: 9);

        var rows = await Serialize(Convert(source));
        Assert.Equal(3m, Speed(rows, Selected(rows, 48, 0, 4), 24));
        Assert.Equal(1m, Speed(rows, Selected(rows, 48, 1, 2), 24));
        Assert.Single(rows, r => r[0] == "ALD");
    }

    [Fact]
    public async Task CoincidentAirCrashRungReportsOnlyTheLostCurve()
    {
        var source = await ReadUgc("@TIL\t2\t0'0\t3\n@USETIL\t2\n#0'0:CG40A1,30\n#480:cG40A\n@USETIL\t0\n#0'240:tG4\n",
            version: 9);

        var converted = new C2SChartConverter(new C2SConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var warning = Assert.Single(converted.Diagnostics.Diagnostics, d => d.Severity >= Severity.Warning);
        var pair = Assert.IsType<NotePairDiagnosticTarget>(warning.Target);
        Assert.Equal("Tap", pair.Left.Type);
        Assert.Equal("AirCrashJoint", pair.Right.Type);
        Assert.Equal(240, pair.Left.Tick);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("ugc")]
    [InlineData("mgxc")]
    public async Task EditedMainTimelineNoteInvalidatesSlaSnapshotAndRetainsAliasInWrittenFile(string format)
    {
        var path = TestTempPaths.Create(".c2s");
        U.Chart source;
        try
        {
            await File.WriteAllTextAsync(path,
                "VERSION\t1.15.00\t1.15.00\nRESOLUTION\t384\nBPM\t0\t0\t120\nMET\t0\t0\t4\t4\n" +
                "SFL\t0\t0\t384\t2\nSTP\t0\t120\t24\nSLP\t0\t0\t384\t3\t2\n" +
                "SLA\t0\t0\t0\t4\t385\t2\nTAP\t0\t192\t0\t4\nTAP\t0\t192\t8\t2\n",
                TestContext.Current.CancellationToken);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var editor = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
            Assert.True(editor.Succeeded, editor.ToString());
            source = editor.Value!;
        }
        finally { File.Delete(path); }

        if (format != "direct")
        {
            source = await EditorRoundTrip(source, format);
        }
        var inner = Assert.Single(source.Notes.Children.OfType<U.Tap>(), n => n.Timeline == 0);
        inner.Lane = 1;

        var rows = await Serialize(Convert(source));
        var alias = Selected(rows, 192, 1, 2);

        Assert.True(alias > Selected(rows, 192, 0, 4));
        Assert.Equal([2m, 1m, 2m], AliasSampleTicks.Select(tick => Speed(rows, alias, tick)));
        Assert.Equal(0, inner.Timeline);
    }

    private static async Task<U.Chart> EditorRoundTrip(U.Chart source, string format)
    {
        var path = TestTempPaths.Create("." + format);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = format == "ugc"
                ? await new UgcChartWriter(path, source).WriteAsync(ct)
                : await new MgxcChartWriter(new MgxcWriteRequest(path, source)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var read = format == "ugc"
                ? await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(read.Succeeded, read.ToString());
            return read.Value!;
        }
        finally { File.Delete(path); }
    }


    [Fact]
    public async Task ExpandedCurveJointsParticipateInTimelineAllocation()
    {
        var source = await ReadUgc("@TIL\t1\t0'0\t2\n@TIL\t2\t0'0\t3\n@USETIL\t1\n#0'0:tF1\n" +
            "@USETIL\t2\n#0'480:s04\n#480:s44\n");
        var slide = Assert.Single(source.Notes.Children.OfType<U.Slide>());
        var handle = new U.SlideJoint { Tick = 720, Lane = 6, Width = 4, Timeline = 2, Joint = Joint.C, IsCurveControl = true };
        slide.InsertBefore(handle, slide.FirstChild);
        var generated = Convert(source).Notes.OfType<C.Slide>().First(n => n.Tick.Original > 480 && n.Tick.Original < 960);
        source.Notes.AppendChild(new U.Tap { Tick = generated.Tick, Lane = generated.Lane + 1, Width = 1, Timeline = 1 });

        var rows = await Serialize(Convert(source));
        var tick = generated.Tick.Scaled;
        Assert.Equal(3m, Speed(rows, Selected(rows, tick, generated.Lane, generated.Width), 48));
        Assert.Equal(2m, Speed(rows, Selected(rows, tick, generated.Lane + 1, 1), 48));
        Assert.True(handle.IsCurveControl);
        Assert.Equal(2, slide.Children.Count);
    }

    [Fact]
    public async Task AirCrashDensityEditInvalidatesSavedSlaCoverage()
    {
        var native = new C.Chart();
        native.Events.Add(new C.Bpm { Tick = 0, Value = 120 });
        native.Events.Add(new C.Met { Tick = 0, Numerator = 4, Denominator = 4 });
        native.Events.Add(new C.Slp { Tick = 0, Length = 485, Timeline = 2, Speed = 3 });
        native.Notes.Add(new C.Sla { Tick = 0, Length = 5, Lane = 0, Width = 4, Timeline = 2 });
        native.Notes.Add(new C.Sla { Tick = 480, Length = 5, Lane = 0, Width = 4, Timeline = 2 });
        native.Notes.Add(new C.AirCrash { Tick = 0, EndTick = 480, Lane = 0, Width = 4, EndLane = 0, EndWidth = 4, Density = 25 });
        var editor = new UgcChartConverter(new UgcConvertRequest(native)).Convert();
        Assert.True(editor.Succeeded, editor.ToString());
        var source = editor.Value!;
        Assert.Equal(0, Selected(await Serialize(Convert(source)), 5, 0, 4));

        Assert.Single(source.Notes.Children.OfType<U.AirCrash>()).Density = 30;

        var rows = await Serialize(Convert(source));
        Assert.Equal(3m, Speed(rows, Selected(rows, 6, 0, 4), 0));
    }

    [Fact]
    public async Task UncoverableAirCrashRungWithEquivalentDefaultCurveDoesNotWarn()
    {
        var source = await ReadUgc("@TIL\t2\t0'0\t1\n@USETIL\t2\n#0'0:CGG0A1,30\n#480:cHG0A\n", version: 9);
        var rows = await Serialize(Convert(source));
        Assert.Equal(1m, Speed(rows, Selected(rows, 6, .0625m, 16), 0));
        Assert.All(rows.Where(r => r[0] == "SLA"), row => Assert.InRange(Int(row[4]), 1, 16));
    }

    [Fact]
    public async Task ExtendedFieldRegionsStayWithinNativeSlaWidthLimit()
    {
        var source = await ReadUgc("@FLAG\tEXFIELD\tTRUE\n@TIL\t2\t0'0\t3\n@USETIL\t2\n#0'480:t01\n#0'480:tl1\n", version: 9);
        var rows = await Serialize(Convert(source));
        Assert.All(rows.Where(r => r[0] == "SLA"), row => Assert.InRange(Int(row[4]), 1, 16));
        Assert.Equal(3m, Speed(rows, Selected(rows, 96, -16, 1), 48));
        Assert.Equal(3m, Speed(rows, Selected(rows, 96, 31, 1), 48));
    }


    [Fact]
    public async Task FutureSpeedChangesAfterCoincidentTapsDoNotWarn()
    {
        var source = await ReadUgc("@TIL\t1\t0'0\t2\n@TIL\t1\t0'960\t3\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t1\n#0'480:t04\n#0'1440:tC1\n@USETIL\t2\n#0'480:t04\n");
        var rows = await Serialize(Convert(source));
        Assert.Equal(2m, Speed(rows, Selected(rows, 96, 0, 4), 48));
    }

    [Fact]
    public async Task CoincidentLongAndShortNotesUseTheCurveNeededByTheLongTail()
    {
        var source = await ReadUgc("@TIL\t1\t0'0\t2\n@TIL\t1\t0'720\t3\n@TIL\t2\t0'0\t2\n" +
            "@USETIL\t1\n#0'480:h04\n#480:s\n@USETIL\t2\n#0'480:t04\n");
        var rows = await Serialize(Convert(source));
        var timeline = Selected(rows, 96, 0, 4);
        Assert.Equal(2m, Speed(rows, timeline, 48));
        Assert.Equal(3m, Speed(rows, timeline, 144));
    }

    [Fact]
    public async Task HighTimelineIdsCanStillAllocateAnInnerAlias()
    {
        var source = await ReadUgc("@TIL\t2147483647\t0'0\t3\n@TIL\t1\t0'0\t2\n" +
            "@USETIL\t2147483647\n#0'480:t04\n@USETIL\t1\n#0'480:t12\n");

        var rows = await Serialize(Convert(source));
        Assert.Equal(3m, Speed(rows, Selected(rows, 96, 0, 4), 48));
        Assert.Equal(2m, Speed(rows, Selected(rows, 96, 1, 2), 48));
    }
}
