using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Workflow;
using static PenguinTools.Application.RequestPaths;

namespace PenguinTools.Application;

public sealed partial class PenguinToolsApplication
{
    public Task<OperationResult<WallBuildResult>> BuildWallAsync(WallBuildRequest request,
        CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        var input = FullPath(request.InputPath);
        var parsed = await _charts.ParseChartAsync(input, cancellationToken);
        if (!parsed.Succeeded)
        {
            return OperationResult<WallBuildResult>.Failure().WithDiagnostics(parsed.Diagnostics);
        }

        var meta = parsed.Value.Meta;
        if (request.SongId is { } songId)
        {
            meta.Id = songId;
        }
        var result = await BuildWallCoreAsync(input, FullPath(request.OutputDirectory), meta,
            ToWorkflow(request.Overrides), cancellationToken);
        return result.WithDiagnostics(parsed.Diagnostics.Merge(result.Diagnostics));
    });

    public Task<OperationResult<WallBuildResult>> BuildWallFilesAsync(WallFilesBuildRequest request,
        CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        var input = FullPath(request.InputPath);
        var meta = new Meta { Id = request.SongId, FilePath = input, WallFilePath = input };
        return await BuildWallCoreAsync(input, FullPath(request.OutputDirectory), meta,
            ToWorkflow(request.Overrides), cancellationToken);
    });

    private async Task<OperationResult<WallBuildResult>> BuildWallCoreAsync(string input, string output,
        Meta meta, WallRequestOverrides overrides, CancellationToken ct)
    {
        var diagnostics = new DiagnosticCollector();
        var walls = new WallExportSession(CreateExportContext(), output, Path.Combine(output, "wallbind.json"));
        await walls.QueueAsync(meta, overrides, diagnostics, required: true, ct);
        if (diagnostics.HasError)
        {
            return OperationResult<WallBuildResult>.Failure().WithDiagnostics(diagnostics);
        }
        var published = await walls.PublishAsync(ct: ct);
        if (!published.Succeeded)
        {
            return OperationResult<WallBuildResult>.Failure()
                .WithDiagnostics(DiagnosticSnapshot.Create(diagnostics).Merge(published.Diagnostics));
        }
        var value = new WallBuildResult(input, overrides.InputPath ?? meta.FullWallFilePath, output,
            meta.Id!.Value, overrides.WallId ?? meta.EffectiveWallId!.Value, overrides.Width ?? meta.WallWidth,
            CreateWallArtifacts(output, published.Value));
        return OperationResult<WallBuildResult>.Success(value)
            .WithDiagnostics(DiagnosticSnapshot.Create(diagnostics).Merge(published.Diagnostics));
    }

    private static WallRequestOverrides ToWorkflow(WallOverrides? value)
    {
        ResourceIds.Require(value?.WallId, MsgKeys.Error_Wall_id_invalid);
        return value is null ? WallRequestOverrides.None :
            new WallRequestOverrides(OptionalFullPath(value.InputPath), value.WallId, value.Width);
    }

    private static List<ApplicationArtifact> CreateWallArtifacts(string output, WallExportSummary summary)
    {
        List<ApplicationArtifact> artifacts = [];
        foreach (var asset in summary.Assets)
        {
            artifacts.Add(new ApplicationArtifact("wall.xml", asset.XmlPath));
            artifacts.Add(new ApplicationArtifact("wall.dds", asset.DdsPath));
        }
        if (summary.Assets.Count > 0 || summary.ManifestChanged)
        {
            artifacts.Add(new ApplicationArtifact("wall.bindings", Path.Combine(output, "wallbind.json")));
        }
        return artifacts;
    }
}
