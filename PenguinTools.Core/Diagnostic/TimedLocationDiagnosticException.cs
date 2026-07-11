namespace PenguinTools.Core.Diagnostic;

public class TimedLocationDiagnosticException(
    MessageDescriptor message,
    int line,
    int tick,
    string? path = null,
    object? target = null) : DiagnosticException(message, target)
{
    public TimedLocationDiagnosticException(
        string messageKey,
        int line,
        int tick,
        string? path = null,
        object? target = null)
        : this(Msg.Key(messageKey), line, tick, path, target)
    {
    }

    public string? Path { get; } = path;
    public int Line { get; } = line;
    public int Tick { get; } = tick;

    public override Diagnostic ToDiagnostic()
    {
        return new TimedLocationDiagnostic(Severity.Error, Descriptor, Line, Tick, Path)
        {
            Target = Target,
            RelatedException = this,
            TimeCalculator = TimeCalculator
        };
    }
}