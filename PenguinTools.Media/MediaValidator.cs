namespace PenguinTools.Media;

public sealed class MediaValidator(IAudioService audio, IImageService images) : IMediaValidator
{
    private readonly IAudioService _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    private readonly IImageService _images = images ?? throw new ArgumentNullException(nameof(images));

    public Task<MediaValidationResult> CheckAudioValidAsync(string source, CancellationToken ct = default) =>
        _audio.CheckAudioValidAsync(source, ct);

    public Task<MediaValidationResult> CheckImageValidAsync(string source, CancellationToken ct = default) =>
        _images.CheckImageValidAsync(source, ct);
}
