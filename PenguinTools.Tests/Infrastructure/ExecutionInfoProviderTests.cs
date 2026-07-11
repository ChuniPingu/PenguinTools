using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Infrastructure;
using Xunit;

namespace PenguinTools.Tests.Infrastructure;

public class ExecutionInfoProviderTests
{
    [Fact]
    public void Create_UsesExternalAssetsDirectory_WhenOverrideProvided()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var userData = Path.Combine(tempRoot, "userdata");
        var externalAssets = Path.Combine(tempRoot, "assets");
        Directory.CreateDirectory(externalAssets);

        try
        {
            var paths = new TestApplicationPaths(
                Path.Combine(tempRoot, "temp"),
                userData);
            var info = ExecutionInfoProvider.Create(paths, externalAssets);

            Assert.Equal(externalAssets, info.InfrastructureAssetsPath);
            Assert.Equal(Path.Combine(userData, AssetManager.PlusAssetsFileName), info.PlusAssetsPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void Create_UsesDefaultAssetsDirectory_WhenPathIsNotSpecified()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var userData = Path.Combine(tempRoot, "userdata");

        try
        {
            var paths = new TestApplicationPaths(
                Path.Combine(tempRoot, "temp"),
                userData);
            var info = ExecutionInfoProvider.Create(paths, AssetPaths.Resolve());

            Assert.Equal(
                Path.Combine(AppContext.BaseDirectory, AssetPaths.DefaultSubdirectory),
                info.InfrastructureAssetsPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private sealed class TestApplicationPaths(string tempWorkPath, string userDataPath) : IApplicationPaths
    {
        public string TempWorkPath { get; } = tempWorkPath;
        public string UserDataPath { get; } = userDataPath;
    }
}