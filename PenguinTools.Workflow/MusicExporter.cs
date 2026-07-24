using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using PenguinTools.Media;
using umgr = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Workflow;

public static class MusicExporter
{
    public static Entry CreateNoteFieldEntry(Entry current, int? id, string? name, string? data)
    {
        return MusicPaths.CreateEntry(current, id, name, data);
    }

    public static bool ShouldBuildStage(Meta meta, StageRequestOverrides overrides)
    {
        return meta.IsCustomStage || overrides.HasBuildInputs;
    }

    public static async Task<OperationResult<Entry>> BuildStageAsync(
        MusicExportContext ctx,
        Meta meta,
        string output,
        StageRequestOverrides overrides,
        CancellationToken cancellationToken)
    {
        var backgroundPath = overrides.BackgroundPath ?? meta.FullBgiFilePath;
        if (string.IsNullOrWhiteSpace(backgroundPath))
            return MusicPaths.CreateFailureResultOf<Entry>(Msg.Key(MsgKeys.Error_Stage_background_required));

        var noteFieldLane = MusicPaths.CreateEntry(
            meta.NotesFieldLine,
            overrides.NoteFieldLaneId,
            overrides.NoteFieldLaneName,
            overrides.NoteFieldLaneData);
        var request = new StageBuildRequest(
            ctx.Assets,
            backgroundPath,
            overrides.EffectPaths,
            overrides.StageId ?? meta.StageId,
            output,
            noteFieldLane);

        return await new StageConverter(request, ctx.MediaTool).BuildAsync(cancellationToken);
    }

    public static async Task<OperationResult> ConvertAudioAsync(
        MusicExportContext ctx,
        Meta meta,
        string output,
        AudioRequestOverrides overrides,
        CancellationToken cancellationToken)
    {
        var request = new AudioConvertRequest(
            meta,
            output,
            overrides.WorkingAudioPath ??
            ctx.AssetStore.GetTempPath($"c_{Path.GetFileNameWithoutExtension(meta.FullBgmFilePath)}.wav"),
            overrides.HcaEncryptionKey ?? AudioConvertRequest.DefaultHcaEncryptionKey);

        return await new AudioConverter(request, ctx.MediaTool).ConvertAsync(cancellationToken);
    }

    public static async Task<OperationResult> ExportAsync(
        MusicExportContext ctx,
        umgr.Chart chart,
        string output,
        string? jacketInput,
        AudioRequestOverrides audioOverrides,
        StageRequestOverrides stageOverrides,
        CancellationToken cancellationToken,
        IProgress<ProgressReport>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = DiagnosticSnapshot.Empty;
        var meta = chart.Meta;
        var stage = meta.Stage;
        var item = Path.GetFileName(meta.FilePath);
        var label = meta.Title;

        progress?.Report(new ProgressReport(item, label));

        if (ShouldBuildStage(meta, stageOverrides))
        {
            var builtStage = await BuildStageAsync(ctx, meta, output, stageOverrides, cancellationToken);
            diagnostics = diagnostics.Merge(builtStage.Diagnostics);
            if (!builtStage.Succeeded || builtStage.Value is null)
                return OperationResult.Failure().WithDiagnostics(diagnostics);

            stage = builtStage.Value;
        }

        if (meta is { Difficulty: Difficulty.WorldsEnd or Difficulty.Ultima, UnlockEventId: { } eventId })
        {
            var songId = meta.Id ?? 0;
            var type = meta.Difficulty == Difficulty.WorldsEnd ? EventXml.MusicType.WldEnd : EventXml.MusicType.Ultima;
            var eventXml = new EventXml(eventId, type, [new Entry(songId, meta.Title)]);
            await eventXml.SaveDirectoryAsync(output);
        }

        var metaMap = new Dictionary<Difficulty, Meta>
        {
            [meta.Difficulty] = meta
        };

        var musicXml = new MusicXml(metaMap, meta.Difficulty)
        {
            StageName = stage
        };

        var chartBundleFolder = await musicXml.SaveDirectoryAsync(output);
        var chartPath = Path.Combine(chartBundleFolder, musicXml[meta.Difficulty].File);
        MusicPaths.EnsureParentDirectory(chartPath);

        var convertedChart = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        diagnostics = diagnostics.Merge(convertedChart.Diagnostics);
        if (!convertedChart.Succeeded || convertedChart.Value is null)
            return OperationResult.Failure().WithDiagnostics(diagnostics);

        var writtenChart =
            await new C2SChartWriter(new C2SWriteRequest(chartPath, convertedChart.Value, chart.GetCalculator()))
                .WriteAsync(cancellationToken);
        diagnostics = diagnostics.Merge(writtenChart.Diagnostics);
        if (!writtenChart.Succeeded) return OperationResult.Failure().WithDiagnostics(diagnostics);

        cancellationToken.ThrowIfCancellationRequested();

        var jacketPath = Path.Combine(chartBundleFolder, musicXml.JaketFile);
        var convertedJacket = await new JacketConverter(
            new JacketConvertRequest(jacketInput ?? meta.FullJacketFilePath, jacketPath),
            ctx.MediaTool).ConvertAsync(cancellationToken);
        diagnostics = diagnostics.Merge(convertedJacket.Diagnostics);
        if (!convertedJacket.Succeeded) return OperationResult.Failure().WithDiagnostics(diagnostics);

        cancellationToken.ThrowIfCancellationRequested();

        var convertedAudio = await ConvertAudioAsync(ctx, meta, output, audioOverrides, cancellationToken);
        diagnostics = diagnostics.Merge(convertedAudio.Diagnostics);

        return (convertedAudio.Succeeded ? OperationResult.Success() : OperationResult.Failure())
            .WithDiagnostics(diagnostics);
    }
}