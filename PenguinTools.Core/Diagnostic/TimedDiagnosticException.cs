namespace PenguinTools.Core.Diagnostic;

public class TimedDiagnosticException(MessageDescriptor message, int tick, object? target = null)
    : DiagnosticException(message, target)
{
    public TimedDiagnosticException(string messageKey, int tick, object? target = null)
        : this(Msg.Key(messageKey), tick, target)
    {
    }

    public int Tick { get; } = tick;

    public override Diagnostic ToDiagnostic()
    {
        return new TimedDiagnostic(Severity.Error, Descriptor, Tick)
        {
            Target = Target,
            RelatedException = this,
            TimeCalculator = TimeCalculator
        };
    }
}