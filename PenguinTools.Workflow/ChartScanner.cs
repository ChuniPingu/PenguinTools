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
    public static async Task<OperationResult<IReadOnlyList<OptionBookSnapshot>>> ScanDirectoryAsync(
        AssetManager assets,
        IMediaTool mediaTool,
        string directory,
        IReadOnlyList<ChartFileFormat>? discovery,
        int batchSize,
        string workingDirectory,
        IDiagnosticSink diagnostics,
        CancellationToken ct,
        ChartScannerMessages? messages = null,
        IProgress<ProgressReport>? progress = null)
    {
        messages ??= ChartScannerMessages.Default;
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
                    messages,
                    i > 0,
                    ct));
        }

        var snapshots = FinalizeAndBuildSnapshots(booksById, diagnostics, messages, ct);
        return OperationResult<IReadOnlyList<OptionBookSnapshot>>.Success(snapshots)
            .WithDiagnostics(batch.Merge(DiagnosticSnapshot.Create(diagnostics)));
    }

    private static async Task<DiagnosticSnapshot> ScanGlobAsync(
        string directory,
        string fileGlob,
        ConcurrentDictionary<int, BookAccumulator> booksById,
        AssetManager assets,
        IMediaTool mediaTool,
        OptionExportProcessContext processContext,
        ChartScannerMessages messages,
        bool skipIfDifficultyFilled,
        CancellationToken ct)
    {
        var chartPaths = Directory.EnumerateFiles(directory, fileGlob, SearchOption.AllDirectories);
        return await OptionExportBatch.BatchAsync(
            chartPaths,
            (filePath, innerDiagnostics) => LoadChartAsync(filePath, assets, mediaTool, booksById, innerDiagnostics,
                messages, skipIfDifficultyFilled, ct),
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
        ChartScannerMessages messages,
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
        var id = meta.Id ?? throw new DiagnosticException(messages.MissingSongId);
        var item = new OptionDifficultySnapshot(meta.Difficulty, meta.Id, chart, meta);
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

    private static IReadOnlyList<OptionBookSnapshot> FinalizeAndBuildSnapshots(
        ConcurrentDictionary<int, BookAccumulator> booksById,
        IDiagnosticSink diagnostics,
        ChartScannerMessages messages,
        CancellationToken ct)
    {
        var list = new List<OptionBookSnapshot>();

        foreach (var (id, book) in booksById.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            OptionDifficultySnapshot[] items;
            lock (book.Gate)
            {
                items = book.Items.Values.ToArray();
            }

            if (items.Length == 0)
            {
                booksById.TryRemove(id, out _);
                continue;
            }

            lock (book.Gate)
            {
                if (book.Items.ContainsKey(Difficulty.WorldsEnd) && book.Items.Count != 1)
                    diagnostics.Report(
                        new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_We_chart_must_be_unique_id))
                        {
                            Target = CreateDiagnosticTargets(items)
                        });
            }

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

            var mainItem = mainItems.FirstOrDefault() ?? items.OrderByDescending(i => i.Difficulty).FirstOrDefault();
            if (mainItem is null)
            {
                booksById.TryRemove(id, out _);
                continue;
            }

            IReadOnlyDictionary<Difficulty, OptionDifficultySnapshot> dict;
            lock (book.Gate)
            {
                dict = book.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            list.Add(new OptionBookSnapshot(
                mainItem.Meta,
                mainItem.Meta.IsCustomStage,
                mainItem.Meta.StageId,
                mainItem.Meta.NotesFieldLine,
                mainItem.Meta.Stage,
                mainItem.Meta.Title,
                dict));
        }

        return list;
    }

    private static ChartDiagnosticTarget[] CreateDiagnosticTargets(IEnumerable<OptionDifficultySnapshot> items)
    {
        return items.Select(item => ChartDiagnosticTarget.FromMeta(item.Meta)).ToArray();
    }

    private sealed class BookAccumulator
    {
        public readonly object Gate = new();
        public readonly Dictionary<Difficulty, OptionDifficultySnapshot> Items = new();
    }
}

public sealed record ChartScannerMessages(
    string MissingSongId,
    string DuplicateSongIdAndDifficulty,
    string WorldsEndMustBeUnique,
    string MoreThanOneMainChart,
    string NoMainChart)
{
    public static ChartScannerMessages Default { get; } = new(
        MsgKeys.Error_File_ignored_due_to_id_missing,
        MsgKeys.Warn_Duplicate_id_and_difficulty,
        MsgKeys.Warn_We_chart_must_be_unique_id,
        MsgKeys.Warn_More_than_one_chart_marked_main,
        MsgKeys.Warn_No_chart_marked_main);
}