using System.Text.Json.Serialization;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Workflow;

public sealed record WallBinding(
    [property: JsonRequired] int MusicId,
    [property: JsonRequired] int WallId,
    WallWidth Width = WallWidth.Wide);

internal sealed class WallBindingsDocument
{
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public List<WallBinding> Bindings { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(WallBindingsDocument))]
internal partial class WallBindingsJsonContext : JsonSerializerContext;

public sealed record WallRequestOverrides(string? InputPath = null, int? WallId = null, WallWidth? Width = null)
{
    public static WallRequestOverrides None { get; } = new();
    public bool HasBuildInputs => InputPath is not null || WallId is not null || Width is not null;
}
