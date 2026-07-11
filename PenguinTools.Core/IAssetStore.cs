namespace PenguinTools.Core;

public interface IAssetStore : IDisposable
{
    string AssetDirectory { get; }

    string TempWorkPath { get; }

    bool HasAsset(string assetName);

    string GetAssetPath(string assetName);

    string GetTempPath(string fileName);

    Stream OpenRead(string assetName);
}