using PenguinTools.Infrastructure;
using Xunit;

namespace PenguinTools.Tests.Infrastructure;

[CollectionDefinition("Asset path environment", DisableParallelization = true)]
public sealed class AssetPathEnvironmentCollection;

[Collection("Asset path environment")]
public sealed class AssetPathsTests
{
    [Fact]
    public void Resolve_PrefersExplicitPathThenEnvironmentThenDefault()
    {
        var previous = Environment.GetEnvironmentVariable(AssetPaths.PathEnvironmentVariable);
        var explicitPath = Path.GetFullPath("explicit-assets");
        var environmentPath = Path.GetFullPath("environment-assets");
        try
        {
            Environment.SetEnvironmentVariable(AssetPaths.PathEnvironmentVariable, $" {environmentPath} ");
            Assert.Equal(explicitPath, AssetPaths.Resolve($" {explicitPath} "));
            Assert.Equal(environmentPath, AssetPaths.Resolve());

            Environment.SetEnvironmentVariable(AssetPaths.PathEnvironmentVariable, null);
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, AssetPaths.DefaultSubdirectory), AssetPaths.Resolve());
        }
        finally
        {
            Environment.SetEnvironmentVariable(AssetPaths.PathEnvironmentVariable, previous);
        }
    }
}
