namespace PenguinTools.Core.Diagnostic;

public sealed record TimedPathDiagnostic(
    Severity Severity,
    MessageDescriptor Message,
    string PathValue,
    int Tick) : Diagnostic(Severity, Message)
{
    public override string? Path => PathValue;
    public override int? Time => Tick;
    public override string? FormattedLocation => PathValue;

    public override Diagnostic WithPathFallback(string path)
    {
        return this;
    }
}