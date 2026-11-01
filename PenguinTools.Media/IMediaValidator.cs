namespace PenguinTools.Media;

public interface IMediaValidator
{
    Task<MediaValidationResult> CheckAudioValidAsync(string source, CancellationToken ct = default);

    Task<MediaValidationResult> CheckImageValidAsync(string source, CancellationToken ct = default);
}
