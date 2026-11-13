using System.Globalization;
using PenguinTools.Assets;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using PenguinTools.Infrastructure;
using PenguinTools.Media;
using PenguinTools.Workflow;
using static PenguinTools.Application.RequestPaths;
using MediaAfbExtractRequest = PenguinTools.Media.AfbExtractRequest;
using MediaCriExtractOptions = PenguinTools.Media.CriExtractOptions;
using MediaJacketConvertRequest = PenguinTools.Media.JacketConvertRequest;

namespace PenguinTools.Application;

public sealed record PenguinToolsApplicationDependencies(
    IApplicationPaths Paths,
    IAssetStore AssetStore,
    AssetManager Assets,
    IAudioService Audio,
    IImageService Images,
    IInfrastructureAssetProvider AssetProvider);

public sealed partial class PenguinToolsApplication : IPenguinToolsApplication
{
    private readonly PenguinToolsApplicationDependencies _dependencies;
    private readonly IDisposable? _ownedResource;
    private bool _disposed;
    private readonly ChartOperations _charts;
    private readonly IMediaValidator _mediaValidator;

    public PenguinToolsApplication(PenguinToolsApplicationDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _dependencies = dependencies;
        _mediaValidator = new MediaValidator(dependencies.Audio, dependencies.Images);
        _charts = new ChartOperations(dependencies.Assets, _mediaValidator);
    }

    private PenguinToolsApplication(PenguinToolsApplicationDependencies dependencies, IDisposable ownedResource)
        : this(dependencies)
    {
        _ownedResource = ownedResource;
    }

    public Task<OperationResult<ChartInspectResult>> InspectChartAsync(
        ChartInspectRequest request, CancellationToken cancellationToken = default)
    {
        return GuardAsync(() => _charts.InspectAsync(request, cancellationToken));
    }

    public IncrementalBuildSession CreateBuildSession()
    {
        ThrowIfDisposed();
        return new IncrementalBuildSession(_dependencies, ThrowIfDisposed);
    }

    public Task<OperationResult<ChartConvertResult>> ConvertChartAsync(
        ChartConvertRequest request, IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return GuardAsync(() => _charts.ConvertAsync(request, progress, cancellationToken));
    }

    public async Task<OperationResult<OptionScanResult>> ScanOptionAsync(
        OptionScanRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(() => ScanOptionCoreAsync(request, progress, cancellationToken));
    }

    private async Task<OperationResult<OptionScanResult>> ScanOptionCoreAsync(
        OptionScanRequest request, IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = FullPath(request.InputDirectory);
        if (!Directory.Exists(input))
        {
            return ApplicationDiagnostics.Failure<OptionScanResult>(
                Msg.Key(MsgKeys.App_Input_directory_not_found), input);
        }

        if (request.BatchSize == 0 || request.BatchSize < -1)
        {
            return ApplicationDiagnostics.Failure<OptionScanResult>(Msg.Key(MsgKeys.App_Batch_size_invalid));
        }

        var (configPath, configDocument, configDiagnostics) =
            await OptionConfiguration.LoadForScanAsync(input, cancellationToken);
        var config = configDocument is null ? null : CreateScanConfig(configDocument);

        if (request.SaveConfig)
        {
            var savePath = Path.Combine(input, "options.json");
            var document = configDocument ?? (File.Exists(savePath)
                ? await OptionConfiguration.LoadAsync(savePath, cancellationToken)
                : new OptionDocument());
            if (request.ChartFileDiscovery is not null)
            {
                document.ChartFileDiscovery = [.. request.ChartFileDiscovery.Select(ToWorkflow)];
            }

            document.BatchSize = request.BatchSize;
            await OptionConfiguration.SaveAsync(savePath, document, cancellationToken);
            configPath = savePath;
            configDocument = document;
            config = CreateScanConfig(document);
            configDiagnostics = DiagnosticSnapshot.Empty;
        }

        var applicationDiscovery = request.ChartFileDiscovery
                                   ?? configDocument?.ChartFileDiscovery.Select(FromWorkflow).ToArray()
                                   ?? [ChartFormat.Mgxc, ChartFormat.Ugc, ChartFormat.Sus];
        var discovery = applicationDiscovery.Select(ToWorkflow).ToArray();
        var workingDirectory = FullPath(request.WorkingDirectory ?? input);
        var scanned = await ScanBooksAsync(input, discovery, request.BatchSize, workingDirectory,
            progress, cancellationToken);
        if (scanned.Value is null)
        {
            return OperationResult<OptionScanResult>.Failure().WithDiagnostics(scanned.Diagnostics);
        }

        var (value, unmatchedDiagnostics) = CreateScanResult(input, applicationDiscovery, request.BatchSize,
            scanned.Value, scanned.Diagnostics, configPath, config);
        return (scanned.Succeeded && !value.IsPartial
                ? OperationResult<OptionScanResult>.Success(value)
                : OperationResult<OptionScanResult>.Failure(value))
            .WithDiagnostics(unmatchedDiagnostics.Merge(configDiagnostics));
    }

