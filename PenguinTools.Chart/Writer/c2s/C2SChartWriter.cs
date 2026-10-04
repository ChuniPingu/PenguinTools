using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;

using C2sModel = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart.Writer.c2s;

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

    private DiagnosticCollector Diagnostic { get; } = new();
    private string OutPath { get; }
    private C2sModel.Chart Chart { get; }
    private int Resolution { get; set; } = 384;

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Resolution = Chart.Extras.SerializationResolution(Chart);
            var sb = new StringBuilder();
            AppendHeaders(sb);

            AppendFormattedEvents(sb);
            sb.AppendLine();
            if (!AppendFormattedNotes(sb))
            {
                return OperationResult.Failure().WithDiagnostics(Diagnostic);
            }

            var text = FinalizeChartText(sb.ToString());
            text = FormatForGame(text);
            await File.WriteAllTextAsync(OutPath, text, ct);
            return OperationResult.Success().WithDiagnostics(Diagnostic);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private void AppendHeaders(StringBuilder sb)
    {
        var version = "1.15.00";
        var bpms = Chart.Events.OfType<C2sModel.Bpm>().Select(x => x.Value).ToArray();
        var fallbackBpm = Chart.Meta.BgmInitialBpm > 0 ? Chart.Meta.BgmInitialBpm : bpms.FirstOrDefault(120m);
        var mainBpm = Chart.Meta.MainBpm > 0 ? Chart.Meta.MainBpm : fallbackBpm;
        var maxBpm = bpms.Length > 0 ? bpms.Max() : mainBpm;
        var minBpm = bpms.Length > 0 ? bpms.Min() : mainBpm;

        sb.AppendLine(CultureInfo.InvariantCulture, $"VERSION\t{version}\t{version}");
        // Song selection and difficulty are supplied by the game's XML.
        sb.AppendLine("MUSIC\t0");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine("DIFFICULT\t0");
        sb.AppendLine("LEVEL\t0.0");
        sb.AppendLine(CultureInfo.InvariantCulture, $"CREATOR\t{Chart.Meta.Designer}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"BPM_DEF\t{(bpms.FirstOrDefault(mainBpm)):F3}\t{mainBpm:F3}\t{maxBpm:F3}\t{minBpm:F3}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"MET_DEF\t{Chart.Meta.BgmInitialDenominator}\t{Chart.Meta.BgmInitialNumerator}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RESOLUTION\t{Resolution}");
        var clock = Chart.Extras.ClockTicks(Chart) * Resolution / 1920;
        sb.AppendLine(CultureInfo.InvariantCulture, $"CLK_DEF\t{clock}");
        sb.AppendLine(Chart.Extras.Headers.GetValueOrDefault("PROGJUDGE_BPM", "PROGJUDGE_BPM\t240.000"));
        sb.AppendLine(Chart.Extras.Headers.GetValueOrDefault("PROGJUDGE_AER", "PROGJUDGE_AER\t  0.999"));
        sb.AppendLine(CultureInfo.InvariantCulture, $"TUTORIAL\t{(Chart.Extras.Tutorial ? 1 : 0)}");
        foreach (var (tag, value) in Chart.Extras.Headers)
        {
            if (tag is not ("CLK_DEF" or "SEQUENCEID" or "PROGJUDGE_BPM" or "PROGJUDGE_AER" or "TUTORIAL" or "VERSION" or "MUSIC" or "DIFFICULT" or "LEVEL" or "CREATOR" or "BPM_DEF" or "MET_DEF"))
            {
                sb.AppendLine(value);
            }
        }

        sb.AppendLine();

    }

    private string RestoreSourceHeaders(string text)
    {
        if (Chart.Extras.SourceMetaKey == ChartExtras.MetaKey(Chart))
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var tag = lines[i].Split('\t')[0];
                if (tag is ("CREATOR" or "BPM_DEF" or "MET_DEF") &&
                    Chart.Extras.Headers.TryGetValue(tag, out var original))
                {
                    lines[i] = original;
                }
            }
            text = string.Join('\n', lines);
        }
        return text;
    }

    private string FinalizeChartText(string text)
    {
        text = RestoreSourceHeaders(text);
        text = Chart.Extras.RestoreSourceOrder(text, Resolution);
        var unchanged = Chart.Extras.SourceKey == GetContentKey(Chart);
        if (unchanged && Chart.Extras.SourceStatistics.Count > 0)
        {
            text += "\n" + string.Join("\n", Chart.Extras.SourceStatistics) + "\n";
        }
        else if (Chart.Extras.SourceKey is null && Chart.Meta.TryGetC2sJudgeSummary(out _, out _, out _, out _, out _, out _))
        {
            var summary = new StringBuilder();
            AppendJudgeSummary(summary);
            text += "\n" + summary;
        }
        else
        {
            text += ChartStatistics.Calculate(text);
        }

        if (Chart.Extras.SourceSnapshot.Length > 0 && (Chart.Extras.SourceKey is null || !Chart.Extras.BinarySnapshotValid))
        {
            Chart.Extras.Resolution = Resolution;
            text = Chart.Extras.FinalizeText(Chart, text);
        }
        return text;
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
        {
            return;
        }

        var tap = C2SJudgeSummaryCalculator.CalculateTap(Chart);
        var flk = C2SJudgeSummaryCalculator.CalculateFlick(Chart);

        hld = AdjustJudgeCount(hld, Chart.Meta.C2sJudgeHldProxyBaseline,
            () => C2SJudgeSummaryCalculator.CalculateHoldProxy(Chart));
        sld = AdjustJudgeCount(sld, Chart.Meta.C2sJudgeSldProxyBaseline,
            () => C2SJudgeSummaryCalculator.CalculateSlideProxy(Chart));
        air = AdjustJudgeCount(air, Chart.Meta.C2sJudgeAirProxyBaseline,
            () => C2SJudgeSummaryCalculator.CalculateAirProxy(Chart));

        var all = tap + hld + sld + air + flk;

        sb.AppendLine(CultureInfo.InvariantCulture, $"T_JUDGE_TAP\t{tap}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"T_JUDGE_HLD\t{hld}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"T_JUDGE_SLD\t{sld}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"T_JUDGE_AIR\t{air}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"T_JUDGE_FLK\t{flk}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"T_JUDGE_ALL\t{all}");
    }

    private static int AdjustJudgeCount(int count, int? baseline, Func<int> calculateProxy)
    {
        if (baseline is not { } value || value < 0)
        {
            return count;
        }

        var adjusted = (long)count + calculateProxy() - value;
        return adjusted is >= 0 and <= int.MaxValue ? (int)adjusted : count;
    }

    internal static string GetContentKey(C2sModel.Chart chart)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var writer = new C2SChartWriter(new C2SWriteRequest("snapshot", chart))
            {
                Resolution = 1920
            };
            var sb = new StringBuilder();
            writer.AppendFormattedEvents(sb);
            if (!writer.AppendFormattedNotes(sb))
            {
                return "invalid";
            }

            sb.AppendLine(chart.Extras.Headers.GetValueOrDefault("PROGJUDGE_BPM", "PROGJUDGE_BPM\t240.000"));
            sb.AppendLine(CultureInfo.InvariantCulture, $"TUTORIAL\t{(chart.Extras.Tutorial ? 1 : 0)}");
            var records = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')).Order(StringComparer.Ordinal);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', records))));
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static readonly string[] EventOrder = ["BPM", "MET", "SLP", "SFL", "DCM", "STP"];

    private void AppendFormattedEvents(StringBuilder sb)
    {
        foreach (var e in Chart.Events.OrderBy(e => Array.IndexOf(EventOrder, e.Id)).ThenBy(e => e.Tick.Original))
        {
            sb.AppendLine(Format(e));
        }

        foreach (var tick in Chart.Extras.ClickTicks.Order())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"CLK\t{tick / 1920}\t{Scale(tick % 1920)}");
        }
    }

    private List<C2sModel.Note> OrderedNotesForWrite()
    {
        // Sort by rounded C2S tick, then stable list index. Same-Round order is
        // the slide/Air FIFO schedule produced by the converter.
        var notes = Chart.Notes
            .Select((note, index) => (note, index))
            .OrderBy(x => x.note.Tick.Round)
            .ThenBy(x => x.note.Lane)
            .ThenBy(x => x.note.Width)
            .ThenBy(x => x.index)
            .Select(x => x.note)
            .ToList();

        var positions = notes
            .Select((note, index) => new { note, index })
            .ToDictionary(x => x.note, x => x.index);

        var groups = notes
            .OfType<C2sModel.AirSlide>()
            .Where(x => x.Parent is C2sModel.AirSlide)
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

}
