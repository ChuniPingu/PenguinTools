using System.Text.Json.Serialization;

namespace PenguinTools.Core.Asset;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AssetType))]
[JsonSerializable(typeof(Entry))]
[JsonSerializable(typeof(Dictionary<AssetType, SortedSet<Entry>>), TypeInfoPropertyName = "AssetDatabase")]
internal sealed partial class AssetJsonSerializerContext : JsonSerializerContext;