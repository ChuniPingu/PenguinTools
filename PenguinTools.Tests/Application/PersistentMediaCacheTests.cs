using System.Text.Json;
using System.Text.Json.Nodes;
using PenguinTools.Application;
using PenguinTools.CLI;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed partial class PenguinToolsApplicationTests
{
    private static async Task<IncrementalBuildResult> BuildColdAsync(SessionFixture fixture, string content,
        string identity = "test-tools", OptionBuildOverrides? overrides = null)
    {
        using var session = fixture.CreateSession();
        var request = fixture.Request(content) with
        {
            DocumentId = Guid.NewGuid().ToString(),
            CacheDirectory = Path.Combine(fixture.Root, "cache"),
            CacheIdentity = identity,
            Overrides = overrides ?? new(OptionName: "A001")
        };
        var result = await session.PrepareAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, string.Join('\n', result.Diagnostics.Diagnostics.Select(item => item.Message)));
        return result.Value!;
    }

    [Fact]
    public async Task PersistentMediaCache_ColdSessionReusesMediaWithCurrentChartAndIndependentSnapshot()
    {
        using var fixture = new SessionFixture();
        var content = SessionChart + "@BGIMG\tjacket.png\n@CMT\t#meta wall jacket.png wide\n";
        var first = await BuildColdAsync(fixture, content);
        var original = await File.ReadAllBytesAsync(first.ChartPath!, TestContext.Current.CancellationToken);
        var second = await BuildColdAsync(fixture, content.Replace("#0'0:t02", "#0'120:t02"));
        Assert.Equal(1, fixture.Media.AudioConversions);
        Assert.Equal(1, fixture.Media.JacketConversions);
        Assert.Equal(1, fixture.Media.StageConversions);
        Assert.Equal(1, fixture.Media.WallConversions);
        Assert.NotEqual(original, await File.ReadAllBytesAsync(second.ChartPath!, TestContext.Current.CancellationToken));
        Assert.Equal(2, fixture.Media.AudioValidations);
        Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, "cache"), "g-*"));
        Directory.Delete(Path.Combine(fixture.Root, "cache"), recursive: true);
        Assert.All(second.Artifacts, item => Assert.True(File.Exists(item.Path)));
        Assert.Equal(original, await File.ReadAllBytesAsync(first.ChartPath!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PersistentMediaCache_ChangedBytesTimingSettingsAndToolIdentityInvalidateDependencies()
    {
        using var fixture = new SessionFixture();
        await BuildColdAsync(fixture, SessionChart);
        var audio = Path.Combine(fixture.Root, "audio.wav");
        var timestamp = File.GetLastWriteTimeUtc(audio);
        await File.WriteAllTextAsync(audio, "audio-2", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(audio, timestamp);
        await BuildColdAsync(fixture, SessionChart);
        Assert.Equal(2, fixture.Media.AudioConversions);
        Assert.Equal(1, fixture.Media.JacketConversions);
        var shifted = SessionChart + "@BGMOFS\t0.5\n";
        await BuildColdAsync(fixture, shifted);
        Assert.Equal(3, fixture.Media.AudioConversions);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "options.json"),
            "{\"hcaEncryptionKey\":12345}", TestContext.Current.CancellationToken);
        await BuildColdAsync(fixture, shifted);
        Assert.Equal(4, fixture.Media.AudioConversions);
        Assert.Equal(1, fixture.Media.JacketConversions);
        await BuildColdAsync(fixture, shifted, "changed-tools");
        Assert.Equal(5, fixture.Media.AudioConversions);
        Assert.Equal(2, fixture.Media.JacketConversions);
    }

    [Fact]
    public async Task PersistentMediaCache_RemovedMediaIsAbsentAndAppearingMediaIsValidatedAgain()
    {
        using var fixture = new SessionFixture();
        await BuildColdAsync(fixture, SessionChart);
        var image = Path.Combine(fixture.Root, "jacket.png");
        File.Delete(image);
        var removed = await BuildColdAsync(fixture, SessionChart.Replace("@JACKET\tjacket.png", "@JACKET\t"));
        Assert.DoesNotContain(removed.Artifacts, item => item.Kind == "jacket");
        Assert.Equal(1, fixture.Media.AudioConversions);
        await File.WriteAllTextAsync(image, "replacement", TestContext.Current.CancellationToken);
        var restored = await BuildColdAsync(fixture, SessionChart);
        Assert.Equal(2, fixture.Media.JacketConversions);
        Assert.Contains(restored.Artifacts, item => item.Kind == "jacket");
        Assert.Equal(1, fixture.Media.AudioConversions);
    }

    [Fact]
    public async Task PersistentMediaCache_IdentityChangeInvalidatesLiveSessionAndValidation()
    {
        using var fixture = new SessionFixture();
        var request = fixture.Request(SessionChart) with
        { CacheDirectory = Path.Combine(fixture.Root, "cache"), CacheIdentity = "first" };
        Assert.True((await fixture.Session.PrepareAsync(request, TestContext.Current.CancellationToken)).Succeeded);
        request = request with { OutputDirectory = Path.Combine(fixture.Root, "identity-next"), CacheIdentity = "second" };
        Assert.True((await fixture.Session.PrepareAsync(request, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(2, fixture.Media.AudioConversions);
        Assert.Equal(2, fixture.Media.JacketConversions);
        Assert.Equal(2, fixture.Media.AudioValidations);
        Assert.Equal(2, fixture.Media.ImageValidations);
    }

    [Fact]
    public async Task PersistentMediaCache_MissingAndCorruptOutputsRebuildAndRepairCheckpoint()
    {
        using var fixture = new SessionFixture();
        await BuildColdAsync(fixture, SessionChart);
        var cache = Path.Combine(fixture.Root, "cache");
        var awb = Assert.Single(Directory.GetFiles(cache, "*.awb", SearchOption.AllDirectories));
        await File.WriteAllTextAsync(awb, "broken", TestContext.Current.CancellationToken);
        await BuildColdAsync(fixture, SessionChart);
        Assert.Equal(2, fixture.Media.AudioConversions);
        Assert.Equal(1, fixture.Media.JacketConversions);
        File.Delete(Assert.Single(Directory.GetFiles(cache, "*.dds", SearchOption.AllDirectories)));
        await BuildColdAsync(fixture, SessionChart);
        Assert.Equal(2, fixture.Media.AudioConversions);
        Assert.Equal(2, fixture.Media.JacketConversions);
        await File.WriteAllTextAsync(Path.Combine(cache, "checkpoint.json"), "{broken", TestContext.Current.CancellationToken);
        await BuildColdAsync(fixture, SessionChart);
        Assert.Equal(3, fixture.Media.AudioConversions);
        Assert.NotNull(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(cache, "checkpoint.json"), TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("artifact")]
    public async Task PersistentMediaCache_RejectsTraversalAndPreservesUnownedFiles(string field)
    {
        using var fixture = new SessionFixture();
        await BuildColdAsync(fixture, SessionChart);
        var cache = Path.Combine(fixture.Root, "cache");
        var path = Path.Combine(cache, "checkpoint.json");
        var checkpoint = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        var protectedFile = Path.Combine(fixture.Root, "sentinel");
        await File.WriteAllTextAsync(protectedFile, "keep", TestContext.Current.CancellationToken);
        if (field == "generation")
        {
            checkpoint["generation"] = "..";
        }
        else
        {
            checkpoint["artifacts"]![0]!["path"] = "../../sentinel";
        }
        await File.WriteAllTextAsync(path, checkpoint.ToJsonString(), TestContext.Current.CancellationToken);
        await BuildColdAsync(fixture, SessionChart);
        Assert.Equal(2, fixture.Media.AudioConversions);
        Assert.Equal("keep", await File.ReadAllTextAsync(protectedFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PersistentMediaCache_FailedExportKeepsCheckpointAndUnwritableCacheAllowsSuccess()
    {
        using var fixture = new SessionFixture();
        await BuildColdAsync(fixture, SessionChart);
        var cache = Path.Combine(fixture.Root, "cache");
        var checkpoint = await File.ReadAllBytesAsync(Path.Combine(cache, "checkpoint.json"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "jacket.png"), "image-2", TestContext.Current.CancellationToken);
        fixture.Media.FailJacket = true;
        using (var failed = fixture.CreateSession())
        {
            var result = await failed.PrepareAsync(fixture.Request(SessionChart) with
            { CacheDirectory = cache, CacheIdentity = "test-tools" }, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
        }
        Assert.Equal(checkpoint, await File.ReadAllBytesAsync(Path.Combine(cache, "checkpoint.json"), TestContext.Current.CancellationToken));
        fixture.Media.FailJacket = false;
        Directory.Delete(cache, recursive: true);
        await File.WriteAllTextAsync(cache, "not a directory", TestContext.Current.CancellationToken);
        var successful = await BuildColdAsync(fixture, SessionChart);
        Assert.True(File.Exists(successful.ChartPath));
        Assert.Equal("not a directory", await File.ReadAllTextAsync(cache, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PersistentMediaCache_RejectsReparseRootWithoutChangingItsTarget()
    {
        using var fixture = new SessionFixture();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "caller-data"));
        var sentinel = Path.Combine(target.FullName, "sentinel");
        await File.WriteAllTextAsync(sentinel, "keep", TestContext.Current.CancellationToken);
        var link = Path.Combine(fixture.Root, "cache");
        try
        {
            Directory.CreateSymbolicLink(link, target.FullName);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Skip("Creating a symbolic link requires filesystem permission.");
        }
        try
        {
            await BuildColdAsync(fixture, SessionChart);
            Assert.Equal("keep", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFileSystemEntries(target.FullName));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task PersistentMediaCache_JsonlAdvertisesCapabilityAndAcceptsCacheFields()
    {
        using var fixture = new SessionFixture();
        var cache = Path.Combine(fixture.Root, "cache");
        var request = new SessionRequest(1, "prepare", "document", fixture.Root, "ugc", SessionChart,
            OutputDirectory: Path.Combine(fixture.Root, "jsonl-output"), Overrides: new(OptionName: "A001"),
            CacheDirectory: cache, CacheIdentity: "opaque:v1");
        using var input = new StringReader(JsonSerializer.Serialize(request, CliJsonSerializerContext.Default.SessionRequest));
        using var output = new StringWriter();
        Assert.Equal(0, await SessionCommands.RunAsync(fixture.Session, input, output, TestContext.Current.CancellationToken));
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var greeting = JsonDocument.Parse(lines[0]);
        Assert.Contains(greeting.RootElement.GetProperty("capabilities").EnumerateArray(), item => item.GetString() == "persistentMediaCache");
        using var response = JsonDocument.Parse(lines[1]);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean());
        Assert.True(File.Exists(Path.Combine(cache, "checkpoint.json")));
    }
}
