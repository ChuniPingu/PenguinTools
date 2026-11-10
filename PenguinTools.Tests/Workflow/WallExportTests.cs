using System.Text.Json;
using PenguinTools.Core;
using PenguinTools.Assets;
using PenguinTools.Infrastructure;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Workflow;

public sealed class WallExportTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("penguintools-walls-");
    private readonly Store _store;
    public WallExportTests() => _store = new Store(_root.FullName);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private string Manifest => Path.Combine(_root.FullName, "wallbind.json");
    private MusicExportContext Context => new(TestAssets.Load(), TestMediaTool.Instance, TestMediaTool.Instance,
        _store, new Provider(_root.FullName));

    [Fact]
    public async Task ParallelBuilds_MergeBindings_CacheResources_AndRemoveOnlyProcessedSongs()
    {
        var cache = new OptionConversionCache();
        var first = await Meta(9978);
        var second = await Meta(2);
        var session = await Session(cache);
        var diagnostics = new DiagnosticCollector();
        await Task.WhenAll(session.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct),
            session.QueueAsync(second, WallRequestOverrides.None, diagnostics, ct: Ct));
        var built = await session.PublishAsync(Ct);
        Assert.True(built.Succeeded, built.ToString());
        Assert.False(diagnostics.HasError);
        Assert.Equal(2, built.Value.Assets.Count);
        var songIds = (await Bindings()).Keys.Order().ToArray();
        Assert.Equal([2, 9978], songIds);
        Assert.NotNull(cache.GetEntry("wall:1009978"));
        Assert.Equal(1009978, (await Bindings())[9978]);

        session = await Session(cache);
        await session.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct);
        var cached = await session.PublishAsync(Ct);
        Assert.True(cached.Succeeded);
        Assert.False(cached.Value!.ManifestChanged); // No conversion or shared-manifest rewrite.

        first.WallWidth = WallWidth.Normal;
        session = await Session(cache);
        await session.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct);
        Assert.True((await session.PublishAsync(Ct)).Value!.ManifestChanged);

        session = await Session(cache);
        await session.QueueAsync(first with { WallFilePath = "", WallMetadataInvalid = true },
            WallRequestOverrides.None, diagnostics, ct: Ct);
        Assert.False((await session.PublishAsync(Ct)).Value!.ManifestChanged);
        Assert.Equal(2, (await Bindings()).Count);

        session = await Session(cache);
        await session.QueueAsync(first with { WallFilePath = "" }, WallRequestOverrides.None, diagnostics, ct: Ct);
        Assert.True((await session.PublishAsync(Ct)).Succeeded);
        Assert.Equal(2, Assert.Single(await Bindings()).Key);
    }

    [Fact]
    public async Task ConflictingSharedId_PreservesOldResources_WhileUnrelatedSongSucceeds()
    {
        var first = await Meta(1);
        first.WallId = 8;
        var second = await Meta(2);
        second.WallId = 8;
        second.WallWidth = WallWidth.Normal;
        var third = await Meta(3);
        var original = await Session();
        var diagnostics = new DiagnosticCollector();
        await original.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct);
        var old = await original.PublishAsync(Ct);
        var oldAsset = Assert.Single(old.Value!.Assets);
        var oldBytes = await File.ReadAllBytesAsync(oldAsset.DdsPath, Ct);

        var session = await Session();
        await session.CheckConflictsAsync([first, second, third], diagnostics, Ct);
        await Task.WhenAll(new[] { first, second, third }.Select(meta =>
            session.QueueAsync(meta, WallRequestOverrides.None, diagnostics, ct: Ct)));
        Assert.True((await session.PublishAsync(Ct)).Succeeded);
        Assert.Contains(diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Wall_conflict);
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(oldAsset.DdsPath, Ct));
        Assert.Equal(new Dictionary<int, int> { [1] = 8, [3] = 1000003 }, await Bindings());
    }

    [Fact]
    public async Task PublicationFailure_RollsBackResourcesAndBindings()
    {
        var meta = await Meta(1);
        var diagnostics = new DiagnosticCollector();
        var first = await Session();
        await first.QueueAsync(meta, WallRequestOverrides.None, diagnostics, ct: Ct);
        var old = await first.PublishAsync(Ct);
        var asset = Assert.Single(old.Value!.Assets);
        var oldManifest = await File.ReadAllBytesAsync(Manifest, Ct);
        await File.WriteAllTextAsync(asset.DdsPath, "old texture", Ct);

        var oldXml = await File.ReadAllBytesAsync(asset.XmlPath, Ct);
        var session = await Session();
        await session.QueueAsync(meta, WallRequestOverrides.None, diagnostics, ct: Ct);
        // Resource commits succeed first; replacing the locked manifest must roll them back.
        using (var locked = new FileStream(Manifest, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = await session.PublishAsync(Ct);
            Assert.False(failed.Succeeded);
        }
        Assert.Equal("old texture", await File.ReadAllTextAsync(asset.DdsPath, Ct));
        Assert.Equal(oldXml, await File.ReadAllBytesAsync(asset.XmlPath, Ct));
        Assert.Equal(oldManifest, await File.ReadAllBytesAsync(Manifest, Ct));
        Assert.Empty(Directory.EnumerateFiles(_root.FullName, "*.bak", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("""{"version":2,"bindings":[]}""")]
    [InlineData("""{"version":1,"bindings":[{"musicId":1,"wallId":2,"width":0}]}""")]
    public async Task InvalidExistingManifest_IsPreserved(string json)
    {
        await File.WriteAllTextAsync(Manifest, json, Ct);
        var result = await WallExportSession.CreateAsync(Context, _root.FullName, Manifest, ct: Ct);
        Assert.False(result.Succeeded);
        Assert.Equal(json, await File.ReadAllTextAsync(Manifest, Ct));
    }

    private async Task<Meta> Meta(int songId)
    {
        var source = Path.Combine(_root.FullName, "wall.png");
        await File.WriteAllTextAsync(source, "fake image", Ct);
        return new Meta { Id = songId, FilePath = Path.Combine(_root.FullName, songId + ".ugc"), WallFilePath = source };
    }

    private async Task<WallExportSession> Session(OptionConversionCache? cache = null)
    {
        var created = await WallExportSession.CreateAsync(Context, _root.FullName, Manifest, cache, ct: Ct);
        Assert.True(created.Succeeded, created.ToString());
        return created.Value;
    }

    private async Task<Dictionary<int, int>> Bindings()
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Manifest, Ct));
        return json.RootElement.GetProperty("bindings").EnumerateArray()
            .ToDictionary(b => b.GetProperty("musicId").GetInt32(), b => b.GetProperty("wallId").GetInt32());
    }

    private sealed record Provider(string Root) : IInfrastructureAssetProvider
    {
        public string GetPath(InfrastructureAsset asset) => Path.Combine(Root, asset.ToString());
    }

    private sealed class Store(string root) : IAssetStore
    {
        public string AssetDirectory => root;
        public string TempWorkPath => root;
        public bool HasAsset(string assetName) => false;
        public string GetAssetPath(string assetName) => Path.Combine(root, assetName);
        public string GetTempPath(string fileName) => Path.Combine(root, fileName);
        public Stream OpenRead(string assetName) => Stream.Null;
        public void Dispose() { }
    }

    public void Dispose()
    {
        _store.Dispose();
        _root.Delete(true);
    }
}
