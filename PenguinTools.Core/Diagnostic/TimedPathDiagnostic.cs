namespace PenguinTools.Core.Diagnostic;

public sealed record TimedPathDiagnostic(
    Severity Severity,
    MessageDescriptor Message,
    string PathValue,
    int Tick) : PathDiagnostic(Severity, Message, PathValue)
{
    public override int? Time => Tick;
}
