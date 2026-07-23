using System.Text.Json.Serialization;

namespace PenguinTools.CRI;

internal sealed record ExtractManifest(int SchemaVersion, string Source, IReadOnlyList<ExtractedCue> Cues);

internal sealed record ExtractedCue(
    int CueId,
    string? Name,
    string WavPath,
    ushort Channels,
    uint SampleRate,
    ushort BitsPerSample,
    uint SampleFrames,
    uint? PreviewStartMs = null,
    uint? PreviewStopMs = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExtractManifest))]
internal sealed partial class CriJsonContext : JsonSerializerContext;
