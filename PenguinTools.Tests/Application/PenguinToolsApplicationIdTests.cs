using System.Xml.Linq;
using PenguinTools.Application;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed partial class PenguinToolsApplicationTests
{
    [Theory]
    [InlineData("song", MsgKeys.Error_Song_id_invalid)]
    [InlineData("difficulty", MsgKeys.Error_Difficulty_id_invalid)]
    [InlineData("difficulty", MsgKeys.Error_Difficulty_id_invalid, 6)]
    public async Task ChartConvert_InvalidExplicitIdsFailBeforeWriting(string field, string key, int id = -1)
    {
        var root = Directory.CreateTempSubdirectory("penguintools-ids-");
        using var store = new TrackingAssetStore(root.FullName);
        using var application = CreateInjectedApplication(root.FullName, store);
        try
        {
            var input = await WriteChartFixtureAsync(root.FullName);
            var output = Path.Combine(root.FullName, "output", "chart.c2s");
            var settings = field == "song"
                ? new ChartConvertOverrides(SongId: id)
                : new ChartConvertOverrides(DifficultyId: id);
            var result = await application.ConvertChartAsync(new ChartConvertRequest(input, output, settings),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == key);
            Assert.False(File.Exists(output));
        }
        finally { root.Delete(true); }
    }

    [Theory]
    [InlineData(0, 0, 0, -1)]
    [InlineData(1000, -2, -3, -4)]
    public async Task OptionBuild_PreservesZeroIdsAndSignedResourceReferences(
        int songId, int stageId, int genreId, int weTagId)
    {
        var root = Directory.CreateTempSubdirectory("penguintools-ids-");
        using var store = new TrackingAssetStore(root.FullName);
        using var application = CreateInjectedApplication(root.FullName, store);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var input = await WriteChartFixtureAsync(root.FullName);
            var text = await File.ReadAllTextAsync(input, ct);
            await File.WriteAllTextAsync(input, text.Replace("@SONGID\t1000", $"@SONGID\t{songId}") +
                $"@CMT\t#meta stage {stageId} Stage\n@CMT\t#meta genre {genreId} Genre\n" +
                (weTagId == -1 ? "" : $"@CMT\t#meta wetag {weTagId} Tag\n"), ct);
            var output = Path.Combine(root.FullName, "A123");
            var settings = new OptionBuildOverrides(OptionName: "A123", OverrideChartGenre: false);
            var result = await application.BuildOptionAsync(new OptionBuildRequest(root.FullName, output,
                SkipConfig: true, Overrides: settings), cancellationToken: ct);

            Assert.True(result.Succeeded, result.ToString());
            var xml = XDocument.Load(Path.Combine(output, "music", $"music{songId:0000}", "Music.xml")).Root!;
            Assert.Equal(songId, (int)xml.Element("name")!.Element("id")!);
            Assert.Equal(stageId, (int)xml.Element("stageName")!.Element("id")!);
            Assert.Equal(genreId, (int)xml.Element("genreNames")!.Element("list")!.Element("StringID")!.Element("id")!);
            Assert.Equal(weTagId, (int)xml.Element("worldsEndTagName")!.Element("id")!);
            Assert.True(File.Exists(Path.Combine(output, "cueFile", $"cueFile{songId:000000}", "CueFile.xml")));
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
    public async Task SongIdUpperBound_RejectsOverridesAndAllowsAutomaticStageAtTheLimit()
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
            var stageSettings = new StageOverrides(BackgroundPath: Path.Combine(root.FullName, "jacket.png"));
            var invalid = await application.ConvertChartAsync(new ChartConvertRequest(input,
                Path.Combine(output, "chart.c2s"), new ChartConvertOverrides(SongId: 2_000_000_001)),
                cancellationToken: ct);
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
            var valid = await application.BuildStageAsync(new StageBuildRequest(input, output, stageSettings), ct);
            Assert.True(valid.Succeeded, valid.ToString());
            Assert.True(File.Exists(Path.Combine(output, "stage2001000000", "Stage.xml")));
        }
        finally { root.Delete(true); }
    }
}
