using System.Collections.Concurrent;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Workflow;

public sealed record OptionExportProcessContext(
    IDiagnosticSink Diagnostics,
    CancellationToken CancellationToken,
    int BatchSize,
    string WorkingDirectory,
    IProgress<ProgressReport>? Progress = null);

public static class OptionExportBatch
{
    public static DiagnosticCollector CreateCollector(IDiagnosticSink? parent = null)
    {
        return new DiagnosticCollector
        {
            TimeCalculator = parent?.TimeCalculator
        };
    }

    public static async Task<DiagnosticSnapshot> ProcessItemsAsync<T>(
        MessageDescriptor phase,
        string unit,
        IEnumerable<T> items,
        Func<T, IDiagnosticSink, Task> action,
        Func<T, string> getItemPath,
        OptionExportProcessContext main,
        bool parallel = false,
        Func<T, string?>? getLabel = null)
    {
        var itemList = items as IList<T> ?? [.. items];
        var diagnostics = new ConcurrentBag<DiagnosticSnapshot>();
        var completed = 0;
        var total = itemList.Count;
        if (total > 0)
            main.Progress?.Report(new ProgressReport(phase, unit, Completed: 0, Total: total));

        if (parallel)
            await Parallel.ForEachAsync(itemList, new ParallelOptions
            {
                CancellationToken = main.CancellationToken,
                MaxDegreeOfParallelism = main.BatchSize
            }, ProcessItemAsync);
        else
            foreach (var item in itemList)
                await ProcessItemAsync(item, main.CancellationToken);

        return diagnostics.Aggregate(DiagnosticSnapshot.Empty, (current, snapshot) => current.Merge(snapshot));

        async ValueTask ProcessItemAsync(T item, CancellationToken ct)
        {
            var ld = CreateCollector(main.Diagnostics);
            try
            {
                await action(item, ld);
                ct.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                ld.Report(ex);
            }
            finally
            {
                diagnostics.Add(CreateItemDiagnostics(ld, getItemPath(item), main.WorkingDirectory));
                var done = Interlocked.Increment(ref completed);
                main.Progress?.Report(new ProgressReport(
                    phase,
                    unit,
                    Item: Path.GetFileName(getItemPath(item)),
                    Label: getLabel?.Invoke(item),
                    Completed: done,
                    Total: total));
            }
        }
    }

    public static DiagnosticSnapshot CreateItemDiagnostics(IDiagnosticSink sink, string path, string workingDirectory)
    {
        var relativePath = Path.GetRelativePath(workingDirectory, path);
        var copied = sink.Diagnostics.Select(diag =>
            string.IsNullOrWhiteSpace(diag.Path) ? diag.WithPathFallback(relativePath) : diag);
        return DiagnosticSnapshot.Create(copied);
    }

    public static Task<DiagnosticSnapshot> BatchAsync<T>(
        MessageDescriptor phase,
        string unit,
        IEnumerable<T> items,
        Func<T, IDiagnosticSink, Task> action,
        Func<T, string> getItemPath,
        OptionExportProcessContext context,
        bool parallel = false,
        Func<T, string?>? getLabel = null)
    {
        return ProcessItemsAsync(phase, unit, items, action, getItemPath, context, parallel, getLabel);
    }
}