using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

public static class MusicMediaExporter
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
        ResourceIds.ValidateSong(meta.Id);
        var backgroundPath = overrides.BackgroundPath ?? meta.FullBgiFilePath;
        if (string.IsNullOrWhiteSpace(backgroundPath))
        {
            return MusicPaths.CreateFailureResultOf<Entry>(Msg.Key(MsgKeys.Error_Stage_background_required));
        }

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
            noteFieldLane,
            meta.BackgroundOffset);

        return await new StageConverter(request, ctx.Images).BuildAsync(cancellationToken);
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

        return await new AudioConverter(request, ctx.Audio).ConvertAsync(cancellationToken);
    }
}
