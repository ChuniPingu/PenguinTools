using System.Text;
using PenguinTools.Core.Diagnostic;

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

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("VERSION\t1.13.00\t1.13.00");
        sb.AppendLine("MUSIC\t0");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine("DIFFICULT\t0");
        sb.AppendLine("LEVEL\t0.0");
        sb.AppendLine($"CREATOR\t{Chart.Meta.Designer}");
        sb.AppendLine(
            $"BPM_DEF\t{Chart.Meta.MainBpm:F3}\t{Chart.Meta.MainBpm:F3}\t{Chart.Meta.MainBpm:F3}\t{Chart.Meta.MainBpm:F3}");
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
}