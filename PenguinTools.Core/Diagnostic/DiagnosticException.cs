namespace PenguinTools.Core.Diagnostic;

public class DiagnosticException(MessageDescriptor message, object? target = null)
    : Exception(message.Key)
{
    public DiagnosticException(string messageKey, object? target = null)
        : this(Msg.Key(messageKey), target)
    {
    }

    public MessageDescriptor Descriptor { get; } = message;
    public object? Target { get; } = target;
    public ITickFormatter? TimeCalculator { get; init; }

    public virtual Diagnostic ToDiagnostic()
    {
        return new Diagnostic(Severity.Error, Descriptor)
        {
            Target = Target,
            RelatedException = this,
            TimeCalculator = TimeCalculator
        };
    }
}