    public async Task<OperationResult<OptionBuildResult>> BuildOptionAsync(
        OptionBuildRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(() => BuildOptionCoreAsync(request, progress, cancellationToken));
    }

    private async Task<OperationResult<OptionBuildResult>> BuildOptionCoreAsync(
        OptionBuildRequest request, IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = FullPath(request.InputDirectory);
        var output = FullPath(request.OutputDirectory);
        if (!Directory.Exists(input))
        {
            return ApplicationDiagnostics.Failure<OptionBuildResult>(
                Msg.Key(MsgKeys.App_Input_directory_not_found), input);
        }

        if (request.SkipConfig && !string.IsNullOrWhiteSpace(request.ConfigPath))
        {
            return ApplicationDiagnostics.Failure<OptionBuildResult>(
                Msg.Key(MsgKeys.App_Config_path_conflict));
        }

        var configPath = OptionConfiguration.ResolveLoadPath(request, input);
        var loadedConfig = configPath is not null;
        var document = loadedConfig
            ? await OptionConfiguration.LoadAsync(configPath!, cancellationToken)
            : new OptionDocument { OptionName = string.Empty };
        ApplyOverrides(document, request.Overrides);
        if (string.IsNullOrWhiteSpace(document.OptionName) || document.OptionName.Length != 4)
        {
            return ApplicationDiagnostics.Failure<OptionBuildResult>(
                Msg.Key(MsgKeys.App_Option_name_required));
        }

        if (document.BatchSize is 0 or < -1)
        {
            return ApplicationDiagnostics.Failure<OptionBuildResult>(Msg.Key(MsgKeys.App_Batch_size_invalid));
        }

        if (!document.HasExportableWork())
        {
            return ApplicationDiagnostics.Failure<OptionBuildResult>(
                Msg.Key(MsgKeys.App_No_export_actions_enabled));
        }

        var exportSettings = document.ToExportSettings() with { IgnoreCache = request.IgnoreCache };
        var scanned = await ScanBooksAsync(input, document.ChartFileDiscovery, document.BatchSize, output,
            progress, cancellationToken);
        if (!scanned.Succeeded)
        {
            return OperationResult<OptionBuildResult>.Failure().WithDiagnostics(scanned.Diagnostics);
        }

        if (scanned.Value.Count == 0)
        {
            return OperationResult<OptionBuildResult>.Failure()
                .WithDiagnostics(scanned.Diagnostics.Merge(DiagnosticSnapshot.Create([
                    new Diagnostic(Severity.Error, Msg.Key(MsgKeys.App_No_charts_to_export))
                ])));
        }

        var snapshots = ApplyMainDifficultyOverrides(scanned.Value, request.Overrides?.MainDifficulties);
        var bundleRoot = ExportOutputPaths.ResolveBundleRootPath(output, document.OptionName);
        var outputPaths = ExportOutputPaths.FromOptionDirectory(bundleRoot);
        var skippedCharts = SkippedVersionCharts(scanned.Diagnostics);
        exportSettings = exportSettings with
        {
            InputComplete = !scanned.Diagnostics.HasError && skippedCharts.Length == 0
        };
        var exported = await OptionExporter.ExportAsync(
            CreateExportContext(), exportSettings, outputPaths, snapshots, output,
            cancellationToken, progress);
        var diagnostics = scanned.Diagnostics.Merge(exported.Diagnostics);
        if (!exported.Succeeded)
        {
            return OperationResult<OptionBuildResult>.Failure().WithDiagnostics(diagnostics);
        }

        string? savedConfigPath = null;
        if (request.SaveConfig)
        {
            savedConfigPath = OptionConfiguration.ResolveSavePath(request, input, configPath);
            await OptionConfiguration.SaveAsync(savedConfigPath, document, cancellationToken);
        }

        var artifacts = CollectOptionArtifacts(bundleRoot);
        var value = new OptionBuildResult(input, bundleRoot, savedConfigPath ?? configPath, document.OptionName,
            scanned.Value.Count, scanned.Value.Sum(x => x.Difficulties.Count), artifacts)
        {
            SkippedCharts = skippedCharts
        };
        return (value.IsPartial ? OperationResult<OptionBuildResult>.Failure(value)
            : OperationResult<OptionBuildResult>.Success(value)).WithDiagnostics(diagnostics);
    }

