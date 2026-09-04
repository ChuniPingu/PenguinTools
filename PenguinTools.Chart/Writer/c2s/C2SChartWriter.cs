using System.Globalization;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Writer.c2s;

using c2s = Models.c2s;

public partial class C2SChartWriter
{
    public C2SChartWriter(C2SWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutPath);
        ArgumentNullException.ThrowIfNull(request.Chart);

        OutPath = request.OutPath;
        Chart = request.Chart;
        Diagnostic.TimeCalculator = request.TimeCalculator;
    }

    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private string OutPath { get; }
    private c2s.Chart Chart { get; }
    private bool EmitV115 { get; set; }

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        EmitV115 = NeedsV115(Chart);
        var version = EmitV115 ? "1.15.00" : "1.14.00";
        var musicId = Chart.Meta.Id
                      ?? (int.TryParse(Chart.Meta.MgxcId, NumberStyles.Integer, CultureInfo.InvariantCulture,
                          out var parsedId)
                          ? parsedId
                          : 0);
        var bpms = Chart.Events.OfType<c2s.Bpm>().Select(x => x.Value).ToArray();
        var mainBpm = Chart.Meta.MainBpm > 0 ? Chart.Meta.MainBpm
            : Chart.Meta.BgmInitialBpm > 0 ? Chart.Meta.BgmInitialBpm
            : bpms.FirstOrDefault(120m);
        var maxBpm = bpms.Length > 0 ? bpms.Max() : mainBpm;
        var minBpm = bpms.Length > 0 ? bpms.Min() : mainBpm;

        sb.AppendLine($"VERSION\t{version}\t{version}");
        sb.AppendLine($"MUSIC\t{musicId}");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine($"DIFFICULT\t{DifficultyValue(Chart.Meta.Difficulty):00}");
        sb.AppendLine($"LEVEL\t{FormatLevel(Chart.Meta.Level)}");
        sb.AppendLine($"CREATOR\t{Chart.Meta.Designer}");
        sb.AppendLine($"BPM_DEF\t{mainBpm:F3}\t{mainBpm:F3}\t{maxBpm:F3}\t{minBpm:F3}");
        sb.AppendLine($"MET_DEF\t{Chart.Meta.BgmInitialDenominator}\t{Chart.Meta.BgmInitialNumerator}");
        sb.AppendLine("RESOLUTION\t384");
        sb.AppendLine("CLK_DEF\t384");
        sb.AppendLine("PROGJUDGE_BPM\t240.000");
        sb.AppendLine("PROGJUDGE_AER\t  0.999");
        sb.AppendLine("TUTORIAL\t0");
        AppendJudgeSummary(sb);
        sb.AppendLine();

        AppendFormattedEvents(sb);
        sb.AppendLine();
        if (!AppendFormattedNotes(sb))
            return OperationResult.Failure().WithDiagnostics(Diagnostic);

        await File.WriteAllTextAsync(OutPath, sb.ToString(), ct);
        return OperationResult.Success().WithDiagnostics(Diagnostic);
    }

    private void AppendJudgeSummary(StringBuilder sb)
    {
        if (!Chart.Meta.TryGetC2sJudgeSummary(
                out _,
                out var hld,
                out var sld,
                out var air,
                out _,
                out _))
            return;

        var tap = C2SJudgeSummaryCalculator.CalculateTap(Chart);
        var flk = C2SJudgeSummaryCalculator.CalculateFlick(Chart);

        if (Chart.Meta.C2sJudgeHldProxyBaseline is int hldProxyBaseline &&
            hldProxyBaseline >= 0)
        {
            var currentHldProxy =
                C2SJudgeSummaryCalculator.CalculateHoldProxy(Chart);

            var adjustedHld =
                (long)hld +
                currentHldProxy -
                hldProxyBaseline;

            if (adjustedHld >= 0 &&
                adjustedHld <= int.MaxValue)
            {
                hld = (int)adjustedHld;
            }
        }

        if (Chart.Meta.C2sJudgeSldProxyBaseline is int sldProxyBaseline &&
            sldProxyBaseline >= 0)
        {
            var currentSldProxy =
                C2SJudgeSummaryCalculator.CalculateSlideProxy(Chart);

            var adjustedSld =
                (long)sld +
                currentSldProxy -
                sldProxyBaseline;

            if (adjustedSld >= 0 &&
                adjustedSld <= int.MaxValue)
            {
                sld = (int)adjustedSld;
            }
        }

        if (Chart.Meta.C2sJudgeAirProxyBaseline is int airProxyBaseline &&
            airProxyBaseline >= 0)
        {
            var currentAirProxy =
                C2SJudgeSummaryCalculator.CalculateAirProxy(Chart);

            var adjustedAir =
                (long)air +
                currentAirProxy -
                airProxyBaseline;

            if (adjustedAir >= 0 &&
                adjustedAir <= int.MaxValue)
            {
                air = (int)adjustedAir;
            }
        }

        var all = tap + hld + sld + air + flk;

        sb.AppendLine($"T_JUDGE_TAP\t{tap}");
        sb.AppendLine($"T_JUDGE_HLD\t{hld}");
        sb.AppendLine($"T_JUDGE_SLD\t{sld}");
        sb.AppendLine($"T_JUDGE_AIR\t{air}");
        sb.AppendLine($"T_JUDGE_FLK\t{flk}");
        sb.AppendLine($"T_JUDGE_ALL\t{all}");
    }

    private void AppendFormattedEvents(StringBuilder sb)
    {
        foreach (var e in Chart.Events) sb.AppendLine(Format(e));
    }

    private IEnumerable<c2s.Note> OrderedNotesForWrite()
    {
        // Sort by rounded C2S tick, then stable list index. Same-Round order is
        // the slide/Air FIFO schedule produced by the converter.
        var notes = Chart.Notes
            .Select((note, index) => (note, index))
            .OrderBy(x => x.note.Tick.Round)
            .ThenBy(x => x.index)
            .Select(x => x.note)
            .ToList();

        var positions = notes
            .Select((note, index) => new { note, index })
            .ToDictionary(x => x.note, x => x.index);

        var groups = notes
            .OfType<c2s.AirSlide>()
            .Where(x => x.Parent is c2s.AirSlide)
            .GroupBy(x => (
                Tick: x.Tick.Original,
                x.Lane,
                x.Width,
                Height: x.Height.Result,
                ParentId: x.Parent!.Id))
            .Where(x => x.Count() > 1)
            .OrderBy(x => x.Key.Tick)
            .ToArray();

        foreach (var group in groups)
        {
            var slots = group
                .Select(x => positions[x])
                .OrderBy(x => x)
                .ToArray();

            var ordered = group
                .OrderBy(x => positions[x.Parent!])
                .ToArray();

            for (var i = 0; i < slots.Length; i++)
            {
                notes[slots[i]] = ordered[i];
                positions[ordered[i]] = slots[i];
            }
        }

        return notes;
    }

    private bool AppendFormattedNotes(StringBuilder sb)
    {
        var hasError = false;
        foreach (var n in OrderedNotesForWrite())
        {
            if (TryFormat(n, out var line, out var error) && error is null)
            {
                sb.AppendLine(line);
                continue;
            }

            Diagnostic.Report(new TimedDiagnostic(Severity.Error, error!, n.Tick.Original)
            {
                Target = n
            });
            hasError = true;
        }

        return !hasError;
    }

    private static string FormatLevel(decimal level) =>
        level.ToString("0.0", CultureInfo.InvariantCulture);

    // TODO: version switch should be be removed on 2027/01/01
    internal static bool NeedsV115(c2s.Chart chart) =>
        chart.Notes.OfType<c2s.AirCrash>().Any(x => x.Attr != AirLadderAttr.DEF) ||
        chart.Notes.OfType<c2s.Slide>().Any(x => x.NoLine);

    private static int DifficultyValue(Difficulty d) => d switch
    {
        Difficulty.WorldsEnd => 4,
        Difficulty.Ultima => 5,
        _ => (int)d
    };
}
