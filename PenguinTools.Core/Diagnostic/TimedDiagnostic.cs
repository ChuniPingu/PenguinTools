namespace PenguinTools.Core.Diagnostic;

public sealed record TimedDiagnostic(Severity Severity, MessageDescriptor Message, int Tick)
    : Diagnostic(Severity, Message)
{
    public override int? Time => Tick;

    public override Diagnostic WithPathFallback(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new TimedPathDiagnostic(Severity, Message, path, Tick)
        {
            Target = Target,
            RelatedException = RelatedException,
            TimeCalculator = TimeCalculator
        };
    }
}