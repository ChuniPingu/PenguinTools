using PenguinTools.Core;
using PenguinTools.Infrastructure;
using Xunit;

namespace PenguinTools.Tests.Infrastructure;

public class ExecutionInfoProviderTests
{
    [Fact]
    public void Create_UsesExternalAssetsDirectory_WhenOverrideProvided()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var externalAssets = Path.Combine(tempRoot, "assets");
        Directory.CreateDirectory(externalAssets);

        try
        {
            var paths = new TestApplicationPaths(Path.Combine(tempRoot, "temp"));
            var info = ExecutionInfoProvider.Create(paths, externalAssets);

            Assert.Equal(externalAssets, info.InfrastructureAssetsPath);
            Assert.Equal(Path.Combine(tempRoot, "temp"), info.TempWorkPath);
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

        try
        {
            var paths = new TestApplicationPaths(Path.Combine(tempRoot, "temp"));
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

    private sealed class TestApplicationPaths(string tempWorkPath) : IApplicationPaths
    {
        public string TempWorkPath { get; } = tempWorkPath;
    }
}
