namespace PenguinTools.Core.Diagnostic;

public class LocationDiagnosticException(
    MessageDescriptor message,
    int line,
    string? path = null,
    object? target = null) : DiagnosticException(message, target)
{
    public LocationDiagnosticException(string messageKey, int line, string? path = null, object? target = null)
        : this(Msg.Key(messageKey), line, path, target)
    {
    }

    public string? Path { get; } = path;
    public int Line { get; } = line;

    public override Diagnostic ToDiagnostic()
    {
        return new LocationDiagnostic(Severity.Error, Descriptor, Line, Path)
        {
            Target = Target,
            RelatedException = this,
            TimeCalculator = TimeCalculator
        };
    }
}