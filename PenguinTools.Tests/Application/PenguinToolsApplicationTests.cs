using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.Assets;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Metadata;
using PenguinTools.Infrastructure;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed class PenguinToolsApplicationTests
{
    [Theory]
    [InlineData(false, ChartDialect.Original)]
    [InlineData(false, ChartDialect.Extended)]
    [InlineData(true, ChartDialect.Original)]
    [InlineData(true, ChartDialect.Extended)]
    public async Task ChartInspection_UsesTheSelectedConversionMode(bool ugc, ChartDialect dialect)
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
                ? (await new UgcChartWriter(input, chart, ChartDialect.Original).WriteAsync(ct)).Succeeded
                : (await new MgxcChartWriter(new MgxcWriteRequest(input, chart, ChartDialect.Original)).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            var result = await application.InspectChartAsync(new ChartInspectRequest(input, dialect), ct);
            Assert.True(result.Succeeded, result.ToString());
            Assert.True(result.Value!.Metadata.BgmEnableBarOffset);
            Assert.Equal(.75m, result.Value.Metadata.BgmManualOffset);
            Assert.Equal(dialect == ChartDialect.Original ? 2.75m : .75m, result.Value.Metadata.BgmRealOffset);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(input, ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".c2s", ".mgxc", true)]
    [InlineData(".mgxc", ".c2s", false)]
    public async Task ChartConversion_RejectsUndefinedModesBeforeWriting(
        string inputExtension, string outputExtension, bool invalidInput)
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "output" + outputExtension);
        var ct = TestContext.Current.CancellationToken;
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            await File.WriteAllTextAsync(output, "existing chart", ct);
            var result = await application.ConvertChartAsync(new ChartConvertRequest(
                Path.Combine(root, "input" + inputExtension), output,
                InputDialect: invalidInput ? (ChartDialect)999 : ChartDialect.Original,
                OutputDialect: invalidInput ? ChartDialect.Original : (ChartDialect)999), cancellationToken: ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Invalid_Header);
            Assert.Equal("existing chart", await File.ReadAllTextAsync(output, ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false, ChartDialect.Original)]
    [InlineData(false, true, ChartDialect.Original)]
    [InlineData(true, false, ChartDialect.Original)]
    [InlineData(true, true, ChartDialect.Original)]
    [InlineData(false, false, ChartDialect.Extended)]
    [InlineData(false, true, ChartDialect.Extended)]
    [InlineData(true, false, ChartDialect.Extended)]
    [InlineData(true, true, ChartDialect.Extended)]
    public async Task ChartConversion_UsesSelectedEditorModeWithoutMigratingInput(
        bool ugc, bool blank, ChartDialect dialect)
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
                ? (await new UgcChartWriter(input, chart, dialect).WriteAsync(ct)).Succeeded
                : (await new MgxcChartWriter(new MgxcWriteRequest(input, chart, dialect)).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            Assert.True((await application.InspectChartAsync(new ChartInspectRequest(input), ct)).Succeeded);
            var result = await application.ConvertChartAsync(new ChartConvertRequest(input, output, InputDialect: dialect), cancellationToken: ct);
            Assert.True(result.Succeeded, result.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var offset = dialect == ChartDialect.Original && blank ? 1920 : 0;
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
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(input, OriginFixture(blank), ChartDialect.Original)).WriteAsync(ct)).Succeeded);
            var rewritten = await application.ConvertChartAsync(new ChartConvertRequest(input, saved,
                InputDialect: ChartDialect.Original, OutputDialect: ChartDialect.Original), cancellationToken: ct);
            Assert.True(rewritten.Succeeded, rewritten.ToString());
            var back = await application.ConvertChartAsync(new ChartConvertRequest(saved, output,
                InputDialect: ChartDialect.Original), cancellationToken: ct);
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
            Assert.InRange(inspected.Value.Metadata.DifficultyId, 0, 5);
            Assert.NotNull(inspected.Value.Metadata.NotesFieldLine);
            Assert.NotNull(inspected.Value.Metadata.Stage);

            var converted = await application.ConvertChartAsync(new ChartConvertRequest(input, output,
                    new ChartConvertOverrides(4321, "Override Designer", 2, 198.5m, true), InputDialect: ChartDialect.Extended),
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
    public async Task MusicBuild_ForwardsProgressAndWritesChart()
    {
        var reports = new List<ProgressReport>();
        var progress = new InlineProgress(reports.Add);

        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        var output = Path.Combine(root, "music");
        try
        {
            var input = await WriteChartFixtureAsync(root);
            var result = await application.BuildMusicAsync(new MusicBuildRequest(input, output), progress,
                TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.False(result.Diagnostics.HasError);
            Assert.NotEmpty(Directory.EnumerateFiles(output, "*.c2s", SearchOption.AllDirectories));
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
