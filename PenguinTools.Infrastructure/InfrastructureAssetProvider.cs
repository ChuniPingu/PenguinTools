using System.Collections.Concurrent;
using PenguinTools.Assets;
using PenguinTools.Core;

namespace PenguinTools.Infrastructure;

public interface IInfrastructureAssetProvider
{
    string GetPath(InfrastructureAsset asset);
}

public sealed class InfrastructureAssetProvider(IAssetStore assets) : IInfrastructureAssetProvider
{
    private readonly ConcurrentDictionary<InfrastructureAsset, string> _paths = new();

    private IAssetStore Assets { get; } = assets ?? throw new ArgumentNullException(nameof(assets));

    public string GetPath(InfrastructureAsset asset)
    {
        return _paths.GetOrAdd(asset, ResolvePath);
    }

    private string ResolvePath(InfrastructureAsset asset)
    {
        return asset switch
        {
            InfrastructureAsset.Mua => ResolveMuaDirectory(),
            _ => throw new ArgumentOutOfRangeException(nameof(asset), asset, null)
        };
    }

    private string ResolveMuaDirectory()
    {
        var path = Path.Combine(Assets.AssetDirectory, "mua");
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(
                $"mua publish directory was not found in asset directory '{Assets.AssetDirectory}'.");

        return path;
    }
}