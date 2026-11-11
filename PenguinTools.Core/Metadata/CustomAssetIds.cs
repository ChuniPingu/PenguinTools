namespace PenguinTools.Core.Metadata;

public static class CustomAssetIds
{
    public const int SongOffset = 1_000_000;

    public static int? ForSong(int? songId) =>
        songId is >= 0 and <= ResourceIds.MaxSongId ? songId + SongOffset : null;
}
