using System.Text.Json.Serialization;

namespace PenguinTools.Core.Metadata;

[JsonConverter(typeof(WallWidthJsonConverter))]
public enum WallWidth
{
    [JsonStringEnumMemberName("normal")] Normal,
    [JsonStringEnumMemberName("wide")] Wide
}

public partial record Meta
{
    public string WallFilePath { get; set; } = string.Empty;
    public string FullWallFilePath => GetFullPath(WallFilePath);

    /// <summary>An explicit resource ID; null follows the song ID.</summary>
    public int? WallId { get; set; }
    public int? EffectiveWallId => WallId ?? CustomAssetIds.ForSong(Id);
    public WallWidth WallWidth { get; set; } = WallWidth.Wide;
    public bool WallMetadataInvalid { get; set; }
    public bool WallImageValid { get; set; } = true;
}
