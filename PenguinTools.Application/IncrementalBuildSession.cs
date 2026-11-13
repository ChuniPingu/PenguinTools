using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;
using PenguinTools.Workflow;

namespace PenguinTools.Application;

/// <summary>Builds independent option snapshots from one document while retaining verified media artifacts.</summary>
public sealed class IncrementalBuildSession : IDisposable
{
    private readonly PenguinToolsApplicationDependencies _dependencies;
    private readonly Action _checkApplication;
    private readonly SessionMediaValidator _validator;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string _workingDirectory;
    private BuildState? _state;
    private long _revision;
    private long _nextBuild;
    private string? _cacheIdentity;
    private bool _disposed;

    private sealed record ArtifactState(string RelativePath, string Kind, string Hash);
    private sealed record BuildState(string DocumentId, string BaseDirectory, string? CacheIdentity, string Directory,
        OptionConversionCache Cache, IReadOnlyList<ArtifactState> Artifacts, string? InputHash,
        OptionChartConversionCache ChartCache, string? ReuseKey, IReadOnlyDictionary<string, string>? Dependencies,
        IncrementalBuildResult Result, DiagnosticSnapshot Diagnostics);

    private sealed class BuildAssetStore(IAssetStore source, string tempDirectory) : IAssetStore
    {
        private long _nextFile;
        public string AssetDirectory => source.AssetDirectory;
        public string TempWorkPath => tempDirectory;
        public bool HasAsset(string assetName) => source.HasAsset(assetName);
        public string GetAssetPath(string assetName) => source.GetAssetPath(assetName);
        public Stream OpenRead(string assetName) => source.OpenRead(assetName);
        public string GetTempPath(string fileName)
        {
            Directory.CreateDirectory(tempDirectory);
            return Path.Combine(tempDirectory, $"t{Interlocked.Increment(ref _nextFile):x}{Path.GetExtension(fileName)}");
        }
        public void Dispose() { }
    }

    internal IncrementalBuildSession(PenguinToolsApplicationDependencies dependencies, Action checkApplication)
    {
        _dependencies = dependencies;
        _checkApplication = checkApplication;
        _validator = new(new MediaValidator(dependencies.Audio, dependencies.Images));
        // Retain the GUID's full entropy while leaving room for native tools' working paths.
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _workingDirectory = Path.Combine(dependencies.Paths.TempWorkPath, "b-" + token);
        Directory.CreateDirectory(_workingDirectory);
    }

