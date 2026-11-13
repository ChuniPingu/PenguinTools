namespace PenguinTools.Application;

public sealed record IncrementalBuildRequest(
    string DocumentId,
    string BaseDirectory,
    ChartFormat Format,
    string OutputDirectory,
    string? Content = null,
    string? InputPath = null,
    OptionBuildOverrides? Overrides = null,
    string? CacheDirectory = null,
    string? CacheIdentity = null);

public sealed record IncrementalArtifact(string Kind, string Path, string Hash, string Status);

public sealed record IncrementalBuildResult(
    long Revision,
    string OptionDirectory,
    string? ChartPath,
    ChartSummary Chart,
    ChartConversionMetadata Metadata,
    IReadOnlyList<IncrementalArtifact> Artifacts,
    IReadOnlyList<string> Changes);
