using System.Collections.Concurrent;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

public static class OptionExporter
{
    public static async Task<OperationResult> ExportAsync(
        MusicExportContext ctx,
        OptionExportSettings settings,
        ExportOutputPaths outputPaths,
        IEnumerable<OptionBookSnapshot> books,
        string diagnosticsWorkingDirectory,
        CancellationToken ct,
        IProgress<ProgressReport>? progress = null)
    {
        var diagnostics = OptionExportBatch.CreateCollector();
        var processContext =
            new OptionExportProcessContext(diagnostics, ct, settings.BatchSize, diagnosticsWorkingDirectory, progress);
        var weEntries = new ConcurrentBag<Entry>();
        var ultEntries = new ConcurrentBag<Entry>();
        var releaseTag = new ReleaseTag(settings.ReleaseTagId, settings.ReleaseTagTitleName);

        var batchDiagnostics = await OptionExportBatch.BatchAsync(
            books,
            (book, innerDiagnostics) => ConvertBookAsync(ctx, book, settings, outputPaths, releaseTag,
                processContext.WorkingDirectory, innerDiagnostics, weEntries, ultEntries, ct),
            book => book.BookMeta.FilePath,
            processContext,
            true,
            book => book.Title);

        await GenerateAuxiliaryFilesAsync(settings, outputPaths, weEntries, ultEntries, releaseTag, ct);

        return OperationResult.Success()
            .WithDiagnostics(batchDiagnostics.Merge(DiagnosticSnapshot.Create(diagnostics)));
    }

    private static async Task ConvertBookAsync(
        MusicExportContext ctx,
        OptionBookSnapshot book,
        OptionExportSettings settings,
        ExportOutputPaths outputPaths,
        ReleaseTag releaseTag,
        string workingDirectory,
        IDiagnosticSink diagnostics,
        ConcurrentBag<Entry> weEntries,
        ConcurrentBag<Entry> ultEntries,
        CancellationToken ct)
    {
        var stage = await BuildStageAsync(ctx, book, settings, outputPaths, diagnostics, ct) ?? book.Stage;
        string? chartFolder = null;
        MusicXml? xml = null;

        if (settings.ConvertChart || settings.ConvertJacket)
            (xml, chartFolder) = await CreateMusicXmlAsync(book, stage, releaseTag, outputPaths.MusicFolder);

        if (settings.ConvertChart && xml is not null && chartFolder is not null)
            await ConvertChartsAsync(book, xml, chartFolder, workingDirectory, diagnostics, weEntries, ultEntries, ct);

        if (settings.ConvertJacket && xml is not null && chartFolder is not null)
            await ConvertJacketAsync(book, xml, chartFolder, settings, ctx, diagnostics, ct);

        if (settings.ConvertAudio)
            await ConvertAudioAsync(book, outputPaths.CueFileFolder, settings, ctx, diagnostics, ct);
    }

    private static async Task<Entry?> BuildStageAsync(
        MusicExportContext ctx,
        OptionBookSnapshot book,
        OptionExportSettings settings,
        ExportOutputPaths outputPaths,
        IDiagnosticSink diagnostics,
        CancellationToken ct)
    {
        if (!book.IsCustomStage || !settings.ConvertBackground) return null;
        if (string.IsNullOrWhiteSpace(book.BookMeta.FullBgiFilePath))
            throw new DiagnosticException(MsgKeys.Error_Background_file_is_not_set);
        if (book.StageId is null) throw new DiagnosticException(MsgKeys.Error_Stage_id_is_not_set);

        var stageXml = new StageXml(book.StageId.Value, book.NotesFieldLine);
        var cachedConversion = await OptionConversionCacheArtifacts.CreateStageAsync(
            book,
            outputPaths.StageFolder,
            ct);
        if (!settings.IgnoreCache &&
            await OptionConversionCacheValidator.IsHitAsync(
                settings.ConversionCache,
                cachedConversion.Key,
                cachedConversion.State,
                cachedConversion.Outputs,
                ct))
            return stageXml.Name;

        var stageConverter = new StageConverter(
            new StageBuildRequest(
                ctx.Assets,
                book.BookMeta.FullBgiFilePath,
                [],
                book.StageId,
                outputPaths.StageFolder,
                book.NotesFieldLine),
            ctx.MediaTool);
        var builtStage = await stageConverter.BuildAsync(ct);
        diagnostics.Report(builtStage.Diagnostics);
        if (!builtStage.Succeeded || builtStage.Value is not { } stageEntry) return null;

        await OptionConversionCacheValidator.StoreAsync(
            settings.ConversionCache,
            cachedConversion.Key,
            cachedConversion.State,
            cachedConversion.Outputs,
            ct);

        ct.ThrowIfCancellationRequested();
        return stageEntry;
    }

