using PenguinTools.Core.Asset;

namespace PenguinTools.Tests;

internal static class TestAssets
{
    public static AssetManager Load(string? userAssetsPath = null)
    {
        var dir = Path.GetDirectoryName(typeof(TestAssets).Assembly.Location)!;
        var cursor = dir;
        while (cursor is not null && !File.Exists(Path.Combine(cursor, "assets.json")))
            cursor = Directory.GetParent(cursor)?.FullName;
        if (cursor is null)
            throw new FileNotFoundException("assets.json not found above " + dir);
        using var fs = File.OpenRead(Path.Combine(cursor, "assets.json"));
        return new AssetManager(fs, userAssetsPath);
    }
}
