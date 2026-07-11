namespace PenguinTools.Core.Diagnostic;

public sealed record LocationDiagnostic(
    Severity Severity,
    MessageDescriptor Message,
    int LineValue,
    string? PathValue = null) : Diagnostic(Severity, Message)
{
    public override string? Path => PathValue;
    public override int? Line => LineValue;

    public override string? FormattedLocation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PathValue)) return $"0x{LineValue:X2}";

            return string.Equals(System.IO.Path.GetExtension(PathValue), ".mgxc", StringComparison.OrdinalIgnoreCase)
                ? $"{PathValue}(0x{LineValue:X2})"
                : $"{PathValue}({LineValue})";
        }
    }

    public override Diagnostic WithPathFallback(string path)
    {
        if (!string.IsNullOrWhiteSpace(PathValue)) return this;

        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new LocationDiagnostic(Severity, Message, LineValue, path)
        {
            Target = Target,
            RelatedException = RelatedException,
            TimeCalculator = TimeCalculator
        };
    }
}