using PenguinTools.Core;
using PenguinTools.Core.IO;

namespace PenguinTools.Infrastructure;

public sealed class AssetStore : IAssetStore
{
    public AssetStore(string assetDirectory, string tempWorkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempWorkPath);

        AssetDirectory = assetDirectory;
        TempWorkPath = tempWorkPath;
        Directory.CreateDirectory(TempWorkPath);
    }

    public string AssetDirectory { get; }

    public string TempWorkPath { get; }

    public bool HasAsset(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        return File.Exists(Path.Combine(AssetDirectory, assetName));
    }

    public string GetAssetPath(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        var path = GetAssetFilePath(assetName);
        ResourceStoreHelpers.EnsureExecutableIfNeeded(path, assetName);
        return path;
    }

    public string GetTempPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Directory.CreateDirectory(TempWorkPath);
        return Path.Combine(TempWorkPath, TempFileNames.MakeUnique(fileName));
    }

    public Stream OpenRead(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        return File.OpenRead(GetAssetFilePath(assetName));
    }

    public void Dispose()
    {
        ResourceStoreHelpers.ClearDirectory(TempWorkPath, true);
    }

    private string GetAssetFilePath(string assetName)
    {
        var path = Path.Combine(AssetDirectory, assetName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Asset '{assetName}' was not found in asset directory '{AssetDirectory}'.",
                path);

        return path;
    }
}
