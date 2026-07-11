namespace PenguinTools.Media;

public sealed record StageBuildRequest(
    AssetManager Assets,
    string BackgroundPath,
    string?[]? EffectPaths,
    int? StageId,
    string OutFolder,
    Entry NoteFieldLane);