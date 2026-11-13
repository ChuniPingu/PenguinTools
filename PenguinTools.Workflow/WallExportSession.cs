using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

public sealed record WallExportSummary(IReadOnlyList<WallAsset> Assets, bool ManifestChanged);

/// <summary>Prepares resources in parallel and publishes one package's bindings with its completed resources.</summary>
public sealed class WallExportSession
{
    private readonly MusicExportContext _services;
    private readonly string _assetRoot;
    private readonly string _manifestPath;
    private readonly OptionConversionCache? _cache;
    private readonly bool _ignoreCache;
    private volatile bool _incomplete;
    private readonly HashSet<int> _blockedIds = [];
    private readonly ConcurrentDictionary<int, Lazy<Task<OperationResult<PreparedExport>>>> _jobs = new();
    private readonly ConcurrentDictionary<int, PreparedExport> _completed = new();
    private readonly ConcurrentDictionary<int, WallBinding> _bindings = new();

    private sealed record PreparedExport(WallAsset Asset, PreparedWallAsset? Prepared, OptionCachedConversion Cache);

    public WallExportSession(MusicExportContext services, string assetRoot, string manifestPath,
        OptionConversionCache? cache = null, bool ignoreCache = false)
    {
        _services = services;
        _assetRoot = assetRoot;
        _manifestPath = manifestPath;
        _cache = cache;
        _ignoreCache = ignoreCache;
    }

    public async Task CheckConflictsAsync(IEnumerable<Meta> metadata, IDiagnosticSink diagnostics,
        CancellationToken ct = default)
    {
        var groups = metadata.Where(meta => !meta.WallMetadataInvalid && meta.WallImageValid &&
                !string.IsNullOrWhiteSpace(meta.WallFilePath) && meta.EffectiveWallId.HasValue)
            .GroupBy(meta => meta.EffectiveWallId!.Value).Where(group => group.Count() > 1);
        foreach (var group in groups)
        {
            string? first = null;
            var conflicting = false;
            foreach (var meta in group)
            {
                try
                {
                    await using var stream = File.OpenRead(meta.FullWallFilePath);
                    var fingerprint = meta.WallWidth + ":" + Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                    if (first is not null && first != fingerprint)
                    {
                        conflicting = true;
                        _blockedIds.Add(group.Key);
                        _incomplete = true;
                    }
                    first ??= fingerprint;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _blockedIds.Add(group.Key);
                    _incomplete = true;
                    diagnostics.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Invalid_wall_image),
                        meta.FullWallFilePath)
                    { Target = ex });
                }
            }
            if (conflicting)
            {
                diagnostics.Report(new Diagnostic(Severity.Error, Msg.Create(MsgKeys.Error_Wall_conflict, group.Key))
                {
                    Target = group.Select(meta => meta.FilePath).ToArray()
                });
            }
        }
    }

    public async Task QueueAsync(Meta meta, WallRequestOverrides overrides, IDiagnosticSink diagnostics,
        bool required = false, CancellationToken ct = default)
    {
        if (!required && !overrides.HasBuildInputs && (!meta.WallImageValid || meta.WallMetadataInvalid))
        {
            _incomplete = true;
            return;
        }

        var source = overrides.InputPath ?? meta.FullWallFilePath;
        if (string.IsNullOrWhiteSpace(source))
        {
            if (required || overrides.HasBuildInputs)
            {
                diagnostics.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Wall_source_required), meta.FilePath));
                _incomplete = true;
            }
            return;
        }

        int songId;
        try
        {
            songId = ResourceIds.RequireSong(meta);
        }
        catch (DiagnosticException ex)
        {
            diagnostics.Report(ex.ToDiagnostic().WithPathFallback(meta.FilePath));
            _incomplete = true;
            return;
        }

        var id = overrides.WallId ?? meta.WallId ?? (songId + CustomAssetIds.SongOffset);
        var width = overrides.Width ?? meta.WallWidth;
        if (id < 0 || !Enum.IsDefined(width))
        {
            var key = id < 0 ? MsgKeys.Error_Wall_id_invalid : MsgKeys.Error_Wall_width_invalid;
            diagnostics.Report(new PathDiagnostic(Severity.Error, Msg.Key(key), meta.FilePath));
            _incomplete = true;
            return;
        }
        if (_blockedIds.Contains(id))
        {
            return;
        }

        var request = new WallBuildRequest(_services.Assets, source, id, width, _assetRoot);
        var result = await _jobs.GetOrAdd(id,
            _ => new Lazy<Task<OperationResult<PreparedExport>>>(() => PrepareAsync(request, ct))).Value;
        foreach (var diagnostic in result.Diagnostics.Diagnostics)
        {
            diagnostics.Report(diagnostic);
        }
        if (!result.Succeeded)
        {
            _incomplete = true;
            return;
        }

        _completed[id] = result.Value;
        _bindings[songId] = new WallBinding(songId, id, width);
    }

    private async Task<OperationResult<PreparedExport>> PrepareAsync(WallBuildRequest request, CancellationToken ct)
    {
        var cached = await OptionConversionCacheArtifacts.CreateWallAsync(request, ct, _cache);
        if (!_ignoreCache && await OptionConversionCacheValidator.IsHitAsync(_cache, cached.Key, cached.State, cached.Outputs, ct))
        {
            var xml = new DdsFieldWallXml(request.WallId);
            var folder = Path.Combine(_assetRoot, xml.DataName);
            return OperationResult<PreparedExport>.Success(new PreparedExport(
                new WallAsset(xml.Name, folder, Path.Combine(folder, "DDSFieldWall.xml"), Path.Combine(folder, xml.Image)),
                null, cached));
        }

        var prepared = await new WallConverter(request, _services.Images).PrepareAsync(ct);
        return prepared.Succeeded
            ? OperationResult<PreparedExport>.Success(new PreparedExport(prepared.Value.Asset, prepared.Value, cached))
                .WithDiagnostics(prepared.Diagnostics)
            : OperationResult<PreparedExport>.Failure().WithDiagnostics(prepared.Diagnostics);
    }

    public async Task<OperationResult<WallExportSummary>> PublishAsync(bool batchComplete = true,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!batchComplete || _incomplete)
        {
            return OperationResult<WallExportSummary>.Success(new WallExportSummary([], false));
        }

        var complete = _completed.Values.OrderBy(value => value.Asset.Name.Id).ToArray();
        var prepared = complete.Where(value => value.Prepared is not null).Select(value => value.Prepared!).ToArray();
        try
        {
            var document = new WallBindingsDocument { Bindings = _bindings.Values.OrderBy(binding => binding.MusicId).ToList() };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, WallBindingsJsonContext.Default.WallBindingsDocument);
            await WallConverter.PublishAsync(prepared, _manifestPath, bytes, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var diagnostics = new DiagnosticCollector();
            diagnostics.Report(ex);
            return OperationResult<WallExportSummary>.Failure().WithDiagnostics(diagnostics);
        }

        foreach (var cached in complete.Select(value => value.Cache))
        {
            await OptionConversionCacheValidator.StoreAsync(_cache, cached.Key, cached.State, cached.Outputs, ct);
        }
        return OperationResult<WallExportSummary>.Success(new WallExportSummary(
            complete.Select(value => value.Asset).ToArray(), true));
    }
}
