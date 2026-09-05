namespace PenguinTools.Core.Diagnostic;

public sealed record TimedLocationDiagnostic(
    Severity Severity,
    MessageDescriptor Message,
    int LineValue,
    int Tick,
    string? PathValue = null) : LocationDiagnostic(Severity, Message, LineValue, PathValue)
{
    public override int? Time => Tick;
}