    private static ApplicationArtifact[] CollectOptionArtifacts(string bundleRoot)
    {
        return Directory.Exists(bundleRoot)
            ? Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => new ApplicationArtifact("option.file", x)).ToArray()
            : [];
    }

    public async Task<OperationResult<JacketConvertResult>> ConvertJacketAsync(
        JacketConvertRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = FullPath(request.InputPath);
            var output = FullPath(request.OutputPath);
            var parsed = await _charts.ParseChartAsync(input, cancellationToken);
            if (!parsed.Succeeded)
            {
                return OperationResult<JacketConvertResult>.Failure().WithDiagnostics(parsed.Diagnostics);
            }

            var chart = parsed.Value;
            var source = OptionalFullPath(request.JacketInputPath) ?? chart.Meta.FullJacketFilePath;
            EnsureParentDirectory(output);
            var converted =
                await new JacketConverter(new MediaJacketConvertRequest(source, output), _dependencies.Images)
                    .ConvertAsync(cancellationToken);
            var value = new JacketConvertResult(input, source, output,
                [new ApplicationArtifact("jacket.dds", output)]);
            return ApplicationDiagnostics.Merge(value, parsed.Diagnostics, converted);
        });
    }

    public async Task<OperationResult<JacketConvertResult>> ConvertJacketFileAsync(
        JacketFileConvertRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = FullPath(request.InputPath);
            var output = FullPath(request.OutputPath);
            EnsureParentDirectory(output);
            var converted = await new JacketConverter(
                    new MediaJacketConvertRequest(input, output), _dependencies.Images)
                .ConvertAsync(cancellationToken);
            var value = new JacketConvertResult(input, input, output,
                [new ApplicationArtifact("jacket.dds", output)]);
            return ApplicationDiagnostics.Merge(value, DiagnosticSnapshot.Empty, converted);
        });
    }

    public async Task<OperationResult<AudioConvertResult>> ConvertAudioAsync(
        AudioConvertRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = FullPath(request.InputPath);
            var output = FullPath(request.OutputDirectory);
            var parsed = await _charts.ParseChartAsync(input, cancellationToken);
            if (!parsed.Succeeded)
            {
                return OperationResult<AudioConvertResult>.Failure().WithDiagnostics(parsed.Diagnostics);
            }

            var chart = parsed.Value;
            var converted = await MusicMediaExporter.ConvertAudioAsync(CreateExportContext(), chart.Meta, output,
                ToWorkflow(request.Overrides), cancellationToken);
            var xml = new CueFileXml(chart.Meta.Id ?? 0);
            var directory = Path.Combine(output, xml.DataName);
            var value = new AudioConvertResult(input, chart.Meta.FullBgmFilePath, output,
            [
                new ApplicationArtifact("cue-file.xml", Path.Combine(directory, "CueFile.xml")),
                new ApplicationArtifact("audio.acb", Path.Combine(directory, xml.AcbFile)),
                new ApplicationArtifact("audio.awb", Path.Combine(directory, xml.AwbFile))
            ]);
            return ApplicationDiagnostics.Merge(value, parsed.Diagnostics, converted);
        });
    }

    public async Task<OperationResult<AudioConvertResult>> ConvertAudioFileAsync(
        AudioFileConvertRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = FullPath(request.InputPath);
            var output = FullPath(request.OutputDirectory);
            var settings = request.Settings;
            ResourceIds.ValidateSong(settings.SongId);
            if (settings.InitialBpm <= 0 || settings.InitialNumerator <= 0 || settings.InitialDenominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Initial timing values must be positive.");
            }

            var meta = new Meta
            {
                FilePath = input,
                BgmFilePath = input,
                Id = settings.SongId,
                BgmPreviewStart = settings.PreviewStart,
                BgmPreviewStop = settings.PreviewStop,
                BgmManualOffset = settings.ManualOffset,
                BgmEnableBarOffset = settings.InsertBlankMeasure,
                BgmInitialBpm = settings.InitialBpm,
                BgmInitialNumerator = settings.InitialNumerator,
                BgmInitialDenominator = settings.InitialDenominator
            };
            var converted = await MusicMediaExporter.ConvertAudioAsync(CreateExportContext(), meta, output,
                new AudioRequestOverrides(null, settings.HcaEncryptionKey), cancellationToken);
            var value = CreateAudioConvertResult(input, output, settings.SongId);
            return ApplicationDiagnostics.Merge(value, DiagnosticSnapshot.Empty, converted);
        });
    }

    public async Task<OperationResult<StageBuildResult>> BuildStageAsync(
        StageBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = FullPath(request.InputPath);
            var output = FullPath(request.OutputDirectory);
            var overrides = ToWorkflow(request.Overrides);
            var parsed = await _charts.ParseChartAsync(input, cancellationToken);
            if (!parsed.Succeeded)
            {
                return OperationResult<StageBuildResult>.Failure().WithDiagnostics(parsed.Diagnostics);
            }

            var chart = parsed.Value;
            var built = await MusicMediaExporter.BuildStageAsync(CreateExportContext(), chart.Meta, output, overrides,
                cancellationToken);
            var stageId = overrides.StageId ?? chart.Meta.StageId;
            var artifacts = CreateStageArtifacts(output, chart.Meta, overrides, out var stageName);
            var value = new StageBuildResult(input, overrides.BackgroundPath ?? chart.Meta.FullBgiFilePath, output,
                stageId, stageName, artifacts);
            return ApplicationDiagnostics.Merge(value, parsed.Diagnostics, built.ToResult());
        });
    }

    public async Task<OperationResult<StageBuildResult>> BuildStageFilesAsync(
        StageFilesBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var background = FullPath(request.BackgroundPath);
            var output = FullPath(request.OutputDirectory);
            var requested = request.Overrides with { BackgroundPath = background };
            var overrides = ToWorkflow(requested);
            var meta = new Meta
            {
                FilePath = background,
                BgiFilePath = background,
                IsCustomStage = true,
                StageId = overrides.StageId
            };
            var built = await MusicMediaExporter.BuildStageAsync(CreateExportContext(), meta, output, overrides,
                cancellationToken);
            var artifacts = CreateStageArtifacts(output, meta, overrides, out var stageName);
            var value = new StageBuildResult(background, background, output, overrides.StageId, stageName, artifacts);
            return ApplicationDiagnostics.Merge(value, DiagnosticSnapshot.Empty, built.ToResult());
        });
    }

    public async Task<OperationResult<CriAudioExtractResult>> ExtractCriAudioAsync(
        CriAudioExtractRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = FullPath(request.SourcePath);
            var output = FullPath(request.OutputDirectory);
            var paired = OptionalFullPath(request.PairedPath);
            if (!File.Exists(source))
            {
                return ApplicationDiagnostics.Failure<CriAudioExtractResult>(Msg.Key(MsgKeys.Error_File_not_found), source);
            }

            progress?.Report(new ProgressReport(Item: Path.GetFileName(source), Completed: 0, Total: 1));
            var extracted = await _dependencies.Audio.ExtractCriAudioAsync(
                new MediaCriExtractOptions(source, output, paired, request.HcaKey), cancellationToken);
            var cues = extracted.Cues.Select(x => new CriCueSummary(
                x.CueId, x.Name, x.WavPath, x.Channels, x.SampleRate, x.BitsPerSample, x.SampleFrames,
                x.PreviewStartMs, x.PreviewStopMs)).ToArray();
            var artifacts = cues.Select(x => new ApplicationArtifact("audio.wav", x.WavPath)).ToArray();
            progress?.Report(new ProgressReport(Item: Path.GetFileName(source), Completed: 1, Total: 1));
            return OperationResult<CriAudioExtractResult>.Success(
                new CriAudioExtractResult(source, output, cues, artifacts));
        });
    }

    public async Task<OperationResult<AfbExtractResult>> ExtractAfbAsync(
        AfbExtractRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = FullPath(request.InputPath);
            var output = FullPath(request.OutputDirectory);
            progress?.Report(new ProgressReport(Item: Path.GetFileName(input), Completed: 0, Total: 1));
            var extracted = await new AfbExtractor(new MediaAfbExtractRequest(input, output), _dependencies.Images)
                .ExtractAsync(cancellationToken);
            var artifacts = Directory.Exists(output)
                ? Directory.EnumerateFiles(output, "*.dds").OrderBy(x => x, StringComparer.Ordinal)
                    .Select(x => new ApplicationArtifact("dds.file", x)).ToArray()
                : [];
            progress?.Report(new ProgressReport(Item: Path.GetFileName(input), Completed: 1, Total: 1));
            var value = new AfbExtractResult(input, output, artifacts);
            return (extracted.Succeeded
                    ? OperationResult<AfbExtractResult>.Success(value)
                    : OperationResult<AfbExtractResult>.Failure())
                .WithDiagnostics(extracted.Diagnostics);
        });
    }

    public async Task<OperationResult<AssetCollectResult>> CollectAssetsAsync(
        AssetCollectRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gameRoot = FullPath(request.GameRoot);
            if (!Directory.Exists(gameRoot))
            {
                return ApplicationDiagnostics.Failure<AssetCollectResult>(
                    Msg.Key(MsgKeys.App_Game_directory_not_found), gameRoot);
            }

            var output = FullPath(request.OutputPath);
            progress?.Report(new ProgressReport(Item: gameRoot));
            using var assetsStream = _dependencies.AssetStore.OpenRead(InfrastructureResourceNames.AssetsJson);
            await AssetManager.CollectToFileAsync(gameRoot, assetsStream, output, cancellationToken);
            return OperationResult<AssetCollectResult>.Success(new AssetCollectResult(gameRoot, output,
                [new ApplicationArtifact("assets.json", output)]));
        });
    }

    public Task<OperationResult<ApplicationInfo>> GetInfoAsync(
        ApplicationInfoRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = ExecutionInfoProvider.Create(_dependencies.Paths, _dependencies.AssetStore.AssetDirectory);
            return Task.FromResult(OperationResult<ApplicationInfo>.Success(new ApplicationInfo(
                info.ApplicationName, info.Version, info.BuildDateUtc, info.BaseDirectory, info.TempWorkPath,
                info.InfrastructureAssetsPath)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplicationDiagnostics.FromException<ApplicationInfo>(ex));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ownedResource?.Dispose();
    }

    public static PenguinToolsApplication CreateDefault(PenguinToolsApplicationOptions? options = null)
    {
        AssetStore? assetStore = null;
        try
        {
            options ??= new PenguinToolsApplicationOptions();
            var assetsDirectory = AssetPaths.Resolve(options.ExternalAssetsDirectory);
            var paths = ApplicationPaths.Create();
            assetStore = new AssetStore(assetsDirectory, paths.TempWorkPath);
            var assetProvider = new InfrastructureAssetProvider(assetStore);
            using var assetsStream = assetStore.OpenRead(InfrastructureResourceNames.AssetsJson);
            var userAssetsPath = string.IsNullOrWhiteSpace(options.UserAssetsPath)
                ? null
                : Path.GetFullPath(options.UserAssetsPath.Trim());
            var assets = new AssetManager(assetsStream, userAssetsPath);
            var audio = new AudioService(assetsDirectory);
            var images = new ImageService(
                Path.Combine(assetsDirectory, "texconv", "texconv.exe"),
                Path.Combine(assetsDirectory, "image-tools", "image-tools.exe"),
                paths.TempWorkPath);
            var dependencies = new PenguinToolsApplicationDependencies(
                paths, assetStore, assets, audio, images, assetProvider);
            return new PenguinToolsApplication(dependencies, assetStore);
        }
        catch
        {
            assetStore?.Dispose();
            throw;
        }
    }

    private async Task<OperationResult<T>> GuardAsync<T>(Func<Task<OperationResult<T>>> action)
    {
        ThrowIfDisposed();
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApplicationDiagnostics.FromException<T>(ex);
        }
    }

    private Task<OperationResult<IReadOnlyList<OptionBook>>> ScanBooksAsync(
        string input, IReadOnlyList<ChartFileFormat> discovery, int batchSize, string workingDirectory,
        IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        var diagnostics = new DiagnosticCollector();
        return ChartScanner.ScanDirectoryAsync(_dependencies.Assets, _mediaValidator, input, discovery,
            new OptionExportProcessContext(diagnostics, batchSize, workingDirectory, cancellationToken, progress));
    }

    private static OptionScanConfig CreateScanConfig(OptionDocument document)
    {
        return new OptionScanConfig(
            document.OptionName,
            document.OptionId,
            document.ConvertChart,
            document.ChartFileDiscovery.Select(ToChartFormatName).ToArray(),
            document.ConvertAudio,
            document.ConvertJacket,
            document.ConvertBackground,
            document.HcaEncryptionKey,
            document.GenerateEventXml,
            document.CustomReleaseTagXml,
            document.SelectedReleaseTagId,
            document.CustomReleaseTagId,
            document.CustomReleaseTagTitleName,
            document.CustomGenre,
            document.SelectedGenreId,
            document.CustomGenreId,
            document.CustomGenreName,
            document.OverrideChartGenre,
            document.UltimaEventId,
            document.WeEventId,
            document.BatchSize)
        { ConvertWall = document.ConvertWall };
    }

    private static (OptionScanResult Result, DiagnosticSnapshot UnmatchedDiagnostics) CreateScanResult(
        string input,
        IReadOnlyList<ChartFormat> discovery,
        int batchSize,
        IReadOnlyList<OptionBook> snapshots,
        DiagnosticSnapshot diagnostics,
        string? configPath = null,
        OptionScanConfig? config = null)
    {
        var remaining = diagnostics.Diagnostics.Select((value, index) => (value, index)).ToList();
        var books = snapshots.OrderBy(x => x.BookMeta.Id).ThenBy(x => x.Title, StringComparer.Ordinal).Select(book =>
        {
            var charts = book.Difficulties.Values.OrderBy(x => x.Difficulty).Select(item =>
            {
                var matches = remaining.Where(x => PathsEqual(input, item.Meta.FilePath, x.value.Path)).ToArray();
                foreach (var match in matches)
                {
                    remaining.Remove(match);
                }

                var meta = item.Meta;
                return new OptionScanDifficulty(item.Difficulty.ToString(), meta.MgxcId, meta.Id,
                    meta.Title, meta.Artist, meta.Designer, meta.Level, meta.MainBpm, meta.MainTil, meta.IsMain,
                    meta.FilePath, ApplicationEntry.From(meta.WeTag), (int)meta.WeDifficulty,
                    ChartMetadata.GetStarDifficultyLabel(meta.WeDifficulty), meta.SortName, ApplicationEntry.From(meta.Genre),
                    meta.UnlockEventId, meta.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), meta.JacketFilePath, meta.BgmFilePath,
                    meta.BgmPreviewStart, meta.BgmPreviewStop, meta.BgmManualOffset, meta.BgmRealOffset,
                    meta.BgmEnableBarOffset, meta.BgmInitialBpm, meta.BgmInitialNumerator, meta.BgmInitialDenominator,
                    meta.IsCustomStage, meta.StageId, meta.BgiFilePath, ApplicationEntry.From(meta.NotesFieldLine),
                    ApplicationEntry.From(meta.Stage),
                    matches.Select(x => ToApplicationDiagnostic(x.value)).ToArray())
                {
                    WallFilePath = meta.WallFilePath,
                    FullWallFilePath = meta.FullWallFilePath,
                    WallId = meta.EffectiveWallId,
                    ExplicitWallId = meta.WallId,
                    WallWidth = meta.WallWidth
                };
            }).ToArray();
            var main = book.MainDifficulty.ToString();
            var mainMeta = book.BookMeta;
            return new OptionScanBook(mainMeta.Id, book.Title, mainMeta.Artist, main, mainMeta.SortName,
                ApplicationEntry.From(mainMeta.Genre), mainMeta.UnlockEventId,
                mainMeta.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), book.IsCustomStage, book.StageId, mainMeta.BgiFilePath,
                ApplicationEntry.From(book.NotesFieldLine), ApplicationEntry.From(book.Stage),
                ApplicationEntry.From(mainMeta.WeTag), (int)mainMeta.WeDifficulty,
                ChartMetadata.GetStarDifficultyLabel(mainMeta.WeDifficulty), mainMeta.JacketFilePath, mainMeta.BgmFilePath,
                mainMeta.BgmPreviewStart, mainMeta.BgmPreviewStop, mainMeta.BgmManualOffset, mainMeta.BgmRealOffset,
                mainMeta.BgmEnableBarOffset, mainMeta.BgmInitialBpm, mainMeta.BgmInitialNumerator,
                mainMeta.BgmInitialDenominator, charts)
            {
                WallFilePath = mainMeta.WallFilePath,
                FullWallFilePath = mainMeta.FullWallFilePath,
                WallId = mainMeta.EffectiveWallId,
                ExplicitWallId = mainMeta.WallId,
                WallWidth = mainMeta.WallWidth
            };
        }).ToArray();
        var unmatched = remaining.Select(x => x.value).ToArray();
        var result = new OptionScanResult(input, discovery.Select(ToChartFormatName).ToArray(), batchSize, books,
            unmatched.Select(ToApplicationDiagnostic).ToArray(), configPath, config)
        {
            SkippedCharts = SkippedVersionCharts(diagnostics)
        };
        return (result, DiagnosticSnapshot.Create(unmatched));
    }

    private static ApplicationDiagnostic[] SkippedVersionCharts(DiagnosticSnapshot diagnostics) =>
        diagnostics.Diagnostics.Where(d => d.Message.Key == MsgKeys.Warn_Chart_skipped_version)
            .OrderBy(d => d.Path, StringComparer.Ordinal).Select(ToApplicationDiagnostic).ToArray();

    private static ApplicationDiagnostic ToApplicationDiagnostic(Diagnostic value)
    {
        return new ApplicationDiagnostic(
            SeverityCodes.ToCode(value.Severity),
            value.Message,
            value.Path,
            value.Line,
            value.Time,
            DiagnosticTime.TryGetPosition(value),
            DiagnosticTargetSerializer.ToJsonElement(value.Target, value));
    }

    private static bool PathsEqual(string root, string chartPath, string? diagnosticPath)
    {
        if (string.IsNullOrWhiteSpace(diagnosticPath))
        {
            return false;
        }

        static string Normalize(string rootPath, string path)
        {
            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(rootPath, path));
        }

        return string.Equals(Normalize(root, chartPath), Normalize(root, diagnosticPath),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static void ApplyOverrides(OptionDocument document, OptionBuildOverrides? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.OptionName is not null)
        {
            document.OptionName = value.OptionName;
        }

        if (value.ChartFileDiscovery is not null)
        {
            document.ChartFileDiscovery = [.. value.ChartFileDiscovery.Select(ToWorkflow)];
        }

        if (value.BatchSize is { } batchSize)
        {
            document.BatchSize = batchSize;
        }

        if (value.ConvertChart is { } convertChart)
        {
            document.ConvertChart = convertChart;
        }

        if (value.ConvertAudio is { } convertAudio)
        {
            document.ConvertAudio = convertAudio;
        }

        if (value.ConvertJacket is { } convertJacket)
        {
            document.ConvertJacket = convertJacket;
        }

        if (value.ConvertWall is { } convertWall)
        {
            document.ConvertWall = convertWall;
        }

        if (value.ConvertBackground is { } convertBackground)
        {
            document.ConvertBackground = convertBackground;
        }

        if (value.HcaEncryptionKey is { } key)
        {
            document.HcaEncryptionKey = key;
        }

        ApplyReleaseOverrides(document, value);
    }

    private static void ApplyReleaseOverrides(OptionDocument document, OptionBuildOverrides value)
    {
        if (value.GenerateEventXml is { } eventXml)
        {
            document.GenerateEventXml = eventXml;
        }

        if (value.CustomReleaseTagXml is { } customReleaseXml)
        {
            document.CustomReleaseTagXml = customReleaseXml;
        }

        if (value.SelectedReleaseTagId is { } selectedReleaseId)
        {
            document.SelectedReleaseTagId = selectedReleaseId;
        }

        if (value.CustomReleaseTagId is { } customReleaseId)
        {
            document.CustomReleaseTagId = customReleaseId;
        }

        if (value.CustomReleaseTagTitleName is not null)
        {
            document.CustomReleaseTagTitleName = value.CustomReleaseTagTitleName;
        }

        if (value.CustomGenre is { } customGenre)
        {
            document.CustomGenre = customGenre;
        }

        if (value.SelectedGenreId is { } selectedGenreId)
        {
            document.SelectedGenreId = selectedGenreId;
        }

        if (value.CustomGenreId is { } customGenreId)
        {
            document.CustomGenreId = customGenreId;
        }

        if (value.CustomGenreName is not null)
        {
            document.CustomGenreName = value.CustomGenreName;
        }

        if (value.OverrideChartGenre is { } overrideChartGenre)
        {
            document.OverrideChartGenre = overrideChartGenre;
        }

        if (value.UltimaEventId is { } ultima)
        {
            document.UltimaEventId = ultima;
        }

        if (value.WeEventId is { } we)
        {
            document.WeEventId = we;
        }
    }

    private MusicExportContext CreateExportContext()
    {
        return new MusicExportContext(
            _dependencies.Assets, _dependencies.Audio, _dependencies.Images, _dependencies.AssetStore, _dependencies.AssetProvider);
    }

    internal static IReadOnlyList<OptionBook> ApplyMainDifficultyOverrides(
        IReadOnlyList<OptionBook> snapshots,
        IReadOnlyList<OptionMainDifficultyOverride>? overrides)
    {
        if (overrides is null or { Count: 0 })
        {
            return snapshots;
        }

        foreach (var item in overrides)
        {
            ResourceIds.ValidateSong(item.SongId);
        }
        var lookup = overrides
            .GroupBy(x => x.SongId)
            .ToDictionary(group => group.Key, group => group.Last().MainDifficulty);

        return snapshots.Select(book => ApplyMainDifficultyOverride(book, lookup)).ToArray();
    }

    private static OptionBook ApplyMainDifficultyOverride(
        OptionBook book,
        Dictionary<int, string> overrides)
    {
        if (book.BookMeta.Id is not { } songId || !overrides.TryGetValue(songId, out var requested))
        {
            return book;
        }

        if (!TryParseDifficultyName(requested, out var targetDifficulty))
        {
            return book;
        }

        foreach (var item in book.Difficulties.Values)
        {
            item.Meta.IsMain = item.Difficulty == targetDifficulty;
        }

        if (!book.Difficulties.TryGetValue(targetDifficulty, out var mainItem))
        {
            mainItem = book.Difficulties.Values.OrderByDescending(x => x.Difficulty).First();
        }

        return new OptionBook(
            mainItem.Difficulty,
            book.Difficulties);
    }

    private static bool TryParseDifficultyName(string value, out Difficulty difficulty)
    {
        var normalized = value.Trim();
        if (string.Equals(normalized, "World's End", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Worlds End", StringComparison.OrdinalIgnoreCase))
        {
            normalized = nameof(Difficulty.WorldsEnd);
        }

        return Enum.TryParse(normalized, true, out difficulty);
    }

    private static AudioConvertResult CreateAudioConvertResult(string input, string output, int songId)
    {
        var xml = new CueFileXml(songId);
        var directory = Path.Combine(output, xml.DataName);
        return new AudioConvertResult(input, input, output,
        [
            new ApplicationArtifact("cue-file.xml", Path.Combine(directory, "CueFile.xml")),
            new ApplicationArtifact("audio.acb", Path.Combine(directory, xml.AcbFile)),
            new ApplicationArtifact("audio.awb", Path.Combine(directory, xml.AwbFile))
        ]);
    }

    private static ChartFormat FromWorkflow(ChartFileFormat value)
    {
        return value switch
        {
            ChartFileFormat.Mgxc => ChartFormat.Mgxc,
            ChartFileFormat.Ugc => ChartFormat.Ugc,
            ChartFileFormat.Sus => ChartFormat.Sus,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }

    private static string ToChartFormatName(ChartFileFormat value)
    {
        return value switch
        {
            ChartFileFormat.Mgxc => "mgxc",
            ChartFileFormat.Ugc => "ugc",
            ChartFileFormat.Sus => "sus",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }

    private static string ToChartFormatName(ChartFormat value)
    {
        return value switch
        {
            ChartFormat.Mgxc => "mgxc",
            ChartFormat.Ugc => "ugc",
            ChartFormat.Sus => "sus",
            ChartFormat.C2s => "c2s",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }

    private static ChartFileFormat ToWorkflow(ChartFormat value)
    {
        return value switch
        {
            ChartFormat.Mgxc => ChartFileFormat.Mgxc,
            ChartFormat.Ugc => ChartFileFormat.Ugc,
            ChartFormat.Sus => ChartFileFormat.Sus,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }

    private static AudioRequestOverrides ToWorkflow(AudioOverrides? value)
    {
        return value is null
            ? AudioRequestOverrides.Default
            : new AudioRequestOverrides(OptionalFullPath(value.WorkingAudioPath),
                value.HcaEncryptionKey);
    }

    private static StageRequestOverrides ToWorkflow(StageOverrides? value)
    {
        return value is null
            ? StageRequestOverrides.None
            : new StageRequestOverrides(OptionalFullPath(value.BackgroundPath),
                value.EffectPaths?.Select(OptionalFullPath).ToArray() ?? [null, null, null, null], value.StageId,
                value.NoteFieldLaneId, value.NoteFieldLaneName, value.NoteFieldLaneData);
    }

    private static IReadOnlyList<ApplicationArtifact> CreateStageArtifacts(string output, Meta meta,
        StageRequestOverrides overrides, out string? stageName)
    {
        stageName = null;
        var stageId = overrides.StageId ?? meta.StageId;
        if (!MusicMediaExporter.ShouldBuildStage(meta, overrides) || stageId is not { } id)
        {
            return [];
        }

        var xml = new StageXml(id, MusicMediaExporter.CreateNoteFieldEntry(meta.NotesFieldLine,
            overrides.NoteFieldLaneId, overrides.NoteFieldLaneName, overrides.NoteFieldLaneData));
        var directory = Path.Combine(output, xml.DataName);
        stageName = xml.Name.Str;
        return
        [
            new ApplicationArtifact("stage.xml", Path.Combine(directory, "Stage.xml")),
            new ApplicationArtifact("stage.base-afb", Path.Combine(directory, xml.BaseFile)),
            new ApplicationArtifact("stage.notes-field-afb", Path.Combine(directory, xml.NotesFieldFile))
        ];
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
