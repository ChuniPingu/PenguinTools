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
            SanitizeMessage(diagnostic.Message),
            diagnostic.Path,
            diagnostic.Line,
            diagnostic.Time,
            DiagnosticTime.TryGetPosition(diagnostic));
    }

    internal static MessageDescriptor SanitizeMessage(MessageDescriptor message)
    {
        if (message.Args is not { Count: > 0 } args) return message;

        var sanitized = new Dictionary<string, object?>(args.Count, StringComparer.Ordinal);
        foreach (var (key, value) in args)
            sanitized[key] = SanitizeArg(value);

        return message with { Args = sanitized };
    }

    private static object? SanitizeArg(object? value)
    {
        return value switch
        {
            null => null,
            string or bool => value,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            _ => value.ToString()
        };
    }
}