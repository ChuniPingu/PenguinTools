using PenguinTools.Core.Metadata;

namespace PenguinTools.Application;

public sealed record WallOverrides(string? InputPath = null, int? WallId = null, WallWidth? Width = null);

public sealed record WallBuildRequest(
    string InputPath, string OutputDirectory, WallOverrides? Overrides = null, int? SongId = null);

public sealed record WallFilesBuildRequest(
    string InputPath, string OutputDirectory, int SongId, WallOverrides? Overrides = null);

public sealed record WallBuildResult(
    string InputPath, string SourcePath, string OutputDirectory, int SongId, int WallId, WallWidth Width,
    IReadOnlyList<ApplicationArtifact> Artifacts);
