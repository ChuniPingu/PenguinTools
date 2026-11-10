using System.Xml.Linq;
using PenguinTools.Application;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Metadata;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed partial class PenguinToolsApplicationTests
{
    [Theory]
    [InlineData("song", MsgKeys.Error_Song_id_invalid)]
    [InlineData("event", MsgKeys.Error_Event_id_invalid)]
    [InlineData("difficulty", MsgKeys.Error_Difficulty_id_invalid)]
    [InlineData("we-difficulty", MsgKeys.Error_We_difficulty_id_invalid)]
    [InlineData("wall", MsgKeys.Error_Wall_id_invalid)]
    [InlineData("difficulty", MsgKeys.Error_Difficulty_id_invalid, 6)]
    [InlineData("we-difficulty", MsgKeys.Error_We_difficulty_id_invalid, 2)]
    [InlineData("we-difficulty", MsgKeys.Error_We_difficulty_id_invalid, 10)]
    public async Task MusicBuild_InvalidExplicitIdsFailBeforeWriting(string field, string key, int id = -1)
    {
        var root = Directory.CreateTempSubdirectory("penguintools-ids-");
        using var store = new TrackingAssetStore(root.FullName);
        using var application = CreateInjectedApplication(root.FullName, store);
        try
        {
            var input = await WriteChartFixtureAsync(root.FullName);
            var output = Path.Combine(root.FullName, "output");
            var settings = field switch
            {
                "song" => new MusicBuildOverrides(SongId: id),
                "event" => new MusicBuildOverrides(UnlockEventId: id),
                "difficulty" => new MusicBuildOverrides(DifficultyId: id),
                "we-difficulty" => new MusicBuildOverrides(WeDifficultyId: id),
                _ => new MusicBuildOverrides()
            };
            var result = await application.BuildMusicAsync(new MusicBuildRequest(input, output,
                Overrides: settings, Wall: field == "wall" ? new WallOverrides(WallId: id) : null),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == key);
            Assert.False(Directory.Exists(output));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task MusicBuild_ZeroIdsAndUnspecifiedReferencesRemainValid()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-ids-");
        using var store = new TrackingAssetStore(root.FullName);
        using var application = CreateInjectedApplication(root.FullName, store);
        try
        {
            var input = await WriteChartFixtureAsync(root.FullName);
            var output = Path.Combine(root.FullName, "output");
            var result = await application.BuildMusicAsync(new MusicBuildRequest(input, output,
                Overrides: new MusicBuildOverrides(SongId: 0, StageEntryId: 0, GenreId: 0)),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.ToString());
            var xml = XDocument.Load(Path.Combine(output, "music0000", "Music.xml")).Root!;
            Assert.Equal("0", xml.Element("name")!.Element("id")!.Value);
            Assert.Equal("0", xml.Element("stageName")!.Element("id")!.Value);
            Assert.Equal("-1", xml.Element("worldsEndTagName")!.Element("id")!.Value);
            Assert.True(File.Exists(Path.Combine(output, "cueFile000000", "CueFile.xml")));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task MusicBuild_PreservesSignedResourceReferences()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-ids-");
        using var store = new TrackingAssetStore(root.FullName);
        using var application = CreateInjectedApplication(root.FullName, store);
        try
        {
            var input = await WriteChartFixtureAsync(root.FullName);
            var output = Path.Combine(root.FullName, "output");
            var result = await application.BuildMusicAsync(new MusicBuildRequest(input, output,
                Overrides: new MusicBuildOverrides(StageEntryId: -2, GenreId: -3, WeTagId: -4)),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.ToString());
            var xml = XDocument.Load(Path.Combine(output, "music1000", "Music.xml")).Root!;
            Assert.Equal("-2", xml.Element("stageName")!.Element("id")!.Value);
            Assert.Equal("-3", xml.Element("genreNames")!.Element("list")!.Element("StringID")!.Element("id")!.Value);
            Assert.Equal("-4", xml.Element("worldsEndTagName")!.Element("id")!.Value);
        }
        finally { root.Delete(true); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(-2, true)]
    [InlineData(int.MaxValue, true)]
    public async Task StageBuild_PreservesSignedIdAndWarnsForUnknownFieldLines(int fieldLine, bool warning)
    {
        var root = Directory.CreateTempSubdirectory("penguintools-ids-");
        using var store = new TrackingAssetStore(root.FullName);
        using var catalog = new MemoryStream("""{"notesFieldLine":[{"id":0,"str":"Orange"}]}"""u8.ToArray());
        using var application = CreateInjectedApplication(root.FullName, store, new AssetManager(catalog));
        try
        {
            var input = await WriteChartFixtureAsync(root.FullName);
            var image = Path.Combine(root.FullName, "jacket.png");
            var settings = new StageOverrides(BackgroundPath: image, StageId: -2, NoteFieldLaneId: fieldLine);
            var chartOutput = Path.Combine(root.FullName, "chart-stage");
            var fileOutput = Path.Combine(root.FullName, "file-stage");
            var ct = TestContext.Current.CancellationToken;
            var fromChart = await application.BuildStageAsync(new StageBuildRequest(input, chartOutput, settings), ct);
            var fromFile = await application.BuildStageFilesAsync(new StageFilesBuildRequest(image, fileOutput, settings), ct);

            foreach (var result in new[] { fromChart, fromFile })
            {
                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal(warning ? 1 : 0, result.Diagnostics.Diagnostics.Count(d =>
                    d.Message.Key == MsgKeys.Warn_Field_line_unknown));
            }
            foreach (var output in new[] { chartOutput, fileOutput })
            {
                var xml = XDocument.Load(Path.Combine(output, "stage-000002", "Stage.xml")).Root!;
                Assert.Equal("-2", xml.Element("name")!.Element("id")!.Value);
                Assert.Equal(fieldLine, (int)xml.Element("notesFieldLine")!.Element("id")!);
            }
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task SongIdUpperBound_RejectsOverridesAndAllowsAutomaticResourcesAtTheLimit()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-ids-");
        using var store = new TrackingAssetStore(root.FullName);
        using var application = CreateInjectedApplication(root.FullName, store);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var input = await WriteChartFixtureAsync(root.FullName);
            var text = await File.ReadAllTextAsync(input, ct);
            var output = Path.Combine(root.FullName, "output");
            var options = new MusicBuildOverrides(DifficultyId: (int)Difficulty.Ultima);
            var stageSettings = new StageOverrides(BackgroundPath: Path.Combine(root.FullName, "jacket.png"));
            var invalidOptions = options with { SongId = 2_000_000_001, UnlockEventId = 0 };
            var invalid = await application.BuildMusicAsync(new MusicBuildRequest(input, output,
                Overrides: invalidOptions, Stage: stageSettings with { StageId = 0 }), cancellationToken: ct);
            Assert.False(invalid.Succeeded);
            Assert.Contains(invalid.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Song_id_invalid);
            Assert.False(Directory.Exists(output));

            await File.WriteAllTextAsync(input, text.Replace("@SONGID\t1000", "@SONGID\t2000000001"), ct);
            var stage = await application.BuildStageAsync(new StageBuildRequest(input, output,
                stageSettings with { StageId = 0 }), ct);
            Assert.False(stage.Succeeded);
            Assert.Contains(stage.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Song_id_invalid);
            Assert.False(Directory.Exists(output));

            await File.WriteAllTextAsync(input, text.Replace("@SONGID\t1000", "@SONGID\t2000000000"), ct);
            var valid = await application.BuildMusicAsync(new MusicBuildRequest(input, output,
                Overrides: options, Stage: stageSettings), cancellationToken: ct);
            Assert.True(valid.Succeeded, valid.ToString());
            Assert.True(File.Exists(Path.Combine(output, "event2001000000", "Event.xml")));
            Assert.True(File.Exists(Path.Combine(output, "stage2001000000", "Stage.xml")));
        }
        finally { root.Delete(true); }
    }
}
