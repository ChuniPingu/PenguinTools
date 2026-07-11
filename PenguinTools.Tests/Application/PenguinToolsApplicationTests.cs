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
        Assert.NotEmpty(result.Value.UserDataPath);
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
    public async Task OptionScan_IncludesAutoDiscoveredConfig()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "options.json");
        var document = new OptionDocument
        {
            OptionName = "TEST",
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
        Assert.False((await application.CollectAssetsAsync(new AssetCollectRequest(output),
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
        public string UserDataPath => Root;
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