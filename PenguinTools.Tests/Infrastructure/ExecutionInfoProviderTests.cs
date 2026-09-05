using PenguinTools.Core;
using PenguinTools.Infrastructure;
using Xunit;

namespace PenguinTools.Tests.Infrastructure;

public class ExecutionInfoProviderTests
{
    [Fact]
    public void Create_ReportsSuppliedPathsWithoutRequiringDirectories()
    {
        var root = TestTempPaths.Create(".dir");
        var externalAssets = Path.Combine(root, "assets");
        var paths = new TestApplicationPaths(Path.Combine(root, "temp"));

        var info = ExecutionInfoProvider.Create(paths, externalAssets);

        Assert.Equal(externalAssets, info.InfrastructureAssetsPath);
        Assert.Equal(paths.TempWorkPath, info.TempWorkPath);
    }
    private sealed class TestApplicationPaths(string tempWorkPath) : IApplicationPaths
    {
        public string TempWorkPath { get; } = tempWorkPath;
    }
}
