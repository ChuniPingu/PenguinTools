using PenguinTools.Core.Asset;
using Xunit;

namespace PenguinTools.Tests;

public sealed class AssetDictionaryCollectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CollectAsync_ReadsAllFieldsAndPreservesSortedFirstEntries()
    {
        WriteXml("A000/music/Music.xml", """
            <MusicData>
              <genreNames><id> 2 </id><str> Genre </str><data> Extra </data></genreNames>
              <stageName><list>
                <StringID><id>30</id><str>Last</str></StringID>
                <StringID><id>10</id><str> First </str><data> Kept </data></StringID>
                <StringID><id>10</id><str>Duplicate</str><data>Ignored</data></StringID>
              </list></stageName>
              <worldsEndTagName><list>
                <StringID><id>4</id><str>Tag</str></StringID>
                <StringID><id>invalid</id><str>Ignored</str></StringID>
              </list></worldsEndTagName>
              <releaseTagName><id>5</id><str>Release</str></releaseTagName>
            </MusicData>
            """);
        WriteXml("A000/stage/Stage.xml", """
            <StageData><notesFieldLine><list>
              <StringID><id>9</id><str>Nine</str><data>9-data</data></StringID>
              <StringID><id>7</id><str>Seven</str></StringID>
            </list></notesFieldLine></StageData>
            """);

        WriteXml("A000/ddsFieldWall/ddsFieldWall9/DDSFieldWall.xml", """
            <DDSFieldWallData><name><id>9</id><str>Wall</str></name></DDSFieldWallData>
            """);

        var result = await AssetDictionary.CollectAsync(_root, TestContext.Current.CancellationToken);

        Assert.Equal([(9, "Wall", "")], Values(result[AssetType.FieldWallNames]));
        Assert.Equal([(2, "Genre", "Extra")], Values(result[AssetType.GenreNames]));
        Assert.Equal([(10, "First", "Kept"), (30, "Last", "")], Values(result[AssetType.StageNames]));
        Assert.Equal([(4, "Tag", "")], Values(result[AssetType.WeTagNames]));
        Assert.Equal([(5, "Release", "")], Values(result[AssetType.ReleaseTagNames]));
        Assert.Equal([(7, "Seven", ""), (9, "Nine", "9-data")], Values(result[AssetType.FieldLines]));
    }

    [Fact]
    public async Task CollectManyAsync_PreservesSpecMergeOrderWhenFilenamesRepeatNonconsecutively()
    {
        WriteXml("A000/Music.xml", """
            <MusicData>
              <stageName><id>7</id><str>Stage</str></stageName>
              <genreNames><id>7</id><str>Music genre</str><data>Later</data></genreNames>
            </MusicData>
            """);
        WriteXml("A000/Other.xml", """
            <MusicData><genreNames><id>7</id><str>Other genre</str><data>Earlier</data></genreNames></MusicData>
            """);

        var result = await AssetDictionary.CollectManyAsync(_root,
            [("Music.xml", AssetType.StageNames), ("Other.xml", AssetType.GenreNames),
                ("Music.xml", AssetType.GenreNames)],
            TestContext.Current.CancellationToken);

        // SortedSet.UnionWith selects the later value for these equally sized sets.
        // Merging filename groups instead of caller order would select "Other genre".
        Assert.Equal([(7, "Music genre", "Later")], Values(result[AssetType.GenreNames]));
        Assert.Equal([(7, "Stage", "")], Values(result[AssetType.StageNames]));
        Assert.Empty(result[AssetType.FieldLines]);
    }

    [Theory]
    [InlineData("Music?.xml", 2)]
    [InlineData("*.xml", 3)]
    public async Task CollectManyAsync_AcceptsFilenameSearchPatterns(string pattern, int expectedCount)
    {
        WriteXml("A000/Music1.xml", GenreXml(1));
        WriteXml("A000/nested/Music2.xml", GenreXml(2));
        WriteXml("A000/Other.xml", GenreXml(3));

        var result = await AssetDictionary.CollectManyAsync(_root, [(pattern, AssetType.GenreNames)],
            TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(1, expectedCount), result[AssetType.GenreNames].Select(entry => entry.Id));
    }

    [Fact]
    public async Task CollectManyAsync_OverlappingPatternsCollectAllMatchingEntries()
    {
        WriteXml("A000/Preferred.xml", """
            <MusicData><genreNames><id>1</id><str>Preferred</str></genreNames></MusicData>
            """);
        WriteXml("A000/Other.xml", """
            <MusicData><genreNames><id>2</id><str>Additional</str></genreNames></MusicData>
            """);

        var result = await AssetDictionary.CollectManyAsync(_root,
            [("Preferred.xml", AssetType.GenreNames), ("*.xml", AssetType.GenreNames)],
            TestContext.Current.CancellationToken);

        Assert.Equal([(1, "Preferred", ""), (2, "Additional", "")], Values(result[AssetType.GenreNames]));
    }

    [Fact]
    public async Task CollectAsync_SkipsMalformedAndUnreadableFiles()
    {
        WriteXml("A000/valid/Music.xml", GenreXml(1));
        WriteXml("A000/malformed/Music.xml", "<MusicData><genreNames>");
        var lockedPath = WriteXml("A000/locked/Music.xml", GenreXml(2));
        using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await AssetDictionary.CollectAsync(_root, TestContext.Current.CancellationToken);

        Assert.Equal([(1, "Genre 1", "")], Values(result[AssetType.GenreNames]));
        Assert.Empty(result[AssetType.StageNames]);
    }

    [Theory]
    [InlineData("A000", true)]
    [InlineData("A300", true)]
    [InlineData("A301", false)]
    [InlineData("a001", false)]
    [InlineData("A00", false)]
    [InlineData("B001", false)]
    public async Task CollectAsync_OnlyDiscoversAllowedAssetFolders(string folder, bool expected)
    {
        WriteXml($"wrapper/{folder}/music/Music.xml", GenreXml(1));
        WriteXml("Music.xml", GenreXml(2));

        var result = await AssetDictionary.CollectAsync(_root, TestContext.Current.CancellationToken);

        Assert.Equal(expected ? [1] : Array.Empty<int>(), result[AssetType.GenreNames].Select(entry => entry.Id));
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public async Task CollectAsync_PreservesAssetRootDiscoveryDepthLimit(int wrapperCount, bool expected)
    {
        var wrappers = Path.Combine(Enumerable.Repeat("wrapper", wrapperCount).ToArray());
        WriteXml(Path.Combine(wrappers, "A001", "Music.xml"), GenreXml(1));

        var result = await AssetDictionary.CollectAsync(_root, TestContext.Current.CancellationToken);

        Assert.Equal(expected ? [1] : Array.Empty<int>(), result[AssetType.GenreNames].Select(entry => entry.Id));
    }

    [Fact]
    public async Task CollectAsync_ScansDeepFilesWhenRootIsAlreadyAnAllowedFolder()
    {
        var nested = Path.Combine(Enumerable.Repeat("nested", 10).ToArray());
        WriteXml(Path.Combine("A001", nested, "Music.xml"), GenreXml(1));

        var result = await AssetDictionary.CollectAsync(Path.Combine(_root, "A001"),
            TestContext.Current.CancellationToken);

        Assert.Equal([1], result[AssetType.GenreNames].Select(entry => entry.Id));
    }

    [Fact]
    public async Task CollectManyAsync_EmptySpecsDoNotEnumerateTheRoot()
    {
        var result = await AssetDictionary.CollectManyAsync(Path.Combine(_root, "A000"), [],
            TestContext.Current.CancellationToken);

        Assert.Equal(Enum.GetValues<AssetType>(), result.Keys);
        Assert.All(result.Values, entries => Assert.Empty(entries));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task CollectAsync_ObservesCancellationBeforeReading()
    {
        WriteXml("A000/Music.xml", GenreXml(1));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AssetDictionary.CollectAsync(_root, cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private string WriteXml(string relativePath, string xml)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, xml);
        return path;
    }

    private static string GenreXml(int id) =>
        $"<MusicData><genreNames><id>{id}</id><str>Genre {id}</str></genreNames></MusicData>";

    private static (int Id, string Str, string Data)[] Values(IEnumerable<Entry> entries) =>
        entries.Select(entry => (entry.Id, entry.Str, entry.Data)).ToArray();
}
