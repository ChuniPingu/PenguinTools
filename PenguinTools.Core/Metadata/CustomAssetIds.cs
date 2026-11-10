namespace PenguinTools.Core.Metadata;

public static class CustomAssetIds
{
    public const int SongOffset = 1_000_000;

    public static int? ForSong(int? songId)
    {
        if (songId is not >= 0)
        {
            return null;
        }

        var value = (long)songId.Value + SongOffset;
        return value <= int.MaxValue ? (int)value : null;
    }
}
