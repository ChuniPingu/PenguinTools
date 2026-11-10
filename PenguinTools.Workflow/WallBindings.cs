using System.Text.Json;
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

    public Dictionary<int, WallBinding> ToLookup()
    {
        if (Version != 1 || Bindings is null)
        {
            throw new JsonException("Unsupported wallbind document.");
        }

        var result = new Dictionary<int, WallBinding>();
        foreach (var binding in Bindings)
        {
            if (binding is null || binding.MusicId < 0 || binding.WallId < 0 || !Enum.IsDefined(binding.Width))
            {
                throw new JsonException("Invalid wall binding.");
            }
            result[binding.MusicId] = binding;
        }
        return result;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(WallBindingsDocument))]
internal partial class WallBindingsJsonContext : JsonSerializerContext;

public sealed record WallRequestOverrides(string? InputPath = null, int? WallId = null, WallWidth? Width = null)
{
    public static WallRequestOverrides None { get; } = new();
    public bool HasBuildInputs => InputPath is not null || WallId is not null || Width is not null;
}
