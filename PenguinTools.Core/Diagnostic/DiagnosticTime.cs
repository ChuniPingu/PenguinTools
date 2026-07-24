namespace PenguinTools.Core.Diagnostic;

public static class DiagnosticTime
{
    public static TickPosition? TryGetPosition(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (diagnostic.Time is not { } tick) return null;
        if (diagnostic.TimeCalculator is null) return null;

        return diagnostic.TimeCalculator.GetPosition(tick);
    }
}
