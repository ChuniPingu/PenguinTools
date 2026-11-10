using PenguinTools.Core.Metadata;

namespace PenguinTools.Media;

public interface IImageService
{
    Task<MediaValidationResult> CheckImageValidAsync(string source, CancellationToken ct = default);

    Task ConvertJacketAsync(string source, string destination, CancellationToken ct = default);

    Task ConvertWallAsync(string source, string destination, WallWidth width, CancellationToken ct = default);

    Task ConvertStageAsync(string background, string destination, string? notesFieldDestination,
        IReadOnlyList<string?>? effects = null, int backgroundOffset = 160, string? templatePath = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ExtractDdsAsync(string source, string outputDirectory, CancellationToken ct = default);

    Task<DdsDecodeResult> DecodeDdsAsync(string source, string destination, CancellationToken ct = default);

    Task ConvertRasterToPngAsync(string source, string destination, CancellationToken ct = default);
}
