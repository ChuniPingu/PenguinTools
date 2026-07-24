using System.Collections.Concurrent;

namespace PenguinTools.Core.Diagnostic;

public class DiagnosticCollector : IDiagnosticSink
{
    private readonly ConcurrentBag<Diagnostic> _diagnostics = [];

    public IReadOnlyCollection<Diagnostic> Diagnostics => _diagnostics;
    public bool HasProblem => !_diagnostics.IsEmpty;
    public bool HasError => _diagnostics.Any(d => d.Severity == Severity.Error);
    public ITickFormatter? TimeCalculator { get; set; }

    public void Report(Diagnostic item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _diagnostics.Add(item.WithTimeCalculator(TimeCalculator));
    }

    /// <summary>
    /// Applies <see cref="TimeCalculator"/> to diagnostics that were reported before it was available.
    /// </summary>
    public void BackfillTimeCalculator()
    {
        if (TimeCalculator is null) return;

        var existing = _diagnostics.ToArray();
        _diagnostics.Clear();
        foreach (var diagnostic in existing)
            _diagnostics.Add(diagnostic.WithTimeCalculator(TimeCalculator));
    }

    public void Report(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is DiagnosticException diagnosticException)
        {
            Report(diagnosticException.ToDiagnostic());
            return;
        }

        Report(new Diagnostic(Severity.Error, Msg.Unhandled(ex.Message))
        {
            RelatedException = ex
        });
    }
}