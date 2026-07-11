namespace PenguinTools.Core.Diagnostic;

public record Diagnostic(Severity Severity, MessageDescriptor Message) : IComparable<Diagnostic>, IComparable
{
    public object? Target { get; init; }
    public Exception? RelatedException { get; init; }
    public ITickFormatter? TimeCalculator { get; init; }

    public virtual string? Path => null;
    public virtual int? Line => null;
    public virtual int? Time => null;
    public virtual string? FormattedLocation => null;

    public int CompareTo(object? obj)
    {
        if (obj is Diagnostic other) return CompareTo(other);
        return obj is null ? 1 : 0;
    }

    public int CompareTo(Diagnostic? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (other is null) return 1;

        var severityComparison = Severity.CompareTo(other.Severity);
        if (severityComparison != 0) return severityComparison;

        var pathComparison = string.Compare(Path, other.Path, StringComparison.Ordinal);
        if (pathComparison != 0) return pathComparison;

        var lineComparison = Nullable.Compare(Line, other.Line);
        if (lineComparison != 0) return lineComparison;

        var timeComparison = Nullable.Compare(Time, other.Time);
        if (timeComparison != 0) return timeComparison;

        var keyComparison = string.Compare(Message.Key, other.Message.Key, StringComparison.Ordinal);
        if (keyComparison != 0) return keyComparison;

        return CompareArgs(Message.Args, other.Message.Args);
    }

    public virtual Diagnostic WithPathFallback(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new PathDiagnostic(Severity, Message, path)
        {
            Target = Target,
            RelatedException = RelatedException,
            TimeCalculator = TimeCalculator
        };
    }

    public Diagnostic WithTimeCalculator(ITickFormatter? timeCalculator)
    {
        if (TimeCalculator is not null || timeCalculator is null) return this;

        return this with { TimeCalculator = timeCalculator };
    }

    public virtual Diagnostic Copy()
    {
        return this with { };
    }

    private static int CompareArgs(IReadOnlyDictionary<string, object?>? left,
        IReadOnlyDictionary<string, object?>? right)
    {
        left ??= new Dictionary<string, object?>();
        right ??= new Dictionary<string, object?>();

        var countComparison = left.Count.CompareTo(right.Count);
        if (countComparison != 0) return countComparison;

        foreach (var key in left.Keys.OrderBy(static key => key, StringComparer.Ordinal))
        {
            if (!right.TryGetValue(key, out var rightValue))
                return 1;

            var valueComparison = string.Compare(
                left[key]?.ToString(),
                rightValue?.ToString(),
                StringComparison.Ordinal);
            if (valueComparison != 0) return valueComparison;
        }

        return 0;
    }
}