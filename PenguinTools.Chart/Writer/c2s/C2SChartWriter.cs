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
        sb.AppendLine();

        AppendFormattedEvents(sb);
        sb.AppendLine();
        if (!AppendFormattedNotes(sb))
            return OperationResult.Failure().WithDiagnostics(Diagnostic);

        await File.WriteAllTextAsync(OutPath, sb.ToString(), ct);
        return OperationResult.Success().WithDiagnostics(Diagnostic);
    }

    private void AppendFormattedEvents(StringBuilder sb)
    {
        foreach (var e in Chart.Events) sb.AppendLine(Format(e));
    }

    private bool AppendFormattedNotes(StringBuilder sb)
    {
        var hasError = false;
        foreach (var n in Chart.Notes)
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
