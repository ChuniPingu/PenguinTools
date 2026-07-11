namespace PenguinTools.Core.Diagnostic;

public sealed record PathDiagnostic(Severity Severity, MessageDescriptor Message, string PathValue)
    : Diagnostic(Severity, Message)
{
    public override string? Path => PathValue;
    public override string? FormattedLocation => PathValue;

    public override Diagnostic WithPathFallback(string path)
    {
        return this;
    }
}