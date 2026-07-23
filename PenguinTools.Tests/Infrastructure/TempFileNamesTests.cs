using PenguinTools.Core.IO;
using PenguinTools.Infrastructure;
using Xunit;

namespace PenguinTools.Tests.Infrastructure;

public class TempFileNamesTests
{
    [Fact]
    public void MakeUnique_PreservesStemAndExtension_AndDiffersAcrossCalls()
    {
        var first = TempFileNames.MakeUnique("c_240.wav");
        var second = TempFileNames.MakeUnique("c_240.wav");

        Assert.StartsWith("c_240.", first);
        Assert.EndsWith(".wav", first);
        Assert.StartsWith("c_240.", second);
        Assert.EndsWith(".wav", second);
        Assert.NotEqual(first, second);
        Assert.Matches(@"^c_240\.[0-9a-f]{32}\.wav$", first);
        Assert.Matches(@"^c_240\.[0-9a-f]{32}\.wav$", second);
    }

    [Fact]
    public void MakeUnique_UsesFileNameOnly_WhenPathIsProvided()
    {
        var name = TempFileNames.MakeUnique(@"C:\songs\Grief & Malice\240.mp3");
        Assert.StartsWith("240.", name);
        Assert.EndsWith(".mp3", name);
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('/', name);
    }

    [Fact]
    public void AssetStore_GetTempPath_ReturnsDistinctPaths_ForSameHint()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new AssetStore(root, Path.Combine(root, "temp"));
            var first = store.GetTempPath("c_240.wav");
            var second = store.GetTempPath("c_240.wav");

            Assert.Equal(store.TempWorkPath, Path.GetDirectoryName(first));
            Assert.Equal(store.TempWorkPath, Path.GetDirectoryName(second));
            Assert.NotEqual(first, second);
            Assert.StartsWith("c_240.", Path.GetFileName(first));
            Assert.EndsWith(".wav", first);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
