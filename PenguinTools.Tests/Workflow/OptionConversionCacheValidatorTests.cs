using System.Security.Cryptography;
using System.Text;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Workflow;

public sealed class OptionConversionCacheValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotCopy_VerifiesSourceBeforeReplacingIndependentOutputs(bool corrupt)
    {
        var root = Directory.CreateTempSubdirectory("PenguinToolsCacheCopy-");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var source = Path.Combine(root.FullName, "source");
            var target = Path.Combine(root.FullName, "target");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            var input = Path.Combine(source, "music.awb");
            var output = Path.Combine(target, "music.awb");
            await File.WriteAllTextAsync(input, "verified source", ct);
            await File.WriteAllTextAsync(output, "previous output", ct);
            var cache = new OptionConversionCache();
            var state = new OptionConversionCacheState("recipe", new Dictionary<string, string>());
            await OptionConversionCacheValidator.StoreAsync(cache, "audio:123", state, [new("awb", input)], ct);
            var snapshot = cache.ForSnapshot(source, target);
            if (corrupt)
            {
                await File.WriteAllTextAsync(input, "corrupt source", ct);
            }
            var hit = await OptionConversionCacheValidator.IsHitAsync(snapshot, "audio:123", state, [new("awb", output)], ct);
            Assert.Equal(!corrupt, hit);
            Assert.Equal(corrupt ? "previous output" : "verified source", await File.ReadAllTextAsync(output, ct));
            Assert.Single(Directory.GetFiles(target));
            if (corrupt)
            {
                Assert.Null(snapshot.VerifiedOutputHash(output));
            }
            else
            {
                Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("verified source"))),
                    snapshot.VerifiedOutputHash(output));
                await File.WriteAllTextAsync(output, "caller edit", ct);
                Assert.Equal("verified source", await File.ReadAllTextAsync(input, ct));
            }
        }
        finally
        {
            root.Delete(true);
        }
    }
}