    private static async Task<(MusicXml Xml, string ChartFolder)> CreateMusicXmlAsync(
        OptionBookSnapshot book,
        Entry stage,
        ReleaseTag releaseTag,
        string musicFolder)
    {
        var metaMap = book.Difficulties.ToDictionary(kv => kv.Key, kv => kv.Value.Meta);
        var xml = new MusicXml(metaMap, book.BookMeta.Difficulty)
        {
            ReleaseTagName = releaseTag.Name,
            StageName = stage
        };

        var chartFolder = await xml.SaveDirectoryAsync(musicFolder);
        return (xml, chartFolder);
    }

    private static void TrackEventEntry(OptionBookSnapshot book, Difficulty difficulty, int songId,
        ConcurrentBag<Entry> weEntries, ConcurrentBag<Entry> ultEntries)
    {
        if (difficulty == Difficulty.WorldsEnd)
            weEntries.Add(new Entry(songId, book.Title));
        else if (difficulty == Difficulty.Ultima) ultEntries.Add(new Entry(songId, book.Title));
    }

    private static async Task ConvertChartsAsync(
        OptionBookSnapshot book,
        MusicXml xml,
        string chartFolder,
        string workingDirectory,
        IDiagnosticSink diagnostics,
        ConcurrentBag<Entry> weEntries,
        ConcurrentBag<Entry> ultEntries,
        CancellationToken ct)
    {
        foreach (var (difficulty, item) in book.Difficulties)
        {
            if (item.SongId is not { } songId) throw new DiagnosticException(MsgKeys.Error_Song_id_is_not_set);

            TrackEventEntry(book, difficulty, songId, weEntries, ultEntries);

            var chartPath = Path.Combine(chartFolder, xml[difficulty].File);
            var chartDiagnostics = OptionExportBatch.CreateCollector();

            void FlushChartDiagnostics()
            {
                diagnostics.Report(
                    OptionExportBatch.CreateItemDiagnostics(chartDiagnostics, item.Meta.FilePath, workingDirectory));
            }

            var convertedChart = new C2SChartConverter(new C2SConvertRequest(item.Chart)).Convert();
            chartDiagnostics.Report(convertedChart.Diagnostics);
            if (!convertedChart.Succeeded || convertedChart.Value is null)
            {
                FlushChartDiagnostics();
                return;
            }

            var chartWriter = new C2SChartWriter(new C2SWriteRequest(chartPath, convertedChart.Value));
            var writtenChart = await chartWriter.WriteAsync(ct);
            chartDiagnostics.Report(writtenChart.Diagnostics);
            FlushChartDiagnostics();
            if (!writtenChart.Succeeded) return;

            ct.ThrowIfCancellationRequested();
        }
    }

