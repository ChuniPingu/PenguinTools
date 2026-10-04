namespace PenguinTools.Core.Diagnostic;

public sealed class DiagnosticSnapshot
{
    private DiagnosticSnapshot(IReadOnlyList<Diagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
    }

    public static DiagnosticSnapshot Empty { get; } = new([]);

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool HasProblem => Diagnostics.Count > 0;
    public bool HasError => Diagnostics.Any(d => d.Severity == Severity.Error);

    public DiagnosticSnapshot Merge(DiagnosticSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!HasProblem)
        {
            return other;
        }

        if (!other.HasProblem)
        {
            return this;
        }

        return Create(Diagnostics.Concat(other.Diagnostics));
    }

    public static DiagnosticSnapshot Create(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return new DiagnosticSnapshot([.. diagnostics.Select(d => d.Copy())]);
    }

    public static DiagnosticSnapshot Create(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        return Create(sink.Diagnostics);
    }
}
