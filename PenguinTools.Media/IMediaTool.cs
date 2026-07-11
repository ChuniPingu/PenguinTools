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