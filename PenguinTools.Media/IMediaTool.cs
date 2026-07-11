namespace PenguinTools.Media;

public interface IMediaTool
{
    Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset,
        CancellationToken ct = default);

    Task<ProcessCommandResult> CheckAudioValidAsync(string src, CancellationToken ct = default);

    Task<ProcessCommandResult> CheckImageValidAsync(string src, CancellationToken ct = default);

    Task ConvertJacketAsync(string src, string dst, CancellationToken ct = default);

    Task ConvertStageAsync(string bg, string stDst, string nfDst, string?[]? fxPaths,
        CancellationToken ct = default);

    Task ExtractDdsAsync(string src, string dst, CancellationToken ct = default);

    Task<DdsDecodeResult> DecodeDdsAsync(string src, string dst, CancellationToken ct = default)
        => throw new NotSupportedException("DDS decoding is not supported by this media tool.");

    Task<CriExtractResult> ExtractCriAudioAsync(CriExtractOptions options, CancellationToken ct = default)
        => throw new NotSupportedException("CRI extraction is not supported by this media tool.");

    Task ConvertCriAsync(
        string wav,
        string acb,
        string awb,
        string name,
        long previewStartMs,
        long previewStopMs,
        ulong hcaKey,
        CancellationToken ct = default);
}
