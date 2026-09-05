using System.Xml.Linq;
using PenguinTools.Assets;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using PenguinTools.Infrastructure;
using PenguinTools.Workflow;
using Xunit;
using UmgrChart = PenguinTools.Chart.Models.umgr.Chart;

namespace PenguinTools.Tests.Workflow;

public sealed class OptionExporterReleaseTagTests
{
    [Fact]
    public async Task ExportAsync_WritesConfiguredReleaseTagToEachMusicXml()
    {
        var workPath = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var outputPaths = ExportOutputPaths.FromOptionDirectory(Path.Combine(workPath, "AXXX"));
        var settings = new OptionExportSettings(
            ConvertChart: false,
            ConvertJacket: true,
            ConvertAudio: false,
            ConvertBackground: false,
            CustomReleaseTagXml: false,
            SelectedReleaseTagId: ReleaseTag.DefaultId,
            CustomReleaseTagId: ReleaseTag.CustomDefaultId,
            CustomReleaseTagTitleName: ReleaseTag.CustomDefaultTitleName,
            CustomGenre: true,
            SelectedGenreId: GenreDefaults.SelectedDefaultId,
            CustomGenreId: GenreDefaults.CustomDefaultId,
            CustomGenreName: GenreDefaults.CustomDefaultName,
            OverrideChartGenre: true,
            GenerateEventXml: false,
            UltimaEventId: 1000001,
            WeEventId: 1000002,
            BatchSize: 1);
        var meta = new Meta
        {
            Id = 4321,
            Title = "Test Song",
            SortName = "Test Song",
            Artist = "Tester",
            Difficulty = Difficulty.Master,
            FilePath = Path.Combine(workPath, "chart.ugc")
        };
        var book = new OptionBookSnapshot(
            meta,
            false,
            null,
            meta.NotesFieldLine,
            meta.Stage,
            meta.Title,
            new Dictionary<Difficulty, OptionDifficultySnapshot>
            {
                [Difficulty.Master] = new(Difficulty.Master, 4321, new UmgrChart(), meta)
            });
        using var assetStore = new DummyAssetStore(workPath);
        var context = new MusicExportContext(
            TestAssets.Load(),
            TestMediaTool.Instance,
            assetStore,
            DummyInfrastructureAssetProvider.Instance);

        try
        {
            var result = await OptionExporter.ExportAsync(context, settings, outputPaths, [book], workPath,
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var musicXmlPath = Path.Combine(outputPaths.MusicFolder, "music4321", "Music.xml");
            var releaseTagName = XDocument.Load(musicXmlPath).Root?.Element("releaseTagName");
            Assert.NotNull(releaseTagName);
            Assert.Equal("0", releaseTagName.Element("id")?.Value);
            Assert.Equal("CHUNITHM", releaseTagName.Element("str")?.Value);
            Assert.Equal(string.Empty, releaseTagName.Element("data")?.Value);
            Assert.False(Directory.Exists(outputPaths.ReleaseTagPath));
        }
        finally
        {
            if (Directory.Exists(workPath)) Directory.Delete(workPath, true);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenCustomReleaseTagEnabled_WritesCustomTagToMusicAndXml()
    {
        var workPath = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var outputPaths = ExportOutputPaths.FromOptionDirectory(Path.Combine(workPath, "AXXX"));
        var settings = new OptionExportSettings(
            ConvertChart: false,
            ConvertJacket: true,
            ConvertAudio: false,
            ConvertBackground: false,
            CustomReleaseTagXml: true,
            SelectedReleaseTagId: ReleaseTag.DefaultId,
            CustomReleaseTagId: 99,
            CustomReleaseTagTitleName: "自制譜",
            CustomGenre: true,
            SelectedGenreId: GenreDefaults.SelectedDefaultId,
            CustomGenreId: GenreDefaults.CustomDefaultId,
            CustomGenreName: GenreDefaults.CustomDefaultName,
            OverrideChartGenre: true,
            GenerateEventXml: false,
            UltimaEventId: 1000001,
            WeEventId: 1000002,
            BatchSize: 1);
        var meta = new Meta
        {
            Id = 4321,
            Title = "Test Song",
            SortName = "Test Song",
            Artist = "Tester",
            Difficulty = Difficulty.Master,
            FilePath = Path.Combine(workPath, "chart.ugc")
        };
        var book = new OptionBookSnapshot(
            meta,
            false,
            null,
            meta.NotesFieldLine,
            meta.Stage,
            meta.Title,
            new Dictionary<Difficulty, OptionDifficultySnapshot>
            {
                [Difficulty.Master] = new(Difficulty.Master, 4321, new UmgrChart(), meta)
            });
        using var assetStore = new DummyAssetStore(workPath);
        var context = new MusicExportContext(
            TestAssets.Load(),
            TestMediaTool.Instance,
            assetStore,
            DummyInfrastructureAssetProvider.Instance);

        try
        {
            var result = await OptionExporter.ExportAsync(context, settings, outputPaths, [book], workPath,
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var musicXmlPath = Path.Combine(outputPaths.MusicFolder, "music4321", "Music.xml");
            var releaseTagName = XDocument.Load(musicXmlPath).Root?.Element("releaseTagName");
            Assert.NotNull(releaseTagName);
            Assert.Equal("99", releaseTagName.Element("id")?.Value);
            Assert.Equal("自制譜", releaseTagName.Element("str")?.Value);

            var customXmlPath = Path.Combine(outputPaths.ReleaseTagPath, "releaseTag000099", "ReleaseTag.xml");
            Assert.True(File.Exists(customXmlPath));
            var customXml = XDocument.Load(customXmlPath).Root;
            Assert.Equal("99", customXml?.Element("name")?.Element("id")?.Value);
            Assert.Equal("自制譜", customXml?.Element("titleName")?.Value);
        }
        finally
        {
            if (Directory.Exists(workPath)) Directory.Delete(workPath, true);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenCustomGenreEnabled_OverridesChartGenreInMusicXml()
    {
        var workPath = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var outputPaths = ExportOutputPaths.FromOptionDirectory(Path.Combine(workPath, "AXXX"));
        var settings = new OptionExportSettings(
            ConvertChart: false,
            ConvertJacket: true,
            ConvertAudio: false,
            ConvertBackground: false,
            CustomReleaseTagXml: false,
            SelectedReleaseTagId: ReleaseTag.DefaultId,
            CustomReleaseTagId: ReleaseTag.CustomDefaultId,
            CustomReleaseTagTitleName: ReleaseTag.CustomDefaultTitleName,
            CustomGenre: true,
            SelectedGenreId: GenreDefaults.SelectedDefaultId,
            CustomGenreId: GenreDefaults.CustomDefaultId,
            CustomGenreName: GenreDefaults.CustomDefaultName,
            OverrideChartGenre: true,
            GenerateEventXml: false,
            UltimaEventId: 1000001,
            WeEventId: 1000002,
            BatchSize: 1);
        var meta = new Meta
        {
            Id = 4321,
            Title = "Test Song",
            SortName = "Test Song",
            Artist = "Tester",
            Genre = new Entry(5, "ORIGINAL"),
            Difficulty = Difficulty.Master,
            FilePath = Path.Combine(workPath, "chart.ugc")
        };
        var book = new OptionBookSnapshot(
            meta,
            false,
            null,
            meta.NotesFieldLine,
            meta.Stage,
            meta.Title,
            new Dictionary<Difficulty, OptionDifficultySnapshot>
            {
                [Difficulty.Master] = new(Difficulty.Master, 4321, new UmgrChart(), meta)
            });
        using var assetStore = new DummyAssetStore(workPath);
        var context = new MusicExportContext(
            TestAssets.Load(),
            TestMediaTool.Instance,
            assetStore,
            DummyInfrastructureAssetProvider.Instance);

        try
        {
            var result = await OptionExporter.ExportAsync(context, settings, outputPaths, [book], workPath,
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var musicXmlPath = Path.Combine(outputPaths.MusicFolder, "music4321", "Music.xml");
            var genreNames = XDocument.Load(musicXmlPath).Root?.Element("genreNames")?.Element("list")
                ?.Element("StringID");
            Assert.NotNull(genreNames);
            Assert.Equal("1000", genreNames.Element("id")?.Value);
            Assert.Equal("自制譜", genreNames.Element("str")?.Value);
        }
        finally
        {
            if (Directory.Exists(workPath)) Directory.Delete(workPath, true);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenCustomGenreDisabled_WritesSelectedAssetGenreToMusicXml()
    {
        var workPath = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var outputPaths = ExportOutputPaths.FromOptionDirectory(Path.Combine(workPath, "AXXX"));
        var settings = new OptionExportSettings(
            ConvertChart: false,
            ConvertJacket: true,
            ConvertAudio: false,
            ConvertBackground: false,
            CustomReleaseTagXml: false,
            SelectedReleaseTagId: ReleaseTag.DefaultId,
            CustomReleaseTagId: ReleaseTag.CustomDefaultId,
            CustomReleaseTagTitleName: ReleaseTag.CustomDefaultTitleName,
            CustomGenre: false,
            SelectedGenreId: 5,
            CustomGenreId: GenreDefaults.CustomDefaultId,
            CustomGenreName: GenreDefaults.CustomDefaultName,
            OverrideChartGenre: true,
            GenerateEventXml: false,
            UltimaEventId: 1000001,
            WeEventId: 1000002,
            BatchSize: 1);
        var meta = new Meta
        {
            Id = 4321,
            Title = "Test Song",
            SortName = "Test Song",
            Artist = "Tester",
            Genre = new Entry(GenreDefaults.CustomDefaultId, GenreDefaults.CustomDefaultName),
            Difficulty = Difficulty.Master,
            FilePath = Path.Combine(workPath, "chart.ugc")
        };
        var book = new OptionBookSnapshot(
            meta,
            false,
            null,
            meta.NotesFieldLine,
            meta.Stage,
            meta.Title,
            new Dictionary<Difficulty, OptionDifficultySnapshot>
            {
                [Difficulty.Master] = new(Difficulty.Master, 4321, new UmgrChart(), meta)
            });
        using var assetStore = new DummyAssetStore(workPath);
        var context = new MusicExportContext(
            TestAssets.Load(),
            TestMediaTool.Instance,
            assetStore,
            DummyInfrastructureAssetProvider.Instance);

        try
        {
            var result = await OptionExporter.ExportAsync(context, settings, outputPaths, [book], workPath,
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var musicXmlPath = Path.Combine(outputPaths.MusicFolder, "music4321", "Music.xml");
            var genreNames = XDocument.Load(musicXmlPath).Root?.Element("genreNames")?.Element("list")
                ?.Element("StringID");
            Assert.NotNull(genreNames);
            Assert.Equal("5", genreNames.Element("id")?.Value);
            var expectedName = context.Assets.GenreNames.FirstOrDefault(entry => entry.Id == 5)?.Str ?? string.Empty;
            Assert.Equal(expectedName, genreNames.Element("str")?.Value);
            Assert.NotEqual(GenreDefaults.CustomDefaultName, genreNames.Element("str")?.Value);
        }
        finally
        {
            if (Directory.Exists(workPath)) Directory.Delete(workPath, true);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenOverrideChartGenreDisabled_KeepsChartGenre()
    {
        var workPath = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var outputPaths = ExportOutputPaths.FromOptionDirectory(Path.Combine(workPath, "AXXX"));
        var settings = new OptionExportSettings(
            ConvertChart: false,
            ConvertJacket: true,
            ConvertAudio: false,
            ConvertBackground: false,
            CustomReleaseTagXml: false,
            SelectedReleaseTagId: ReleaseTag.DefaultId,
            CustomReleaseTagId: ReleaseTag.CustomDefaultId,
            CustomReleaseTagTitleName: ReleaseTag.CustomDefaultTitleName,
            CustomGenre: true,
            SelectedGenreId: GenreDefaults.SelectedDefaultId,
            CustomGenreId: GenreDefaults.CustomDefaultId,
            CustomGenreName: GenreDefaults.CustomDefaultName,
            OverrideChartGenre: false,
            GenerateEventXml: false,
            UltimaEventId: 1000001,
            WeEventId: 1000002,
            BatchSize: 1);
        var meta = new Meta
        {
            Id = 4321,
            Title = "Test Song",
            SortName = "Test Song",
            Artist = "Tester",
            Genre = new Entry(5, "ORIGINAL"),
            Difficulty = Difficulty.Master,
            FilePath = Path.Combine(workPath, "chart.ugc")
        };
        var book = new OptionBookSnapshot(
            meta,
            false,
            null,
            meta.NotesFieldLine,
            meta.Stage,
            meta.Title,
            new Dictionary<Difficulty, OptionDifficultySnapshot>
            {
                [Difficulty.Master] = new(Difficulty.Master, 4321, new UmgrChart(), meta)
            });
        using var assetStore = new DummyAssetStore(workPath);
        var context = new MusicExportContext(
            TestAssets.Load(),
            TestMediaTool.Instance,
            assetStore,
            DummyInfrastructureAssetProvider.Instance);

        try
        {
            var result = await OptionExporter.ExportAsync(context, settings, outputPaths, [book], workPath,
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var musicXmlPath = Path.Combine(outputPaths.MusicFolder, "music4321", "Music.xml");
            var genreNames = XDocument.Load(musicXmlPath).Root?.Element("genreNames")?.Element("list")
                ?.Element("StringID");
            Assert.NotNull(genreNames);
            Assert.Equal("5", genreNames.Element("id")?.Value);
            Assert.Equal("ORIGINAL", genreNames.Element("str")?.Value);
        }
        finally
        {
            if (Directory.Exists(workPath)) Directory.Delete(workPath, true);
        }
    }

    private sealed class DummyAssetStore(string tempWorkPath) : IAssetStore
    {
        public string AssetDirectory { get; } = tempWorkPath;
        public string TempWorkPath { get; } = tempWorkPath;

        public bool HasAsset(string assetName)
        {
            return false;
        }

        public string GetAssetPath(string assetName)
        {
            return GetTempPath(assetName);
        }

        public string GetTempPath(string fileName)
        {
            Directory.CreateDirectory(TempWorkPath);
            return Path.Combine(TempWorkPath, Core.IO.TempFileNames.MakeUnique(fileName));
        }

        public Stream OpenRead(string assetName)
        {
            throw new FileNotFoundException(assetName);
        }

        public void Dispose()
        {
        }
    }

    private sealed class DummyInfrastructureAssetProvider : IInfrastructureAssetProvider
    {
        public static readonly DummyInfrastructureAssetProvider Instance = new();

        public string GetPath(InfrastructureAsset asset)
        {
            return string.Empty;
        }
    }
}