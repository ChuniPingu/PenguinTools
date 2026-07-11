using PenguinTools.Core.Diagnostic;

namespace PenguinTools.CLI;

internal static class CliDiagnostics
{
    internal static CliDiagnosticPayload[] ToPayload(DiagnosticSnapshot snapshot)
    {
        return [.. GetOrderedDiagnostics(snapshot).Select(ToPayload)];
    }

    internal static CliDiagnosticPayload[] ToPayload(IEnumerable<Diagnostic> diagnostics)
    {
        return
        [
            .. diagnostics
                .OrderByDescending(d => d.Severity)
                .ThenBy(d => d.Path, StringComparer.Ordinal)
                .ThenBy(d => d.Line)
                .ThenBy(d => d.Time)
                .ThenBy(d => d.Message.Key, StringComparer.Ordinal)
                .Select(ToPayload)
        ];
    }

    internal static DiagnosticSnapshot SnapshotFromMessage(MessageDescriptor message)
    {
        var sink = new DiagnosticCollector();
        sink.Report(new Diagnostic(Severity.Error, message));
        return DiagnosticSnapshot.Create(sink);
    }

    private static IEnumerable<Diagnostic> GetOrderedDiagnostics(DiagnosticSnapshot snapshot)
    {
        return snapshot.Diagnostics.OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Line)
            .ThenBy(d => d.Time)
            .ThenBy(d => d.Message.Key, StringComparer.Ordinal);
    }

    private static CliDiagnosticPayload ToPayload(Diagnostic diagnostic)
    {
        return new CliDiagnosticPayload(
            SeverityCodes.ToCode(diagnostic.Severity),
            diagnostic.Message,
            diagnostic.Path,
            diagnostic.Line,
            diagnostic.Time);
    }
}