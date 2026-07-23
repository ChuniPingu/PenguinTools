using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.Assets;
using PenguinTools.Core;
using PenguinTools.Infrastructure;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed class PenguinToolsApplicationTests
{
    [Fact]
    public async Task DefaultRuntime_ProvidesInfo_AndCanBeDisposed()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var result = await application.GetInfoAsync(new ApplicationInfoRequest(),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.TempWorkPath);
        Assert.NotEmpty(result.Value.InfrastructureAssetsPath);
    }

    [Fact]
    public async Task ChartOperations_ReturnDiagnostics_ForMissingInput()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        var inspect = await application.InspectChartAsync(new ChartInspectRequest(missing), cancellationToken: ct);
        var convert = await application.ConvertChartAsync(new ChartConvertRequest(missing, missing + ".c2s"),
            cancellationToken: ct);
        Assert.False(inspect.Succeeded);
        Assert.True(inspect.Diagnostics.HasError);
        Assert.False(convert.Succeeded);
        Assert.True(convert.Diagnostics.HasError);
    }

    [Fact]
    public async Task OptionOperations_ValidateBeforeStartingWork()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var ct = TestContext.Current.CancellationToken;
        var scan = await application.ScanOptionAsync(new OptionScanRequest(missing), cancellationToken: ct);
        var build = await application.BuildOptionAsync(new OptionBuildRequest(missing, missing), cancellationToken: ct);
        Assert.False(scan.Succeeded);
        Assert.False(build.Succeeded);
    }

    [Fact]
    public async Task OptionScan_SurfacesMissingSongId_OnTopLevelAndUnmatched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var chartPath = Path.Combine(directory, "graduation.ugc");
        await File.WriteAllTextAsync(chartPath,
            "@VER\t8\n@TICKS\t480\n@TITLE\tgraduation\n@DESIGN\tDesigner\n@DIFF\t3\n" +
            "@SONGID\tgraduation\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n",
            TestContext.Current.CancellationToken);
        using var application = PenguinToolsApplication.CreateDefault();
        try
        {
            var result = await application.ScanOptionAsync(
                new OptionScanRequest(directory, [ChartFormat.Ugc]),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value!.Books);
            Assert.Contains(result.Value.UnmatchedDiagnostics,
                d => d.Message.Key == MsgKeys.Error_File_ignored_due_to_id_missing);
            Assert.Contains(result.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Error_File_ignored_due_to_id_missing);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OptionScan_IncludesAutoDiscoveredConfig()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "options.json");
        var document = new OptionDocument
        {
            OptionName = "TEST",
            ChartFileDiscovery = [ChartFileFormat.Ugc, ChartFileFormat.Sus],
            ConvertChart = false,
            ConvertAudio = false,
            ConvertJacket = true,
            ConvertBackground = false
        };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(document, OptionDocumentJson.Default),
            TestContext.Current.CancellationToken);
        try
        {
            var result = await application.ScanOptionAsync(new OptionScanRequest(directory),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Value);
            Assert.Equal(configPath, result.Value!.ConfigPath);
            Assert.NotNull(result.Value.Config);
            Assert.Equal("TEST", result.Value.Config!.OptionName);
            Assert.Equal(["ugc", "sus"], result.Value.Config.ChartFileDiscovery);
            Assert.Equal(["mgxc", "ugc", "sus"], result.Value.ChartFileDiscovery);
            Assert.False(result.Value.Config.ConvertChart);
            Assert.False(result.Value.Config.ConvertAudio);
            Assert.True(result.Value.Config.ConvertJacket);
            Assert.False(result.Value.Config.ConvertBackground);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OptionScan_SaveConfig_PersistsDiscoveryOrderBeforeScanning()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "options.json");
        var document = new OptionDocument { OptionName = "TEST", ConvertAudio = false };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(document, OptionDocumentJson.Default),
            TestContext.Current.CancellationToken);
        try
        {
            var result = await application.ScanOptionAsync(new OptionScanRequest(
                    directory, [ChartFormat.Ugc, ChartFormat.Mgxc], 4, SaveConfig: true),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded);
            var saved = JsonSerializer.Deserialize<OptionDocument>(
                await File.ReadAllTextAsync(configPath, TestContext.Current.CancellationToken),
                OptionDocumentJson.Default);
            Assert.NotNull(saved);
            Assert.Equal([ChartFileFormat.Ugc, ChartFileFormat.Mgxc], saved.ChartFileDiscovery);
            Assert.Equal(4, saved.BatchSize);
            Assert.False(saved.ConvertAudio);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OptionBuild_RejectsConflictingConfigSettings()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var result = await application.BuildOptionAsync(new OptionBuildRequest(
                    directory, directory, Path.Combine(directory, "options.json"), true),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.True(result.Diagnostics.HasError);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OptionBuild_AutoLoadsConfig_AppliesOverrides_AndDoesNotPersistOnFailure()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "options.json");
        var document = new OptionDocument { OptionName = "TEST", BatchSize = 8 };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(document, OptionDocumentJson.Default),
            TestContext.Current.CancellationToken);
        var original = await File.ReadAllTextAsync(configPath, TestContext.Current.CancellationToken);
        try
        {
            var result = await application.BuildOptionAsync(new OptionBuildRequest(directory, directory,
                    Overrides: new OptionBuildOverrides(BatchSize: 0)),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics,
                diagnostic => diagnostic.Message.Key == MsgKeys.App_Batch_size_invalid);
            Assert.Equal(original, await File.ReadAllTextAsync(configPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OptionBuild_DoesNotPersistOnFailure_EvenWithSaveConfig()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "options.json");
        var document = new OptionDocument { OptionName = "TEST", BatchSize = 8 };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(document, OptionDocumentJson.Default),
            TestContext.Current.CancellationToken);
        var original = await File.ReadAllTextAsync(configPath, TestContext.Current.CancellationToken);
        try
        {
            var result = await application.BuildOptionAsync(new OptionBuildRequest(directory, directory,
                    SaveConfig: true, Overrides: new OptionBuildOverrides(BatchSize: 0)),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Equal(original, await File.ReadAllTextAsync(configPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OptionBuild_WithoutConfig_RequiresOptionName()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var result = await application.BuildOptionAsync(new OptionBuildRequest(directory, directory,
                SkipConfig: true), cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics,
                diagnostic => diagnostic.Message.Key == MsgKeys.App_Option_name_required);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        using var source = new CancellationTokenSource();
        source.Cancel();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgxc");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.InspectChartAsync(
            new ChartInspectRequest(missing), cancellationToken: source.Token));
    }

    [Fact]
    public async Task MediaAndAssetOperations_ReturnDiagnostics_ForMissingInputs()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgxc");
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var ct = TestContext.Current.CancellationToken;
        Assert.False((await application.BuildMusicAsync(new MusicBuildRequest(missing, output),
            cancellationToken: ct)).Succeeded);
        Assert.False((await application.ConvertJacketAsync(new JacketConvertRequest(missing, output),
            cancellationToken: ct)).Succeeded);
        Assert.False((await application.ConvertAudioAsync(new AudioConvertRequest(missing, output),
            cancellationToken: ct)).Succeeded);
        Assert.False((await application.BuildStageAsync(new StageBuildRequest(missing, output),
            cancellationToken: ct)).Succeeded);
        Assert.False((await application.CollectAssetsAsync(new AssetCollectRequest(output, output),
            cancellationToken: ct)).Succeeded);
    }

    [Fact]
    public async Task ChartConversion_DoesNotReportProgress()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var input = Path.Combine(ChartTestPaths.AssetsDirectory, "Ver seX.mgxc");
        if (!File.Exists(input)) return;

        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "chart.c2s");
        var reports = new List<ProgressReport>();
        try
        {
            var result = await application.ConvertChartAsync(new ChartConvertRequest(input, output),
                new InlineProgress(reports.Add), TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.True(File.Exists(output));
            Assert.Empty(reports);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task JacketConversion_WritesArtifact()
    {
        var input = Path.Combine(ChartTestPaths.AssetsDirectory, "Ver seX.mgxc");
        if (!File.Exists(input))
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "jacket.dds");
        var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var result = await application.ConvertJacketAsync(new JacketConvertRequest(input, output),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.Equal(output, Assert.Single(result.Value!.Artifacts).Path);
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ChartConversion_WritesOutputFile()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var input = Path.Combine(ChartTestPaths.AssetsDirectory, "Ver seX.mgxc");
        if (!File.Exists(input))
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "chart.c2s");
        try
        {
            var result = await application.ConvertChartAsync(new ChartConvertRequest(input, output),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.True(File.Exists(output));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ChartInspect_ExposesConversionMetadata_AndConvertAppliesOverrides()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "chart.ugc");
        var output = Path.Combine(root, "chart.c2s");
        await File.WriteAllTextAsync(input,
            "@VER\t8\n@TICKS\t480\n@TITLE\tTest\n@DESIGN\tOriginal\n@DIFF\t3\n" +
            "@SONGID\t1000\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n",
            TestContext.Current.CancellationToken);
        var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var inspected = await application.InspectChartAsync(new ChartInspectRequest(input),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(inspected.Succeeded);
            Assert.NotNull(inspected.Value);
            Assert.InRange(inspected.Value.Metadata.DifficultyId, 0, 5);
            Assert.NotNull(inspected.Value.Metadata.NotesFieldLine);
            Assert.NotNull(inspected.Value.Metadata.Stage);

            var converted = await application.ConvertChartAsync(new ChartConvertRequest(input, output,
                    new ChartConvertOverrides(4321, "Override Designer", 2, 198.5m, true)),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(converted.Succeeded);
            Assert.Equal(4321, converted.Value!.Chart.SongId);
            Assert.Equal("Override Designer", converted.Value.Chart.Designer);
            Assert.Equal("Expert", converted.Value.Chart.Difficulty);
            Assert.Equal(198.5m, converted.Value.Chart.MainBpm);
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DirectMediaOperations_UseExplicitFilesAndMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var image = Path.Combine(root, "image.png");
        var audio = Path.Combine(root, "audio.mp3");
        await File.WriteAllBytesAsync(image, [1], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(audio, [1], TestContext.Current.CancellationToken);
        var effects = Enumerable.Range(1, 4).Select(index => Path.Combine(root, $"effect{index}.png")).ToArray();
        foreach (var effect in effects)
            await File.WriteAllBytesAsync(effect, [1], TestContext.Current.CancellationToken);
        var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var jacket = await application.ConvertJacketFileAsync(
                new JacketFileConvertRequest(image, Path.Combine(root, "jacket.dds")),
                TestContext.Current.CancellationToken);
            Assert.True(jacket.Succeeded);
            Assert.Equal(image, jacket.Value!.SourcePath);

            var convertedAudio = await application.ConvertAudioFileAsync(new AudioFileConvertRequest(
                    audio, Path.Combine(root, "audio-output"),
                    new AudioFileSettings(1234, 10, 20, -0.1m, true, 120, 4, 4)),
                TestContext.Current.CancellationToken);
            Assert.True(convertedAudio.Succeeded);
            Assert.Equal(3, convertedAudio.Value!.Artifacts.Count);

            var stage = await application.BuildStageFilesAsync(new StageFilesBuildRequest(
                    image, Path.Combine(root, "stage-output"),
                    new StageOverrides(image, effects, 123456, 0, "Orange", "オレンジ")),
                TestContext.Current.CancellationToken);
            Assert.True(stage.Succeeded);
            Assert.Equal(123456, stage.Value!.StageId);
            Assert.Equal(3, stage.Value.Artifacts.Count);
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DirectAudio_RejectsInvalidTiming()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var audio = Path.Combine(root, "audio.ogg");
        await File.WriteAllBytesAsync(audio, [1], TestContext.Current.CancellationToken);
        var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var invalid = await application.ConvertAudioFileAsync(new AudioFileConvertRequest(
                    audio, root, new AudioFileSettings(1, 0, 1, 0, true, 0, 4, 4)),
                TestContext.Current.CancellationToken);
            Assert.False(invalid.Succeeded);
            Assert.True(invalid.Diagnostics.HasError);
        }
        finally
        {
            store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Progress_IsForwarded_ForLongRunningOperations()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var reports = new List<ProgressReport>();
        var progress = new InlineProgress(reports.Add);
        var input = Path.Combine(ChartTestPaths.AssetsDirectory, "Ver seX.mgxc");
        if (!File.Exists(input)) return;

        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "music");
        try
        {
            await application.BuildMusicAsync(new MusicBuildRequest(input, output), progress,
                TestContext.Current.CancellationToken);
            Assert.NotEmpty(reports);
            Assert.All(reports, report => Assert.NotNull(report.Item));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InjectedResources_AreNotOwnedByApplication()
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new TrackingAssetStore(root);
        var paths = new TestPaths(root);
        var application = CreateInjectedApplication(root, store);
        application.Dispose();
        Assert.False(store.Disposed);
        store.Dispose();
        Directory.Delete(root, true);
    }

    private static PenguinToolsApplication CreateInjectedApplication(string root, IAssetStore store)
    {
        var paths = new TestPaths(root);
        var dependencies = new PenguinToolsApplicationDependencies(paths, store,
            TestAssets.Load(), TestMediaTool.Instance, new TestAssetProvider(root));
        return new PenguinToolsApplication(dependencies);
    }

    private sealed class InlineProgress(Action<ProgressReport> action) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value)
        {
            action(value);
        }
    }

    private sealed record TestPaths(string Root) : IApplicationPaths
    {
        public string TempWorkPath => Root;
    }

    private sealed record TestAssetProvider(string Root) : IInfrastructureAssetProvider
    {
        public string GetPath(InfrastructureAsset asset)
        {
            return Path.Combine(Root, asset.ToString());
        }
    }

    private sealed class TrackingAssetStore(string root) : IAssetStore
    {
        public bool Disposed { get; private set; }
        public string AssetDirectory => root;
        public string TempWorkPath => root;

        public bool HasAsset(string assetName)
        {
            return false;
        }

        public string GetAssetPath(string assetName)
        {
            return Path.Combine(root, assetName);
        }

        public string GetTempPath(string fileName)
        {
            return Path.Combine(root, fileName);
        }

        public Stream OpenRead(string assetName)
        {
            return Stream.Null;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
