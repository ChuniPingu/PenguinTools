using System.Text;
using System.Xml;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Media;

public sealed record WallAsset(Entry Name, string Directory, string XmlPath, string DdsPath);

public sealed record PreparedWallAsset(WallAsset Asset, byte[] Xml, byte[] Dds);

public sealed record WallBuildRequest(
    AssetManager Assets, string SourcePath, int WallId, WallWidth Width, string OutputDirectory);

public sealed class WallConverter(WallBuildRequest request, IImageService images)
{
    public static Task PublishAsync(IEnumerable<PreparedWallAsset> assets, string manifestPath,
        ReadOnlyMemory<byte> manifest, CancellationToken ct = default)
    {
        var outputs = new List<(string Path, ReadOnlyMemory<byte> Data)>();
        foreach (var prepared in assets)
        {
            outputs.Add((prepared.Asset.DdsPath, prepared.Dds));
            outputs.Add((prepared.Asset.XmlPath, prepared.Xml));
        }
        outputs.Add((manifestPath, manifest));
        return StagedOutputs.WriteAsync(outputs, ct);
    }

    public async Task<OperationResult<PreparedWallAsset>> PrepareAsync(CancellationToken ct = default)
    {
        var diagnostics = new DiagnosticCollector();
        if (request.WallId < 0 || !Enum.IsDefined(request.Width))
        {
            var key = request.WallId < 0 ? MsgKeys.Error_Wall_id_invalid : MsgKeys.Error_Wall_width_invalid;
            diagnostics.Report(new Diagnostic(Severity.Error, Msg.Key(key)));
            return OperationResult<PreparedWallAsset>.Failure().WithDiagnostics(diagnostics);
        }
        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            diagnostics.Report(new Diagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Wall_source_required)));
            return OperationResult<PreparedWallAsset>.Failure().WithDiagnostics(diagnostics);
        }

        string? staging = null;
        try
        {
            if (!File.Exists(request.SourcePath))
            {
                throw new FileNotFoundException("Wall image was not found.", request.SourcePath);
            }
            var validation = await images.CheckImageValidAsync(request.SourcePath, ct);
            if (validation.IsFailure)
            {
                diagnostics.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Invalid_wall_image),
                    request.SourcePath)
                { Target = validation.Failure });
                return OperationResult<PreparedWallAsset>.Failure().WithDiagnostics(diagnostics);
            }

            foreach (var existing in request.Assets.FieldWallNames.Where(entry => entry.Id == request.WallId))
            {
                diagnostics.Report(new Diagnostic(Severity.Warning,
                    Msg.Create(MsgKeys.Warn_Wall_already_exists, existing.Str, request.WallId)));
            }

            var xml = new DdsFieldWallXml(request.WallId);
            var directory = Path.Combine(request.OutputDirectory, xml.DataName);
            var xmlPath = Path.Combine(directory, "DDSFieldWall.xml");
            var ddsPath = Path.Combine(directory, xml.Image);
            staging = Path.Combine(request.OutputDirectory, ".wall-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(staging);
            var temporaryDds = Path.Combine(staging, "wall.dds");
            await images.ConvertWallAsync(request.SourcePath, temporaryDds, request.Width, ct);
            var dds = await File.ReadAllBytesAsync(temporaryDds, ct);
            await using var xmlBytes = new MemoryStream();
            await using (var writer = XmlWriter.Create(xmlBytes, new XmlWriterSettings
            {
                Async = true,
                Encoding = new UTF8Encoding(false),
                Indent = true,
                CloseOutput = false
            }))
            {
                await xml.ToDocument().SaveAsync(writer, ct);
                await writer.FlushAsync();
            }
            return OperationResult<PreparedWallAsset>.Success(new PreparedWallAsset(
                new WallAsset(xml.Name, directory, xmlPath, ddsPath), xmlBytes.ToArray(), dds))
                .WithDiagnostics(diagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Invalid_wall_image),
                request.SourcePath)
            { Target = ex });
            return OperationResult<PreparedWallAsset>.Failure().WithDiagnostics(diagnostics);
        }
        finally
        {
            if (staging is not null && System.IO.Directory.Exists(staging))
            {
                System.IO.Directory.Delete(staging, recursive: true);
            }
        }
    }
}
