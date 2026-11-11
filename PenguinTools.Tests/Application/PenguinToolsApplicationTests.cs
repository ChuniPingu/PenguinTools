using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.Assets;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Metadata;
using PenguinTools.Infrastructure;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed partial class PenguinToolsApplicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChartInspection_UsesOfficialPreroll(bool ugc)
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, ugc ? "input.ugc" : "input.mgxc");
        var ct = TestContext.Current.CancellationToken;
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var chart = OriginFixture(true);
            Assert.True(ugc
                ? (await new UgcChartWriter(input, chart).WriteAsync(ct)).Succeeded
                : (await new MgxcChartWriter(new MgxcWriteRequest(input, chart)).WriteAsync(ct)).Succeeded);
            await LegacyEditorFixture.DowngradeAsync(input, ct);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            var result = await application.InspectChartAsync(new ChartInspectRequest(input), ct);
            Assert.True(result.Succeeded, result.ToString());
            Assert.NotNull(result.Value!.Metadata);
            Assert.True(result.Value.Metadata.BgmEnableBarOffset);
            Assert.Equal(.75m, result.Value.Metadata.BgmManualOffset);
            Assert.Equal(2.75m, result.Value.Metadata.BgmRealOffset);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(input, ct));
        }
        finally { Directory.Delete(root, true); }
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChartConversion_AppliesOfficialPrerollWithoutChangingInput(
        bool ugc, bool blank)
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, ugc ? "input.ugc" : "input.mgxc");
        var output = Path.Combine(root, "output.c2s");
        var ct = TestContext.Current.CancellationToken;
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var chart = OriginFixture(blank);
            Assert.True(ugc
                ? (await new UgcChartWriter(input, chart).WriteAsync(ct)).Succeeded
                : (await new MgxcChartWriter(new MgxcWriteRequest(input, chart)).WriteAsync(ct)).Succeeded);
            await LegacyEditorFixture.DowngradeAsync(input, ct);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            Assert.True((await application.InspectChartAsync(new ChartInspectRequest(input), ct)).Succeeded);
            var result = await application.ConvertChartAsync(new ChartConvertRequest(input, output), cancellationToken: ct);
            Assert.True(result.Succeeded, result.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var offset = blank ? 1920 : 0;
            Assert.Equal(offset, Assert.Single(parsed.Value!.Notes.OfType<PenguinTools.Chart.Models.c2s.Tap>()).Tick.Original);
            var hold = Assert.Single(parsed.Value.Notes.OfType<PenguinTools.Chart.Models.c2s.Hold>());
            Assert.Equal(240 + offset, hold.Tick.Original);
            Assert.Equal(720 + offset, hold.EndTick.Original);
            Assert.Equal(offset > 0, parsed.Value.Extras.PrerollBar1Applied);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(input, ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChartConversion_OriginalEditorRewriteRetainsSofs(bool ugc, bool blank)
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.mgxc");
        var saved = Path.Combine(root, ugc ? "saved.ugc" : "saved.mgxc");
        var output = Path.Combine(root, "output.c2s");
        var ct = TestContext.Current.CancellationToken;
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(input, OriginFixture(blank))).WriteAsync(ct)).Succeeded);
            var rewritten = await application.ConvertChartAsync(new ChartConvertRequest(input, saved), cancellationToken: ct);
            Assert.True(rewritten.Succeeded, rewritten.ToString());
            var back = await application.ConvertChartAsync(new ChartConvertRequest(saved, output), cancellationToken: ct);
            Assert.True(back.Succeeded, back.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(blank ? 1920 : 0, Assert.Single(parsed.Value!.Notes.OfType<PenguinTools.Chart.Models.c2s.Tap>()).Tick.Original);
        }
        finally { Directory.Delete(root, true); }
    }

    private static PenguinTools.Chart.Models.umgr.Chart OriginFixture(bool blank)
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();
        chart.Meta.BgmEnableBarOffset = blank;
        chart.Meta.BgmManualOffset = .75m;
        chart.Events.AppendChild(new PenguinTools.Chart.Models.umgr.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new PenguinTools.Chart.Models.umgr.BeatEvent { Tick = 0, Bar = 0, Numerator = 4, Denominator = 4 });
        chart.Notes.AppendChild(new PenguinTools.Chart.Models.umgr.Tap { Tick = 0, Lane = 0, Width = 2 });
        var hold = new PenguinTools.Chart.Models.umgr.Hold { Tick = 240, Lane = 4, Width = 2 };
        hold.AppendChild(new PenguinTools.Chart.Models.umgr.HoldJoint { Tick = 720, Lane = 4, Width = 2 });
        chart.Notes.AppendChild(hold);
        return chart;
    }

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
    public async Task OptionScan_SkipsChart_WhenMetaIgnoreIsSet()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var ignoredPath = Path.Combine(directory, "skipped.ugc");
        var keptPath = Path.Combine(directory, "kept.ugc");
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(
            ignoredPath,
            "@VER\t7\n@TICKS\t960\n@TITLE\tSkipped\n@DIFF\t3\n@SONGID\t5097\n" +
            "@CMT\t#meta ignore\n@UNKNOWN\tfoo\n#bad\n",
            ct);
        await File.WriteAllTextAsync(
            keptPath,
            "@VER\t8\n@TICKS\t480\n@TITLE\tKept\n@DESIGN\tDesigner\n@DIFF\t2\n" +
            "@SONGID\t5097\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n",
            ct);
        using var application = PenguinToolsApplication.CreateDefault();
        try
        {
            var result = await application.ScanOptionAsync(
                new OptionScanRequest(directory, [ChartFormat.Ugc]),
                cancellationToken: ct);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Value);
            var book = Assert.Single(result.Value!.Books);
            var chart = Assert.Single(book.Charts);
            Assert.Equal("Expert", chart.Difficulty);
            Assert.Equal(keptPath, chart.FilePath);
            Assert.Contains(result.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_Meta_Ignored);
            Assert.DoesNotContain(result.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Error_Invalid_Header);
            Assert.DoesNotContain(result.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_Unrecognized_meta);
            Assert.DoesNotContain(result.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_Unrecognized_note);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OptionScan_UsesConfigChartFileDiscovery_WhenRequestOmitsIt()
    {
        using var application = PenguinToolsApplication.CreateDefault();
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "options.json");
        var document = new OptionDocument
        {
            OptionName = "TEST",
            ChartFileDiscovery = [ChartFileFormat.Mgxc]
        };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(document, OptionDocumentJson.Default),
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "ignored.ugc"),
            "@VER\t8\n@TICKS\t480\n@TITLE\tignored\n@DESIGN\tDesigner\n@DIFF\t3\n" +
            "@SONGID\t1\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n",
            TestContext.Current.CancellationToken);
        try
        {
            var result = await application.ScanOptionAsync(new OptionScanRequest(directory),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Value);
            Assert.Equal(["mgxc"], result.Value!.ChartFileDiscovery);
            Assert.Empty(result.Value.Books);
            Assert.DoesNotContain(result.Value.UnmatchedDiagnostics,
                d => d.Path is not null && d.Path.EndsWith("ignored.ugc", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Diagnostics.Diagnostics,
                d => d.Path is not null && d.Path.EndsWith("ignored.ugc", StringComparison.OrdinalIgnoreCase));
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
            ConvertBackground = false,
            ConvertWall = false
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
            Assert.Equal(["ugc", "sus"], result.Value.ChartFileDiscovery);
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
    public async Task ChartConversion_ReportsProgress()
    {

        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        var output = Path.Combine(root, "chart.c2s");
        var reports = new List<ProgressReport>();
        try
        {
            var input = await WriteChartFixtureAsync(root);
            var result = await application.ConvertChartAsync(new ChartConvertRequest(input, output),
                new InlineProgress(reports.Add), TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.True(File.Exists(output));
            Assert.NotEmpty(reports);
            Assert.Contains(reports, report => report is { Completed: 1, Total: 1 });
            Assert.All(reports, report => Assert.Equal(Path.GetFileName(input), report.Item));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task JacketConversion_ReportsArtifactForChart()
    {

        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "jacket.dds");
        var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var input = await WriteChartFixtureAsync(root);
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
            Assert.NotNull(inspected.Value.Metadata);
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
        {
            await File.WriteAllBytesAsync(effect, [1], TestContext.Current.CancellationToken);
        }

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
    public async Task OptionBuild_ExportsAllDifficultiesForOneSong()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-single-song-");
        var input = Path.Combine(root.FullName, "song");
        Directory.CreateDirectory(input);
        using var store = new TrackingAssetStore(root.FullName);
        using var application = CreateInjectedApplication(root.FullName, store);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var master = await WriteChartFixtureAsync(input);
            var text = await File.ReadAllTextAsync(master, ct);
            await File.WriteAllTextAsync(Path.Combine(input, "expert.ugc"),
                text.Replace("@DIFF\t3", "@DIFF\t2"), ct);
            var reports = new System.Collections.Concurrent.ConcurrentQueue<ProgressReport>();
            var output = Path.Combine(root.FullName, "A123");
            var result = await application.BuildOptionAsync(new OptionBuildRequest(input, output,
                SkipConfig: true, Overrides: new OptionBuildOverrides(OptionName: "A123")),
                new InlineProgress(reports.Enqueue), ct);

            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(1, result.Value.BookCount);
            Assert.Equal(2, result.Value.ChartCount);
            var music = Path.Combine(output, "music", "music1000");
            var xml = System.Xml.Linq.XDocument.Load(Path.Combine(music, "Music.xml"));
            var enabled = xml.Descendants("MusicFumenData").Where(fumen => (bool)fumen.Element("enable")!)
                .Select(fumen => (string)fumen.Element("file")!.Element("path")!).ToArray();
            Assert.Equal(["1000_02.c2s", "1000_03.c2s"], enabled);
            Assert.All(enabled, file => Assert.True(File.Exists(Path.Combine(music, file))));
            Assert.True(File.Exists(Path.Combine(output, "cueFile", "cueFile001000", "CueFile.xml")));
            Assert.Contains(reports, report => report is { Completed: 1, Total: 1, Item: not null });
        }
        finally { root.Delete(true); }
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

    [Fact]
    public async Task Walls_UseFinalSongId_AndExposeActualArtifactsAndMetadata()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-wall-app-");
        using var store = new TrackingAssetStore(root.FullName);
        var assets = TestAssets.Load();
        assets.DefineEntry(AssetType.FieldWallNames, new Entry(3, "Known wall"));
        using var app = CreateInjectedApplication(root.FullName, store, assets);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var chart = await WriteChartFixtureAsync(root.FullName);
            await File.AppendAllTextAsync(chart, "@CMT\t#meta wall \"jacket.png\" normal\n", ct);
            var inspected = await app.InspectChartAsync(new ChartInspectRequest(chart), ct);
            Assert.True(inspected.Succeeded);
            Assert.Equal(1001000, inspected.Value.Metadata!.WallId);
            Assert.Null(inspected.Value.Metadata.ExplicitWallId);
            var output = Path.Combine(root.FullName, "loose");
            var built = await app.BuildWallAsync(new WallBuildRequest(chart, output, SongId: 9978), ct);
            Assert.True(built.Succeeded, built.ToString());
            Assert.Equal(1009978, built.Value.WallId);
            Assert.Equal(WallWidth.Normal, built.Value.Width);
            Assert.Equal(3, built.Value.Artifacts.Count);
            Assert.All(built.Value.Artifacts, artifact => Assert.True(File.Exists(artifact.Path)));
            Assert.True(File.Exists(Path.Combine(output, "ddsFieldWall1009978", "DDSFieldWall.xml")));
            var explicitResult = await app.BuildWallFilesAsync(new WallFilesBuildRequest(
                Path.Combine(root.FullName, "jacket.png"), output, 9978, new WallOverrides(WallId: 3)), ct);
            Assert.True(explicitResult.Succeeded, explicitResult.ToString());
            Assert.Equal(3, explicitResult.Value.WallId);
            Assert.Equal(WallWidth.Wide, explicitResult.Value.Width);
            Assert.Contains(explicitResult.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Wall_already_exists);
            var failed = await app.BuildWallAsync(new WallBuildRequest(chart, output,
                new WallOverrides(InputPath: Path.Combine(root.FullName, "missing.png"))), ct);
            Assert.False(failed.Succeeded);
            Assert.Contains(failed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Invalid_wall_image);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task OptionWalls_UseSelectedMainDifficulty_LayoutAndSavedSwitch()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-wall-option-");
        using var store = new TrackingAssetStore(root.FullName);
        using var app = CreateInjectedApplication(root.FullName, store);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var chart = await WriteChartFixtureAsync(root.FullName);
            var text = await File.ReadAllTextAsync(chart, ct);
            await File.AppendAllTextAsync(chart, "@CMT\t#meta wall jacket.png normal 42\n", ct);
            var ultima = Path.Combine(root.FullName, "ultima.ugc");
            await File.WriteAllTextAsync(ultima, text.Replace("@DIFF\t3", "@DIFF\t5", StringComparison.Ordinal) +
                "@CMT\t#meta wall jacket.png wide 43\n", ct);
            var scan = await app.ScanOptionAsync(new OptionScanRequest(root.FullName), cancellationToken: ct);
            Assert.True(scan.Succeeded, scan.ToString());
            Assert.All(Assert.Single(scan.Value.Books).Charts, item => Assert.NotEmpty(item.FullWallFilePath));
            var output = Path.Combine(root.FullName, "A123");
            var overrides = new OptionBuildOverrides(OptionName: "A123", ConvertChart: false, ConvertAudio: false,
                ConvertJacket: false, ConvertBackground: false, ConvertWall: true,
                MainDifficulties: [new OptionMainDifficultyOverride(1000, "Ultima")]);
            var built = await app.BuildOptionAsync(new OptionBuildRequest(root.FullName, output,
                SkipConfig: true, SaveConfig: true, Overrides: overrides), cancellationToken: ct);
            Assert.True(built.Succeeded, built.ToString());
            Assert.True(File.Exists(Path.Combine(output, "ddsFieldWall", "ddsFieldWall0043", "DDSFieldWall.xml")));
            var manifest = Path.Combine(output, "wallbind.json");
            var original = await File.ReadAllBytesAsync(manifest, ct);
            var disabled = await app.BuildOptionAsync(new OptionBuildRequest(root.FullName, output,
                SaveConfig: true, Overrides: new OptionBuildOverrides(ConvertWall: false, GenerateEventXml: true)), cancellationToken: ct);
            Assert.True(disabled.Succeeded, disabled.ToString());
            Assert.Equal(original, await File.ReadAllBytesAsync(manifest, ct));
            var saved = await app.ScanOptionAsync(new OptionScanRequest(root.FullName), cancellationToken: ct);
            Assert.True(saved.Succeeded, saved.ToString());
            Assert.False(saved.Value.Config!.ConvertWall);
        }
        finally { root.Delete(true); }
    }

    private static async Task<string> WriteChartFixtureAsync(string root)
    {
        var ct = TestContext.Current.CancellationToken;
        var input = Path.Combine(root, "chart.ugc");
        await File.WriteAllTextAsync(Path.Combine(root, "audio.wav"), "fake audio", ct);
        await File.WriteAllTextAsync(Path.Combine(root, "jacket.png"), "fake image", ct);
        await File.WriteAllTextAsync(input,
            "'MGR_ORIGIN=ZERO_V1\n@VER\t8\n@TICKS\t480\n@TITLE\tTest\n@DESIGN\tDesigner\n@DIFF\t3\n" +
            "@SONGID\t1000\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n" +
            "@BGM\taudio.wav\n@JACKET\tjacket.png\n", ct);
        return input;
    }
    private static PenguinToolsApplication CreateInjectedApplication(string root, IAssetStore store, AssetManager? assets = null)
    {
        var paths = new TestPaths(root);
        var dependencies = new PenguinToolsApplicationDependencies(paths, store,
            assets ?? TestAssets.Load(), TestMediaTool.Instance, TestMediaTool.Instance, new TestAssetProvider(root));
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
            return Path.Combine(root, Core.IO.TempFileNames.MakeUnique(fileName));
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
