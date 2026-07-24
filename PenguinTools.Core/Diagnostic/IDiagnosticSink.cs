namespace PenguinTools.Core.Diagnostic;

public interface IDiagnosticSink
{
    IReadOnlyCollection<Diagnostic> Diagnostics { get; }
    bool HasProblem { get; }
    bool HasError { get; }
    ITickFormatter? TimeCalculator { get; set; }

    void Report(Diagnostic item);
    void Report(Exception ex);

    /// <summary>
    /// Applies <see cref="TimeCalculator"/> to diagnostics reported before it was set.
    /// </summary>
    void BackfillTimeCalculator();
}