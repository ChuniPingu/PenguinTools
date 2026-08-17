using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using Xunit;
using c2s = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Tests.Parser;

public sealed class OfficialC2sRoundTripTests
{
    private static bool TryGetAssetDirectory(out string directory)
    {
        var cursor = AppContext.BaseDirectory;

        while (cursor is not null)
        {
            if (string.Equals(
                    Path.GetFileName(cursor),
                    "PenguinTools.Tests",
                    StringComparison.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(
                    cursor,
                    "Assets");

                if (Directory.Exists(candidate) &&
                    Directory.EnumerateFiles(
                            candidate,
                            "*.c2s",
                            SearchOption.TopDirectoryOnly)
                        .Any())
                {
                    directory = candidate;
                    return true;
                }
            }

            cursor = Directory.GetParent(cursor)?.FullName;
        }

        directory = string.Empty;
        return false;
    }

    public static IEnumerable<object[]> OfficialChartFiles()
    {
        if (!TryGetAssetDirectory(out var assetDirectory))
            return [];

        var tracked = TrackedFailedCharts.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory.GetFiles(
                assetDirectory,
                "*.c2s",
                SearchOption.TopDirectoryOnly)
            .Where(path => tracked.Contains(Path.GetFileNameWithoutExtension(path)!))
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(path => new object[]
            {
                Path.GetFileNameWithoutExtension(path)!,
                path
            });
    }

    // Former parse failures (unpaired AirHold/AirSlide) and overflow on cmmt.
    private static readonly string[] TrackedFailedCharts =
    [
        "0180_03",
        "0320_03",
        "0390_03",
        "0440_03",
        "0531_03",
        "0594_03",
        "0761_03",
        "0772_03",
        "0862_03",
        "1029_03",
        "1086_03",
        "2033_03",
        "2054_03",
        "2079_03",
        "2090_03",
        "2175_03",
        "2429_03",
        "8086_04",
        "8206_05",
        "8273_05",
        "8294_05",
        "8302_05"
    ];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(OfficialChartFiles))]
    public Task OfficialCharts_C2sMgxcC2s_FirstRoundPreservesJudgeSummary(
        string _,
        string file) =>
        RoundTripFile(file);

    private static async Task RoundTripFile(string sourcePath)
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Path.GetFileNameWithoutExtension(sourcePath);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsOfficialRoundTrip",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var original = await new C2SParser(
                    new C2SParseRequest(sourcePath))
                .ParseAsync(ct);

            Assert.True(
                original.Succeeded,
                $"{name}: source C2S parse failed: {original}");

            Assert.True(
                original.Value!.Meta.TryGetC2sJudgeSummary(
                    out var sourceTap,
                    out var sourceHld,
                    out var sourceSld,
                    out var sourceAir,
                    out var sourceFlk,
                    out var sourceAll),
                $"{name}: source C2S has no T_JUDGE summary.");

            var sourceNoteSnapshot = NoteSnapshot(original.Value!);
            var sourceEventSnapshot = EventSnapshot(original.Value!);
            var sourceMetaSnapshot = MetaSnapshot(original.Value!);

            var currentC2sPath = sourcePath;

            for (var round = 1; round <= 3; round++)
            {
                var mgxcPath = Path.Combine(
                    directory,
                    $"{name}_{round}.mgxc");

                var c2sPath = Path.Combine(
                    directory,
                    $"{name}_{round}.c2s");

                var parsedC2s = await new C2SParser(
                        new C2SParseRequest(currentC2sPath))
                    .ParseAsync(ct);

                Assert.True(
                    parsedC2s.Succeeded,
                    $"{name}: round {round} input C2S parse failed: {parsedC2s}");

                var toUmgr = new UgcChartConverter(
                    new UgcConvertRequest(
                        parsedC2s.Value!))
                    .Convert();

                Assert.True(
                    toUmgr.Succeeded,
                    $"{name}: round {round} C2S to UMGR conversion failed: {toUmgr}");

                var mgxcWritten = await new MgxcChartWriter(
                        new MgxcWriteRequest(
                            mgxcPath,
                            toUmgr.Value!))
                    .WriteAsync(ct);

                Assert.True(
                    mgxcWritten.Succeeded,
                    $"{name}: round {round} MGXC write failed: {mgxcWritten}");

                Assert.True(
                    File.Exists(mgxcPath),
                    $"{name}: round {round} MGXC output was not created.");

                var mgxcParsed = await new MgxcParser(
                        new MgxcParseRequest(
                            mgxcPath,
                            TestAssets.Load()),
                        TestMediaTool.Instance)
                    .ParseAsync(ct);

                var mgxcDiagnosticText = string.Join(
                    Environment.NewLine,
                    mgxcParsed.Diagnostics.Diagnostics.Select(
                        diagnostic =>
                        {
                            var args = diagnostic.Message.Args is null
                                ? string.Empty
                                : string.Join(
                                    ", ",
                                    diagnostic.Message.Args.Select(
                                        pair =>
                                            $"{pair.Key}={pair.Value}"));

                            var target = diagnostic.Target
                                is PenguinTools.Chart.Models.umgr.Note[] notes
                                ? string.Join(
                                    " -> ",
                                    notes.Select(
                                        note =>
                                            note is null
                                                ? "null"
                                                : $"{note.GetType().Name}" +
                                                  $"@tick={note.Tick.Original}" +
                                                  $",lane={note.Lane}" +
                                                  $",width={note.Width}"))
                                : diagnostic.Target?.ToString() ??
                                  "null";

                            return
                                $"{diagnostic.Severity}: " +
                                $"{diagnostic.Message.Key}; " +
                                $"time={diagnostic.Time?.ToString() ?? "-"}; " +
                                $"location={diagnostic.FormattedLocation ?? "-"}; " +
                                $"args=[{args}]; " +
                                $"target={target}";
                        }));

                Assert.True(
                    mgxcParsed.Succeeded,
                    $"{name}: round {round} MGXC parse failed:" +
                    Environment.NewLine +
                    mgxcDiagnosticText);

                var backToC2s = new C2SChartConverter(
                    new C2SConvertRequest(
                        mgxcParsed.Value!))
                    .Convert();

                var backToC2sDiagnosticText = string.Join(
                    Environment.NewLine,
                    backToC2s.Diagnostics.Diagnostics.Select(
                        diagnostic =>
                            $"{diagnostic.Severity}: " +
                            $"{diagnostic.Message.Key}; " +
                            $"target={diagnostic.Target}"));

                Assert.True(
                    backToC2s.Succeeded,
                    $"{name}: round {round} MGXC to C2S conversion failed:" +
                    Environment.NewLine +
                    backToC2sDiagnosticText);

                var c2sWritten = await new C2SChartWriter(
                        new C2SWriteRequest(
                            c2sPath,
                            backToC2s.Value!))
                    .WriteAsync(ct);

                Assert.True(
                    c2sWritten.Succeeded,
                    $"{name}: round {round} C2S write failed: {c2sWritten}");

                Assert.True(
                    File.Exists(c2sPath),
                    $"{name}: round {round} C2S output was not created.");

                var roundTrip = await new C2SParser(
                        new C2SParseRequest(c2sPath))
                    .ParseAsync(ct);

                Assert.True(
                    roundTrip.Succeeded,
                    $"{name}: round {round} output C2S parse failed: {roundTrip}");

                var roundTripNoteSnapshot =
                    NoteSnapshot(roundTrip.Value!);

                Assert.True(
                    sourceNoteSnapshot.Length == roundTripNoteSnapshot.Length,
                    $"{name}: round {round} note count changed " +
                    $"{sourceNoteSnapshot.Length} -> {roundTripNoteSnapshot.Length}");

                for (var i = 0; i < sourceNoteSnapshot.Length; i++)
                {
                    Assert.True(
                        sourceNoteSnapshot[i] == roundTripNoteSnapshot[i],
                        $"{name}: round {round} note mismatch at index {i}" +
                        Environment.NewLine +
                        $"Expected: {sourceNoteSnapshot[i]}" +
                        Environment.NewLine +
                        $"Actual:   {roundTripNoteSnapshot[i]}");
                }

                var roundTripEventSnapshot =
                    EventSnapshot(roundTrip.Value!);

                Assert.True(
                    sourceEventSnapshot.Length == roundTripEventSnapshot.Length,
                    $"{name}: round {round} event count changed " +
                    $"{sourceEventSnapshot.Length} -> {roundTripEventSnapshot.Length}");

                for (var i = 0; i < sourceEventSnapshot.Length; i++)
                {
                    Assert.True(
                        sourceEventSnapshot[i] == roundTripEventSnapshot[i],
                        $"{name}: round {round} event mismatch at index {i}" +
                        Environment.NewLine +
                        $"Expected: {sourceEventSnapshot[i]}" +
                        Environment.NewLine +
                        $"Actual:   {roundTripEventSnapshot[i]}");
                }

                var roundTripMetaSnapshot =
                    MetaSnapshot(roundTrip.Value!);

                Assert.True(
                    sourceMetaSnapshot == roundTripMetaSnapshot,
                    $"{name}: round {round} meta changed" +
                    Environment.NewLine +
                    $"Expected: {sourceMetaSnapshot}" +
                    Environment.NewLine +
                    $"Actual:   {roundTripMetaSnapshot}");

                var hasRoundTripSummary =
                    roundTrip.Value!.Meta.TryGetC2sJudgeSummary(
                        out var roundTripTap,
                        out var roundTripHld,
                        out var roundTripSld,
                        out var roundTripAir,
                        out var roundTripFlk,
                        out var roundTripAll);

                Assert.True(
                    hasRoundTripSummary,
                    $"{name}: round {round} T_JUDGE summary disappeared.");

                Assert.True(
                    sourceTap == roundTripTap,
                    $"{name}: round {round} TAP changed {sourceTap} -> {roundTripTap}");

                Assert.True(
                    sourceHld == roundTripHld,
                    $"{name}: round {round} HLD changed {sourceHld} -> {roundTripHld}");

                Assert.True(
                    sourceSld == roundTripSld,
                    $"{name}: round {round} SLD changed {sourceSld} -> {roundTripSld}");

                Assert.True(
                    sourceAir == roundTripAir,
                    $"{name}: round {round} AIR changed {sourceAir} -> {roundTripAir}");

                Assert.True(
                    sourceFlk == roundTripFlk,
                    $"{name}: round {round} FLK changed {sourceFlk} -> {roundTripFlk}");

                Assert.True(
                    sourceAll == roundTripAll,
                    $"{name}: round {round} ALL changed {sourceAll} -> {roundTripAll}");

                currentC2sPath = c2sPath;
            }
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    private static string MetaSnapshot(c2s.Chart chart)
    {
        var meta = chart.Meta;

        var musicId =
            meta.Id ??
            (int.TryParse(
                meta.MgxcId,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedId)
                ? parsedId
                : 0);

        return string.Join(
            "|",
            musicId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            meta.Difficulty.ToString(),
            meta.Level.ToString(System.Globalization.CultureInfo.InvariantCulture),
            meta.Designer,
            meta.MainBpm.ToString(System.Globalization.CultureInfo.InvariantCulture),
            meta.BgmInitialDenominator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            meta.BgmInitialNumerator.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string[] EventSnapshot(c2s.Chart chart) =>
        chart.Events
            .Select(EventKey)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static string EventKey(c2s.Event e)
    {
        var head =
            $"{e.GetType().Name}|{e.Tick.Original}";

        return e switch
        {
            c2s.Bpm x =>
                $"{head}|{x.Value}",

            c2s.Met x =>
                $"{head}|{x.Numerator}|{x.Denominator}",

            c2s.Slp x =>
                $"{head}|{x.Timeline}|{x.Length.Original}|{x.Speed}",

            c2s.Dcm x =>
                $"{head}|{x.Length.Original}|{x.Speed}",

            c2s.SpeedEventBase x =>
                $"{head}|{x.Length.Original}|{x.Speed}",

            _ => head
        };
    }

    private static string[] NoteSnapshot(c2s.Chart chart) =>
        chart.Notes
            .Select(NoteKey)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static string NoteKey(c2s.Note note)
    {
        var head =
            $"{note.GetType().Name}|{note.Tick.Original}|{note.Timeline}|" +
            $"{note.Lane}|{note.Width}";

        return note switch
        {
            c2s.ExTap x =>
                $"{head}|{x.Effect}",

            c2s.Hold x =>
                $"{head}|{x.EndTick.Original}|{x.EndLane}|{x.EndWidth}|{x.Effect}",

            c2s.Sla x =>
                $"{head}|{x.Length.Original}",

            c2s.Slide x =>
                $"{head}|{x.EndTick.Original}|{x.EndLane}|{x.EndWidth}|" +
                $"{x.Joint}|{x.NoLine}|{x.Effect}",

            c2s.Air x =>
                $"{head}|{x.Direction}|{x.Color}|{ParentKey(x.Parent)}",

            c2s.AirSlide x =>
                $"{head}|{x.EndTick.Original}|{x.EndLane}|{x.EndWidth}|" +
                $"{x.Height.Original}|{x.EndHeight.Original}|{x.Joint}|" +
                $"{x.Color}|{ParentKey(x.Parent)}",

            c2s.AirHold x =>
                $"{head}|{x.EndTick.Original}|{x.EndLane}|{x.EndWidth}|" +
                $"{x.Joint}|{x.Color}|{ParentKey(x.Parent)}",

            c2s.AirCrash x =>
                $"{head}|{x.EndTick.Original}|{x.EndLane}|{x.EndWidth}|" +
                $"{x.Height.Original}|{x.EndHeight.Original}|" +
                $"{x.Density.Original}|{x.Color}|{x.Attr}",

            _ => head
        };
    }

    private static string ParentKey(c2s.Note? note) =>
        note is null
            ? "null"
            : $"{note.Id}@{note.Tick.Original}:{note.Timeline}:" +
              $"{note.Lane}:{note.Width}";
}
