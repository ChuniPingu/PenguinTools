namespace PenguinTools.Media;

public sealed record CriExtractOptions(
    string SourcePath,
    string OutputDirectory,
    string? PairedInputPath = null,
    ulong? HcaKey = null);

public sealed record CriCue(
    int CueId,
    string? Name,
    string WavPath,
    ushort Channels,
    uint SampleRate,
    ushort BitsPerSample,
    uint SampleFrames,
    uint? PreviewStartMs = null,
    uint? PreviewStopMs = null);

public sealed record CriExtractResult(int SchemaVersion, string Source, IReadOnlyList<CriCue> Cues);

public sealed record DdsDecodeResult(string SourcePath, string OutputPath);