    public async Task<OperationResult<IncrementalBuildResult>> PrepareAsync(IncrementalBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _checkApplication();
            return await PrepareCoreAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApplicationDiagnostics.FromException<IncrementalBuildResult>(ex);
        }
        finally
        {
            await _validator.CompletePendingAsync();
            _gate.Release();
        }
    }

    private async Task<OperationResult<IncrementalBuildResult>> PrepareCoreAsync(IncrementalBuildRequest request,
        CancellationToken ct, bool retryChangedInputs = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentId);
        var persistent = PersistentMediaCache.FromRequest(request);
        _validator.BeginRequest(request.CacheIdentity != _cacheIdentity);
        _cacheIdentity = request.CacheIdentity;
        var baseDirectory = Path.GetFullPath(request.BaseDirectory);
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        if (!Directory.Exists(baseDirectory))
        {
            return ApplicationDiagnostics.Failure<IncrementalBuildResult>(Msg.Key(MsgKeys.App_Input_directory_not_found), baseDirectory);
        }
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new ArgumentException("The snapshot output directory must be absent or empty.", nameof(request));
        }
        if (request.Format is not (ChartFormat.Ugc or ChartFormat.Mgxc or ChartFormat.Sus) ||
            request.Content is not null && request.Format != ChartFormat.Ugc)
        {
            return ApplicationDiagnostics.Failure<IncrementalBuildResult>(
                Msg.Create(MsgKeys.Error_Chart_conversion_unsupported, request.Format.ToString()));
        }
        if ((request.Content is null) == string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new ArgumentException("Provide either UGC content or an input path.", nameof(request));
        }

        var configPath = Path.Combine(baseDirectory, "options.json");
        var configHash = await HashConfigurationAsync(configPath, ct);
        var document = File.Exists(configPath) ? await OptionConfiguration.LoadAsync(configPath, ct) : new OptionDocument();
        PenguinToolsApplication.ApplyOverrides(document, request.Overrides);
        if (document.OptionName.Length != 4 || document.OptionName is "." or ".." ||
            document.OptionName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return ApplicationDiagnostics.Failure<IncrementalBuildResult>(Msg.Key(MsgKeys.App_Option_name_required));
        }
        if (document.BatchSize is 0 or < -1)
        {
            return ApplicationDiagnostics.Failure<IncrementalBuildResult>(Msg.Key(MsgKeys.App_Batch_size_invalid));
        }
        if (!document.HasExportableWork())
        {
            return ApplicationDiagnostics.Failure<IncrementalBuildResult>(Msg.Key(MsgKeys.App_No_export_actions_enabled));
        }

        var inputHash = await HashInputAsync(request, baseDirectory, ct);
        var previous = _state is { } state && state.DocumentId == request.DocumentId &&
            state.BaseDirectory == baseDirectory && state.CacheIdentity == request.CacheIdentity ? state : null;
        var reuseKey = request.Content is not null && configHash == await HashConfigurationAsync(configPath, ct)
            ? configHash + ":" + DateTime.Today.Ticks + ":" + JsonSerializer.Serialize(request.Overrides, ApplicationJsonContext.Default.OptionBuildOverrides)
            : null;
        if (previous is { Dependencies: { } dependencies } && reuseKey is not null &&
            reuseKey == previous.ReuseKey && inputHash == previous.InputHash &&
            await SessionMediaValidator.DependenciesMatchAsync(dependencies, ct))
        {
            var directory = Path.Combine(outputDirectory, document.OptionName);
            var manifest = CreateManifest(previous.Artifacts, previous.Artifacts, directory);
            var relativeChart = previous.Artifacts.FirstOrDefault(item => item.Kind == "chart")?.RelativePath;
            var duplicate = previous.Result with
            {
                Revision = _revision + 1,
                OptionDirectory = directory,
                ChartPath = relativeChart is null ? null : Path.Combine(directory, relativeChart),
                Artifacts = manifest,
                Changes = []
            };
            await PublishAsync(previous.Directory, outputDirectory, document.OptionName, ct);
            _revision++;
            if (persistent is not null)
            {
                await persistent.StoreAsync(previous.Cache, previous.Directory, MediaArtifacts(previous.Artifacts), ct);
            }
            return OperationResult<IncrementalBuildResult>.Success(duplicate).WithDiagnostics(previous.Diagnostics);
        }
        var parsed = request.Content is not null
            ? await new UgcParser(new UgcParseRequest(Path.Combine(baseDirectory, "document.ugc"), _dependencies.Assets, request.Content), _validator).ParseAsync(ct)
            : await new ChartOperations(_dependencies.Assets, _validator).ParseChartAsync(ResolveInput(request, baseDirectory), ct);
        if (!parsed.Succeeded)
        {
            return OperationResult<IncrementalBuildResult>.Failure().WithDiagnostics(parsed.Diagnostics);
        }
        if (request.Content is null && inputHash != await HashInputAsync(request, baseDirectory, ct))
        {
            inputHash = null;
        }

        var candidate = Path.Combine(_workingDirectory, $"{checked(++_nextBuild):x}");
        var mediaWork = Path.Combine(_workingDirectory, "m");
        var cache = previous?.Cache.ForSnapshot(previous.Directory, candidate)
            ?? (persistent is null ? null : await persistent.RestoreAsync(candidate, ct)) ?? new OptionConversionCache();
        var verifiedInputs = _validator.CaptureDependencies();
        if (verifiedInputs is not null)
        {
            cache.SetVerifiedInputs(verifiedInputs);
        }
        document.ConversionCache = cache;
        var chartCache = inputHash is not null && inputHash == previous?.InputHash
            ? previous.ChartCache.Fork() : new OptionChartConversionCache();
        var settings = document.ToExportSettings() with { ChartCache = chartCache };
        var chart = parsed.Value;
        var books = PenguinToolsApplication.ApplyMainDifficultyOverrides(
            [new OptionBook(chart.Meta.Difficulty, new Dictionary<PenguinTools.Core.Metadata.Difficulty, OptionDifficulty>
            { [chart.Meta.Difficulty] = new(chart) })], request.Overrides?.MainDifficulties);
        using var assetStore = new BuildAssetStore(_dependencies.AssetStore, mediaWork);
        var context = new MusicExportContext(_dependencies.Assets, _dependencies.Audio, _dependencies.Images,
            assetStore, _dependencies.AssetProvider);
        Directory.CreateDirectory(candidate);
        try
        {
            var exported = await OptionExporter.ExportAsync(context, settings, ExportOutputPaths.FromOptionDirectory(candidate),
                books, baseDirectory, ct);
            var diagnostics = parsed.Diagnostics.Merge(exported.Diagnostics);
            if (!exported.Succeeded)
            {
                return OperationResult<IncrementalBuildResult>.Failure().WithDiagnostics(diagnostics);
            }

            var artifacts = await ReadArtifactsAsync(candidate, cache, ct);
            var optionDirectory = Path.Combine(outputDirectory, document.OptionName);
            var manifest = CreateManifest(artifacts, previous?.Artifacts ?? [], optionDirectory);
            var chartPath = artifacts.FirstOrDefault(item => item.Kind == "chart")?.RelativePath;
            var value = new IncrementalBuildResult(_revision + 1, optionDirectory,
                chartPath is null ? null : Path.Combine(optionDirectory, chartPath),
                ChartMetadata.CreateChartSummary(chart.Meta), ChartMetadata.CreateChartConversionMetadata(chart.Meta),
                manifest, manifest.Where(item => item.Status != "reused").Select(item => item.Kind).Distinct().Order().ToArray());
            var observed = _validator.CaptureDependencies();
            if ((observed is not null && !await SessionMediaValidator.DependenciesMatchAsync(observed, ct)) ||
                configHash != await HashConfigurationAsync(configPath, ct))
            {
                DeleteOwnedDirectory(candidate);
                candidate = "";
                if (Directory.Exists(mediaWork))
                {
                    DeleteOwnedDirectory(mediaWork);
                }
                return retryChangedInputs
                    ? await PrepareCoreAsync(request, ct, retryChangedInputs: false)
                    : ApplicationDiagnostics.Failure<IncrementalBuildResult>(Msg.Key(MsgKeys.App_Inputs_changed_during_export));
            }
            if (observed is null)
            {
                reuseKey = null;
            }
            await PublishAsync(candidate, outputDirectory, document.OptionName, ct);

            var retired = _state;
            _state = new(request.DocumentId, baseDirectory, request.CacheIdentity, candidate, cache, artifacts, inputHash, chartCache,
                reuseKey, observed, value, diagnostics);
            _revision++;
            candidate = "";
            if (retired is not null)
            {
                DeleteOwnedDirectory(retired.Directory);
            }
            if (persistent is not null)
            {
                await persistent.StoreAsync(cache, _state.Directory, MediaArtifacts(artifacts), ct);
            }
            return OperationResult<IncrementalBuildResult>.Success(value).WithDiagnostics(diagnostics);
        }
        finally
        {
            if (Directory.Exists(mediaWork))
            {
                DeleteOwnedDirectory(mediaWork);
            }
            if (candidate.Length > 0)
            {
                DeleteOwnedDirectory(candidate);
            }
        }
    }

    private static string ResolveInput(IncrementalBuildRequest request, string baseDirectory)
    {
        var input = Path.GetFullPath(request.InputPath!, baseDirectory);
        if (!Path.GetExtension(input).Equals("." + request.Format, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Input extension must match the requested format.", nameof(request));
        }
        return input;
    }

    private static async Task<string> HashInputAsync(IncrementalBuildRequest request, string baseDirectory, CancellationToken ct)
    {
        if (request.Content is not null)
        {
            return "ugc:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Content)));
        }
        var path = ResolveInput(request, baseDirectory);
        await using var stream = File.OpenRead(path);
        return path + ":" + Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static async Task<string> HashConfigurationAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return "missing";
        }
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static async Task<IReadOnlyList<ArtifactState>> ReadArtifactsAsync(string directory, OptionConversionCache cache, CancellationToken ct)
    {
        var artifacts = new List<ArtifactState>();
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, file);
            var hash = cache.VerifiedOutputHash(file);
            if (hash is null)
            {
                await using var stream = File.OpenRead(file);
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            }
            artifacts.Add(new(relative, ArtifactKind(relative), hash));
        }
        return artifacts;
    }

    private static string ArtifactKind(string relative)
    {
        var path = relative.Replace('\\', '/');
        if (path.EndsWith(".c2s", StringComparison.OrdinalIgnoreCase))
        {
            return "chart";
        }
        if (path.StartsWith("cueFile/", StringComparison.OrdinalIgnoreCase))
        {
            return "audio";
        }
        if (path.StartsWith("stage/", StringComparison.OrdinalIgnoreCase))
        {
            return "stage";
        }
        if (path.StartsWith("ddsFieldWall/", StringComparison.OrdinalIgnoreCase) || path == "wallbind.json")
        {
            return "wall";
        }
        if (path.StartsWith("music/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
        {
            return "jacket";
        }
        return "metadata";
    }

    private static PersistentMediaArtifact[] MediaArtifacts(IReadOnlyList<ArtifactState> artifacts) => artifacts
        .Where(item => item.Kind is "audio" or "jacket" or "stage" or "wall")
        .Select(item => new PersistentMediaArtifact(item.RelativePath, item.Hash)).ToArray();

    private static List<IncrementalArtifact> CreateManifest(IReadOnlyList<ArtifactState> current,
        IReadOnlyList<ArtifactState> previous, string optionDirectory)
    {
        var old = previous.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var result = new List<IncrementalArtifact>();
        foreach (var item in current)
        {
            var reused = old.Remove(item.RelativePath, out var preceding) && preceding.Hash == item.Hash;
            result.Add(new(item.Kind, Path.Combine(optionDirectory, item.RelativePath), item.Hash, reused ? "reused" : "changed"));
        }
        result.AddRange(old.Values.Select(item => new IncrementalArtifact(item.Kind,
            Path.Combine(optionDirectory, item.RelativePath), item.Hash, "removed")));
        return result;
    }

    private static async Task PublishAsync(string source, string outputDirectory, string optionName, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var destinationRoot = Path.Combine(outputDirectory, optionName);
        if (File.Exists(destinationRoot) || Directory.Exists(destinationRoot))
        {
            throw new IOException("The snapshot option directory already exists.");
        }
        Directory.CreateDirectory(destinationRoot);
        try
        {
            // The caller consumes this tree after the successful response. Copying
            // directly avoids Windows directory-rename locks on recently written files.
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(destinationRoot, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = File.OpenRead(file);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
            }
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            try
            {
                Directory.Delete(destinationRoot, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private void DeleteOwnedDirectory(string directory)
    {
        if (!Path.GetFullPath(directory).StartsWith(_workingDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Build cache directory is outside the session.");
        }
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _validator.Dispose();
            Directory.Delete(_workingDirectory, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
