namespace PenguinTools.Media;

public interface IAudioService
{
    Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset,
        CancellationToken ct = default);

    Task<MediaValidationResult> CheckAudioValidAsync(string src, CancellationToken ct = default);

    Task<CriExtractResult> ExtractCriAudioAsync(CriExtractOptions options, CancellationToken ct = default);

    Task ConvertCriAsync(CriConvertRequest request, CancellationToken ct = default);
}
