using System.Collections.Concurrent;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.sus;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Workflow;

public static class ChartScanner
{
    public static async Task<OperationResult<IReadOnlyList<OptionBook>>> ScanDirectoryAsync(
        AssetManager assets,
        IMediaValidator mediaValidator,
        string directory,
        IReadOnlyList<ChartFileFormat>? discovery,
        OptionExportProcessContext processContext)
    {
        var diagnostics = processContext.Diagnostics;
        var ct = processContext.CancellationToken;
        var scanMediaValidator = new ScanMediaValidator(mediaValidator, ct);
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
                    scanMediaValidator,
                    processContext,
                    i > 0));
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
        IMediaValidator mediaValidator,
        OptionExportProcessContext processContext,
        bool skipIfDifficultyFilled)
    {
        var ct = processContext.CancellationToken;
        var chartPaths = Directory.EnumerateFiles(directory, fileGlob, SearchOption.AllDirectories);
        return await OptionExportBatch.BatchAsync(
            chartPaths,
            (filePath, innerDiagnostics) => LoadChartAsync(filePath, assets, mediaValidator, booksById, innerDiagnostics,
                skipIfDifficultyFilled, ct),
            filePath => filePath,
            processContext,
            true);
    }

    private static async Task LoadChartAsync(
        string filePath,
        AssetManager assets,
        IMediaValidator mediaValidator,
        ConcurrentDictionary<int, BookAccumulator> booksById,
        IDiagnosticSink diagnostics,
        bool skipIfDifficultyFilled,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ext = Path.GetExtension(filePath);
        UmgrModel.Chart? chart = null;
        if (string.Equals(ext, ChartFileDiscoveryFormats.GetExtension(ChartFileFormat.Ugc),
                StringComparison.OrdinalIgnoreCase))
        {
            var r = await new UgcParser(new UgcParseRequest(filePath, assets), mediaValidator).ParseAsync(ct);
            ReportParseDiagnostics(r.Diagnostics, diagnostics);
            if (!r.Succeeded)
            {
                return;
            }

            chart = r.Value;
        }
        else if (string.Equals(ext, ChartFileDiscoveryFormats.GetExtension(ChartFileFormat.Mgxc),
                     StringComparison.OrdinalIgnoreCase))
        {
            var r = await new MgxcParser(new MgxcParseRequest(filePath, assets), mediaValidator).ParseAsync(ct);
            ReportParseDiagnostics(r.Diagnostics, diagnostics);
            if (!r.Succeeded)
            {
                return;
            }

            chart = r.Value;
        }
        else if (string.Equals(ext, ChartFileDiscoveryFormats.GetExtension(ChartFileFormat.Sus),
                     StringComparison.OrdinalIgnoreCase))
        {
            var r = await new SusParser(new SusParseRequest(filePath, assets), mediaValidator).ParseAsync(ct);
            diagnostics.Report(r.Diagnostics);
            if (!r.Succeeded)
            {
                return;
            }

            chart = r.Value;
        }
        else
        {
            return;
        }

        var meta = chart.Meta;
        var id = meta.Id ?? throw new DiagnosticException(MsgKeys.Error_File_ignored_due_to_id_missing);
        ResourceIds.ValidateSong(id);
        var item = new OptionDifficulty(chart);
        var book = booksById.GetOrAdd(id, _ => new BookAccumulator());

        lock (book.Gate)
        {
            if (skipIfDifficultyFilled && book.Items.ContainsKey(meta.Difficulty))
            {
                return;
            }

            if (book.Items.ContainsKey(meta.Difficulty))
            {
                diagnostics.Report(new PathDiagnostic(Severity.Warning,
                    Msg.Key(MsgKeys.Warn_Duplicate_id_and_difficulty),
                    filePath));
            }

            book.Items[meta.Difficulty] = item;
        }
    }

    private static void ReportParseDiagnostics(DiagnosticSnapshot result, IDiagnosticSink sink)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            sink.Report(diagnostic.Message.Key == MsgKeys.Error_Chart_version_too_new
                ? diagnostic with
                {
                    Severity = Severity.Warning,
                    Message = diagnostic.Message with { Key = MsgKeys.Warn_Chart_skipped_version }
                }
                : diagnostic);
        }
    }

    private static List<OptionBook> FinalizeBooks(
        ConcurrentDictionary<int, BookAccumulator> booksById,
        IDiagnosticSink diagnostics,
        CancellationToken ct)
    {
        var list = new List<OptionBook>();

        // All scan batches have completed; accumulators are no longer being modified.
        foreach (var bookItems in booksById.Values.Select(book => book.Items))
        {
            ct.ThrowIfCancellationRequested();
            var items = bookItems.Values.ToArray();

            if (items.Length == 0)
            {
                continue;
            }

            if (bookItems.ContainsKey(Difficulty.WorldsEnd) && items.Length != 1)
            {
                diagnostics.Report(
                    new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_We_chart_must_be_unique_id))
                    {
                        Target = CreateDiagnosticTargets(items)
                    });
            }

            var mainItems = items.Where(i => i.Meta.IsMain).ToArray();
            if (mainItems.Length > 1)
            {
                diagnostics.Report(
                    new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_More_than_one_chart_marked_main))
                    {
                        Target = CreateDiagnosticTargets(mainItems)
                    });
            }
            else if (mainItems.Length == 0 && items.Length > 1)
            {
                diagnostics.Report(new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_No_chart_marked_main))
                {
                    Target = CreateDiagnosticTargets(items)
                });
            }

            var mainItem = mainItems.FirstOrDefault() ?? items.OrderByDescending(i => i.Difficulty).First();

            var dict = bookItems.ToDictionary(kv => kv.Key, kv => kv.Value);

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
        public readonly Dictionary<Difficulty, OptionDifficulty> Items = [];
    }
}