    private static async Task ConvertJacketAsync(
        OptionBookSnapshot book,
        MusicXml xml,
        string chartFolder,
        OptionExportSettings settings,
        MusicExportContext ctx,
        IDiagnosticSink diagnostics,
        CancellationToken ct)
    {
        var jacketPath = book.BookMeta.FullJacketFilePath;
        if (!File.Exists(jacketPath))
        {
            diagnostics.Report(new PathDiagnostic(Severity.Warning, Msg.Key(MsgKeys.Error_Jacket_file_not_found),
                jacketPath));
            return;
        }

        var outputPath = Path.Combine(chartFolder, xml.JaketFile);
        var cachedConversion = await OptionConversionCacheArtifacts.CreateJacketAsync(
            xml.DataName,
            jacketPath,
            outputPath,
            ct);
        if (!settings.IgnoreCache &&
            await OptionConversionCacheValidator.IsHitAsync(
                settings.ConversionCache,
                cachedConversion.Key,
                cachedConversion.State,
                cachedConversion.Outputs,
                ct))
            return;

        var jacketConverter = new JacketConverter(
            new JacketConvertRequest(jacketPath, outputPath),
            ctx.MediaTool);
        var convertedJacket = await jacketConverter.ConvertAsync(ct);
        diagnostics.Report(convertedJacket.Diagnostics);
        if (!convertedJacket.Succeeded) return;

        await OptionConversionCacheValidator.StoreAsync(
            settings.ConversionCache,
            cachedConversion.Key,
            cachedConversion.State,
            cachedConversion.Outputs,
            ct);

        ct.ThrowIfCancellationRequested();
    }

    private static async Task ConvertAudioAsync(
        OptionBookSnapshot book,
        string cueFileFolder,
        OptionExportSettings settings,
        MusicExportContext ctx,
        IDiagnosticSink diagnostics,
        CancellationToken ct)
    {
        var songId = book.BookMeta.Id;
        var cachedConversion = songId is null
            ? null
            : await OptionConversionCacheArtifacts.CreateAudioAsync(
                book.BookMeta,
                cueFileFolder,
                settings.HcaEncryptionKey,
                ct);
        if (songId is not null &&
            cachedConversion is not null &&
            !settings.IgnoreCache &&
            await OptionConversionCacheValidator.IsHitAsync(
                settings.ConversionCache,
                cachedConversion.Key,
                cachedConversion.State,
                cachedConversion.Outputs,
                ct))
            return;

        var audioConverter = new AudioConverter(
            new AudioConvertRequest(
                book.BookMeta,
                cueFileFolder,
                ctx.AssetStore.GetTempPath(
                    $"c_{Path.GetFileNameWithoutExtension(book.BookMeta.FullBgmFilePath)}.wav"),
                settings.HcaEncryptionKey),
            ctx.MediaTool);
        var convertedAudio = await audioConverter.ConvertAsync(ct);
        diagnostics.Report(convertedAudio.Diagnostics);
        if (!convertedAudio.Succeeded) return;

        if (cachedConversion is not null)
            await OptionConversionCacheValidator.StoreAsync(
                settings.ConversionCache,
                cachedConversion.Key,
                cachedConversion.State,
                cachedConversion.Outputs,
                ct);

        ct.ThrowIfCancellationRequested();
    }

    private static async Task GenerateAuxiliaryFilesAsync(
        OptionExportSettings settings,
        ExportOutputPaths outputPaths,
        ConcurrentBag<Entry> weEntries,
        ConcurrentBag<Entry> ultEntries,
        ReleaseTag releaseTag,
        CancellationToken ct)
    {
        if (settings.GenerateReleaseTagXml) await releaseTag.SaveDirectoryAsync(outputPaths.ReleaseTagPath);

        if (settings.GenerateEventXml && !ultEntries.IsEmpty)
        {
            var eventXml = new EventXml(settings.UltimaEventId, EventXml.MusicType.Ultima, ultEntries.ToHashSet());
            await eventXml.SaveDirectoryAsync(outputPaths.EventFolder);
            ct.ThrowIfCancellationRequested();
        }

        if (settings.GenerateEventXml && !weEntries.IsEmpty)
        {
            var eventXml = new EventXml(settings.WeEventId, EventXml.MusicType.WldEnd, weEntries.ToHashSet());
            await eventXml.SaveDirectoryAsync(outputPaths.EventFolder);
            ct.ThrowIfCancellationRequested();
        }
    }
}