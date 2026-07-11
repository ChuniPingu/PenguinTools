using System.Reflection;
using PenguinTools.Core;
using PenguinTools.Core.Asset;

namespace PenguinTools.Infrastructure;

public static class ExecutionInfoProvider
{
    public static ExecutionInfo Create(
        IApplicationPaths paths,
        string assetsDirectory,
        AssetManager? assets = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsDirectory);

        var entryAssembly = Assembly.GetEntryAssembly();
        var applicationName = entryAssembly?.GetName().Name ?? "PenguinTools";
        var version = entryAssembly?.GetName().Version?.ToString() ?? "unknown";

        return new ExecutionInfo(
            applicationName,
            version,
            BuildDateAttribute.GetAssemblyBuildDate(entryAssembly),
            AppContext.BaseDirectory,
            paths.TempWorkPath,
            paths.UserDataPath,
            assetsDirectory,
            assets?.PlusAssetsPath ?? Path.Combine(paths.UserDataPath, AssetManager.PlusAssetsFileName));
    }
}