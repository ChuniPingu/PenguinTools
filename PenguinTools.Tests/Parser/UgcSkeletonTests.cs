using PenguinTools.Chart.Parser.ugc;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class UgcSkeletonTests
{
    [Fact]
    public async Task EmptyUgc_ReturnsEmptyChart()
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(tmp, "@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120.0\n\n", ct);
            var assets = TestAssets.Load();
            var parser = new UgcParser(new UgcParseRequest(tmp, assets), TestMediaTool.Instance);

            var result = await parser.ParseAsync(ct);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value!.Notes.Children);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
