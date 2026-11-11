using System.Diagnostics;
using PenguinTools.Core;
using PenguinTools.Media;

namespace PenguinTools.Tests;

internal sealed class TestMediaTool : IAudioService, IImageService, IMediaValidator
{
    public static readonly TestMediaTool Instance = new();

    public Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset,
        CancellationToken ct = default)
    {
        return Task.FromResult(Ok());
    }

    public Task<MediaValidationResult> CheckAudioValidAsync(string src, CancellationToken ct = default)
    {
        return Task.FromResult(MediaValidationResult.Valid);
    }

    public Task<MediaValidationResult> CheckImageValidAsync(string src, CancellationToken ct = default)
    {
        return Task.FromResult(MediaValidationResult.Valid);
    }

    public Task ConvertJacketAsync(string src, string dst, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task ConvertWallAsync(string source, string destination, PenguinTools.Core.Metadata.WallWidth width,
        CancellationToken ct = default) => File.WriteAllBytesAsync(destination, [1, 2, 3], ct);

    public Task ConvertStageAsync(string background, string destination, string? notesFieldDestination,
        IReadOnlyList<string?>? effects = null, int backgroundOffset = 160, string? templatePath = null,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ExtractDdsAsync(string src, string dst, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<DdsDecodeResult> DecodeDdsAsync(string src, string dst, CancellationToken ct = default)
    {
        return Task.FromResult(new DdsDecodeResult(src, dst));
    }

    public Task ConvertRasterToPngAsync(string source, string destination, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<CriExtractResult> ExtractCriAudioAsync(CriExtractOptions options, CancellationToken ct = default)
    {
        return Task.FromResult(new CriExtractResult(1, options.SourcePath, []));
    }

    public Task ConvertCriAsync(
        CriConvertRequest request,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    private static ProcessCommandResult Ok()
    {
        return new ProcessCommandResult(new ProcessStartInfo { FileName = "null" }, (int)InterExitCode.Success, "", "");
    }
}
