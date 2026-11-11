using System.Text.Json;
using PenguinTools.Assets;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Infrastructure;
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
    public async Task ParallelBuilds_ReplaceBindings_ReuseResources_AndWriteEmptyManifest()
    {
        var cache = new OptionConversionCache();
        var first = await Meta(9978);
        var second = await Meta(2);
        var session = Session(cache);
        var diagnostics = new DiagnosticCollector();
        await Task.WhenAll(session.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct),
            session.QueueAsync(second, WallRequestOverrides.None, diagnostics, ct: Ct));
        var built = await session.PublishAsync(ct: Ct);
        Assert.True(built.Succeeded, built.ToString());
        Assert.False(diagnostics.HasError);
        var songIds = (await Bindings()).Keys.Order().ToArray();
        Assert.Equal([2, 9978], songIds);
        Assert.NotNull(cache.GetEntry("wall:1009978"));
        var asset = built.Value.Assets.Single(a => a.Name.Id == 1009978);

        // A cache hit may replace the manifest, but must not replace these locked image outputs.
        using (var lockedDds = new FileStream(asset.DdsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var lockedXml = new FileStream(asset.XmlPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            session = Session(cache);
            await session.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct);
            var cached = await session.PublishAsync(ct: Ct);
            Assert.True(cached.Succeeded, cached.ToString());
            Assert.True(cached.Value.ManifestChanged);
        }
        Assert.Equal(9978, Assert.Single(await Bindings()).Key);
        Assert.All(built.Value.Assets, a => Assert.True(File.Exists(a.DdsPath)));

        var recipe = cache.GetEntry("wall:1009978")!.RecipeHash;
        first.WallWidth = WallWidth.Normal;
        session = Session(cache);
        await session.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct);
        Assert.True((await session.PublishAsync(ct: Ct)).Succeeded);
        Assert.NotEqual(recipe, cache.GetEntry("wall:1009978")!.RecipeHash);

        session = Session(cache);
        await session.QueueAsync(first with { WallFilePath = "" }, WallRequestOverrides.None, diagnostics, ct: Ct);
        Assert.True((await session.PublishAsync(ct: Ct)).Succeeded);
        Assert.Empty(await Bindings());
    }

    [Fact]
    public async Task EmptyBuild_WritesAnExplicitEmptyManifest()
    {
        var result = await Session().PublishAsync(ct: Ct);
        Assert.True(result.Succeeded);
        Assert.True(result.Value.ManifestChanged);
        Assert.Empty(await Bindings());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidOptionalWall_KeepsTheWholePreviousPublication(bool invalidMetadata)
    {
        var first = await Meta(1);
        var second = await Meta(2);
        var diagnostics = new DiagnosticCollector();
        var session = Session();
        await session.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct);
        var old = await session.PublishAsync(ct: Ct);
        Assert.True(old.Succeeded);
        var manifest = await File.ReadAllBytesAsync(Manifest, Ct);
        var oldAsset = Assert.Single(old.Value.Assets);
        await File.WriteAllTextAsync(oldAsset.DdsPath, "old texture", Ct);
        session = Session();
        await session.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct);
        await session.QueueAsync(second with
        {
            WallMetadataInvalid = invalidMetadata,
            WallImageValid = invalidMetadata,
            WallFilePath = invalidMetadata ? "" : second.WallFilePath
        }, WallRequestOverrides.None, diagnostics, ct: Ct);
        var skipped = await session.PublishAsync(ct: Ct);
        Assert.True(skipped.Succeeded);
        Assert.False(skipped.Value.ManifestChanged);
        Assert.Empty(skipped.Value.Assets);
        Assert.Empty(skipped.Diagnostics.Diagnostics);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(Manifest, Ct));
        Assert.Equal("old texture", await File.ReadAllTextAsync(oldAsset.DdsPath, Ct));
    }

    [Fact]
    public async Task ConflictingSharedId_PreservesTheWholePreviousPublication()
    {
        var first = await Meta(1);
        first.WallId = 8;
        var second = await Meta(2);
        second.WallId = 8;
        second.WallWidth = WallWidth.Normal;
        var third = await Meta(3);
        var original = Session();
        var diagnostics = new DiagnosticCollector();
        await original.QueueAsync(first, WallRequestOverrides.None, diagnostics, ct: Ct);
        var old = await original.PublishAsync(ct: Ct);
        var oldAsset = Assert.Single(old.Value!.Assets);
        var oldBytes = await File.ReadAllBytesAsync(oldAsset.DdsPath, Ct);

        var session = Session();
        await session.CheckConflictsAsync([first, second, third], diagnostics, Ct);
        await Task.WhenAll(new[] { first, second, third }.Select(meta =>
            session.QueueAsync(meta, WallRequestOverrides.None, diagnostics, ct: Ct)));
        var skipped = await session.PublishAsync(ct: Ct);
        Assert.True(skipped.Succeeded);
        Assert.False(skipped.Value.ManifestChanged);
        Assert.Empty(skipped.Value.Assets);
        Assert.Contains(diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Wall_conflict);
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(oldAsset.DdsPath, Ct));
        Assert.Equal(new Dictionary<int, int> { [1] = 8 }, await Bindings());
    }

    [Fact]
    public async Task PublicationFailure_RollsBackResourcesAndBindings()
    {
        var meta = await Meta(1);
        var diagnostics = new DiagnosticCollector();
        var first = Session();
        await first.QueueAsync(meta, WallRequestOverrides.None, diagnostics, ct: Ct);
        var old = await first.PublishAsync(ct: Ct);
        var asset = Assert.Single(old.Value!.Assets);
        var oldManifest = await File.ReadAllBytesAsync(Manifest, Ct);
        await File.WriteAllTextAsync(asset.DdsPath, "old texture", Ct);

        var oldXml = await File.ReadAllBytesAsync(asset.XmlPath, Ct);
        var session = Session();
        await session.QueueAsync(meta, WallRequestOverrides.None, diagnostics, ct: Ct);
        // Resource commits succeed first; replacing the locked manifest must roll them back.
        using (var locked = new FileStream(Manifest, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = await session.PublishAsync(ct: Ct);
            Assert.False(failed.Succeeded);
            var diagnostic = Assert.Single(failed.Diagnostics.Diagnostics);
            Assert.Equal(MsgKeys.Error_Unhandled, diagnostic.Message.Key);
            Assert.IsType<IOException>(diagnostic.RelatedException);
        }
        Assert.Equal("old texture", await File.ReadAllTextAsync(asset.DdsPath, Ct));
        Assert.Equal(oldXml, await File.ReadAllBytesAsync(asset.XmlPath, Ct));
        Assert.Equal(oldManifest, await File.ReadAllBytesAsync(Manifest, Ct));
        Assert.Empty(Directory.EnumerateFiles(_root.FullName, "*.bak", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("""{"version":2,"bindings":[]}""")]
    [InlineData("""{"version":1,"bindings":[{"musicId":1,"wallId":2,"width":0}]}""")]
    public async Task InvalidExistingManifest_IsReplacedWithoutReading(string json)
    {
        await File.WriteAllTextAsync(Manifest, json, Ct);
        var result = await Session().PublishAsync(ct: Ct);
        Assert.True(result.Succeeded);
        Assert.True(result.Value.ManifestChanged);
        Assert.Empty(await Bindings());
    }

    [Theory]
    [InlineData(null, null, WallWidth.Wide, MsgKeys.Error_Song_id_is_not_set)]
    [InlineData(-1, null, WallWidth.Wide, MsgKeys.Error_Song_id_invalid)]
    [InlineData(1, -1, WallWidth.Wide, MsgKeys.Error_Wall_id_invalid)]
    [InlineData(2_000_000_001, null, WallWidth.Wide, MsgKeys.Error_Song_id_invalid)]
    [InlineData(int.MaxValue, 1, WallWidth.Wide, MsgKeys.Error_Song_id_invalid)]
    [InlineData(1, null, (WallWidth)99, MsgKeys.Error_Wall_width_invalid)]
    public async Task InvalidSettings_ReportTheAffectedField_WithoutPublishing(
        int? songId, int? wallId, WallWidth width, string expectedKey)
    {
        var meta = await Meta(1);
        meta.Id = songId;
        meta.WallId = wallId;
        meta.WallWidth = width;
        var session = Session();
        var diagnostics = new DiagnosticCollector();

        await session.QueueAsync(meta, WallRequestOverrides.None, diagnostics, required: true, ct: Ct);

        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal(expectedKey, diagnostic.Message.Key);
        Assert.Equal(meta.FilePath, diagnostic.Path);
        var published = await session.PublishAsync(ct: Ct);
        Assert.True(published.Succeeded);
        Assert.Empty(published.Value.Assets);
        Assert.False(File.Exists(Manifest));
    }

    [Theory]
    [InlineData(-1, WallWidth.Wide, MsgKeys.Error_Wall_id_invalid)]
    [InlineData(1, (WallWidth)99, MsgKeys.Error_Wall_width_invalid)]
    public async Task DirectConversion_ReportsTheAffectedField(int wallId, WallWidth width, string expectedKey)
    {
        var meta = await Meta(1);
        var result = await new WallConverter(
            new WallBuildRequest(Context.Assets, meta.FullWallFilePath, wallId, width, _root.FullName),
            TestMediaTool.Instance).PrepareAsync(Ct);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedKey, Assert.Single(result.Diagnostics.Diagnostics).Message.Key);
    }

    private async Task<Meta> Meta(int songId)
    {
        var source = Path.Combine(_root.FullName, "wall.png");
        await File.WriteAllTextAsync(source, "fake image", Ct);
        return new Meta { Id = songId, FilePath = Path.Combine(_root.FullName, songId + ".ugc"), WallFilePath = source };
    }

    private WallExportSession Session(OptionConversionCache? cache = null) =>
        new(Context, _root.FullName, Manifest, cache);

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
