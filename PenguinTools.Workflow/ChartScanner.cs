using System.Collections.Concurrent;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.sus;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

using umgr = Chart.Models.umgr;

public static class ChartScanner
{
    public static async Task<OperationResult<IReadOnlyList<OptionBook>>> ScanDirectoryAsync(
        AssetManager assets,
        IMediaTool mediaTool,
        string directory,
        IReadOnlyList<ChartFileFormat>? discovery,
        int batchSize,
        string workingDirectory,
        IDiagnosticSink diagnostics,
        CancellationToken ct,
        IProgress<ProgressReport>? progress = null)
    {
        var processContext = new OptionExportProcessContext(diagnostics, ct, batchSize, workingDirectory, progress);
        var booksById = new ConcurrentDictionary<int, BookAccumulator>();

        var batch = DiagnosticSnapshot.Empty;
        var orderedFormats = ChartFileDiscoveryFormats.Normalize(discovery);

        for (var i = 0; i < orderedFormats.Count; i++)
        {
            var format = orderedFormats[i];
            batch = batch.Merge(
                await ScanGlobAsync(
                    directory,
                    ChartFileDiscoveryFormats.GetGlob(format),
                    booksById,
                    assets,
                    mediaTool,
                    processContext,
                    i > 0,
                    ct));
        }

        var snapshots = FinalizeBooks(booksById, diagnostics, ct);
        return OperationResult<IReadOnlyList<OptionBook>>.Success(snapshots)
            .WithDiagnostics(batch.Merge(DiagnosticSnapshot.Create(diagnostics)));
    }

    private static async Task<DiagnosticSnapshot> ScanGlobAsync(
        string directory,
        string fileGlob,
        ConcurrentDictionary<int, BookAccumulator> booksById,
        AssetManager assets,
        IMediaTool mediaTool,
        OptionExportProcessContext processContext,
        bool skipIfDifficultyFilled,
        CancellationToken ct)
    {
        var chartPaths = Directory.EnumerateFiles(directory, fileGlob, SearchOption.AllDirectories);
        return await OptionExportBatch.BatchAsync(
            chartPaths,
            (filePath, innerDiagnostics) => LoadChartAsync(filePath, assets, mediaTool, booksById, innerDiagnostics,
                skipIfDifficultyFilled, ct),
            filePath => filePath,
            processContext,
            true);
    }

    private static async Task LoadChartAsync(
        string filePath,
        AssetManager assets,
        IMediaTool mediaTool,
        ConcurrentDictionary<int, BookAccumulator> booksById,
        IDiagnosticSink diagnostics,
        bool skipIfDifficultyFilled,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ext = Path.GetExtension(filePath);
        umgr.Chart? chart = null;
        if (string.Equals(ext, ChartFileDiscoveryFormats.GetExtension(ChartFileFormat.Ugc),
                StringComparison.OrdinalIgnoreCase))
        {
            var r = await new UgcParser(new UgcParseRequest(filePath, assets), mediaTool).ParseAsync(ct);
            diagnostics.Report(r.Diagnostics);
            if (!r.Succeeded || r.Value is not { } ugcChart) return;
            chart = ugcChart;
        }
        else if (string.Equals(ext, ChartFileDiscoveryFormats.GetExtension(ChartFileFormat.Mgxc),
                     StringComparison.OrdinalIgnoreCase))
        {
            var r = await new MgxcParser(new MgxcParseRequest(filePath, assets), mediaTool).ParseAsync(ct);
            diagnostics.Report(r.Diagnostics);
            if (!r.Succeeded || r.Value is not { } mgxcChart) return;
            chart = mgxcChart;
        }
        else if (string.Equals(ext, ChartFileDiscoveryFormats.GetExtension(ChartFileFormat.Sus),
                     StringComparison.OrdinalIgnoreCase))
        {
            var r = await new SusParser(new SusParseRequest(filePath, assets), mediaTool).ParseAsync(ct);
            diagnostics.Report(r.Diagnostics);
            if (!r.Succeeded || r.Value is not { } susChart) return;
            chart = susChart;
        }
        else
        {
            return;
        }

        var meta = chart.Meta;
        var id = meta.Id ?? throw new DiagnosticException(MsgKeys.Error_File_ignored_due_to_id_missing);
        var item = new OptionDifficulty(chart);
        var book = booksById.GetOrAdd(id, _ => new BookAccumulator());

        lock (book.Gate)
        {
            if (skipIfDifficultyFilled && book.Items.ContainsKey(meta.Difficulty)) return;

            if (book.Items.ContainsKey(meta.Difficulty))
                diagnostics.Report(new PathDiagnostic(Severity.Warning,
                    Msg.Key(MsgKeys.Warn_Duplicate_id_and_difficulty),
                    filePath));

            book.Items[meta.Difficulty] = item;
        }
    }

    private static IReadOnlyList<OptionBook> FinalizeBooks(
        ConcurrentDictionary<int, BookAccumulator> booksById,
        IDiagnosticSink diagnostics,
        CancellationToken ct)
    {
        var list = new List<OptionBook>();

        // All scan batches have completed; accumulators are no longer being modified.
        foreach (var book in booksById.Values)
        {
            ct.ThrowIfCancellationRequested();
            var items = book.Items.Values.ToArray();

            if (items.Length == 0) continue;

            if (book.Items.ContainsKey(Difficulty.WorldsEnd) && items.Length != 1)
                diagnostics.Report(
                    new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_We_chart_must_be_unique_id))
                    {
                        Target = CreateDiagnosticTargets(items)
                    });

            var mainItems = items.Where(i => i.Meta.IsMain).ToArray();
            if (mainItems.Length > 1)
                diagnostics.Report(
                    new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_More_than_one_chart_marked_main))
                    {
                        Target = CreateDiagnosticTargets(mainItems)
                    });
            else if (mainItems.Length == 0 && items.Length > 1)
                diagnostics.Report(new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_No_chart_marked_main))
                {
                    Target = CreateDiagnosticTargets(items)
                });

            var mainItem = mainItems.FirstOrDefault() ?? items.OrderByDescending(i => i.Difficulty).First();

            var dict = book.Items.ToDictionary(kv => kv.Key, kv => kv.Value);

            list.Add(new OptionBook(
                mainItem.Difficulty,
                dict));
        }

        return list;
    }

    private static ChartDiagnosticTarget[] CreateDiagnosticTargets(IEnumerable<OptionDifficulty> items)
    {
        return items.Select(item => ChartDiagnosticTarget.FromMeta(item.Meta)).ToArray();
    }

    private sealed class BookAccumulator
    {
        public readonly object Gate = new();
        public readonly Dictionary<Difficulty, OptionDifficulty> Items = new();
    }
}
