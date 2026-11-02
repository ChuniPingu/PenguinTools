using System.Collections.Concurrent;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

/// <summary>Shares validation work between charts for the lifetime of one directory scan.</summary>
internal sealed class ScanMediaValidator(IMediaValidator mediaValidator, CancellationToken scanCancellationToken) : IMediaValidator
{
    private readonly ConcurrentDictionary<ValidationFile, Lazy<Task<MediaValidationResult>>> _audioValidations =
        new(ValidationFileComparer.Instance);
    private readonly ConcurrentDictionary<ValidationFile, Lazy<Task<MediaValidationResult>>> _imageValidations =
        new(ValidationFileComparer.Instance);

    public Task<MediaValidationResult> CheckAudioValidAsync(string src, CancellationToken ct = default) =>
        ValidateAsync(src, _audioValidations, mediaValidator.CheckAudioValidAsync, ct);

    public Task<MediaValidationResult> CheckImageValidAsync(string src, CancellationToken ct = default) =>
        ValidateAsync(src, _imageValidations, mediaValidator.CheckImageValidAsync, ct);

    private async Task<MediaValidationResult> ValidateAsync(
        string path,
        ConcurrentDictionary<ValidationFile, Lazy<Task<MediaValidationResult>>> validations,
        Func<string, CancellationToken, Task<MediaValidationResult>> validate,
        CancellationToken ct)
    {
        scanCancellationToken.ThrowIfCancellationRequested();
        ct.ThrowIfCancellationRequested();

        var file = GetValidationFile(path);
        var validation = file is null
            ? validate(path, scanCancellationToken)
            : validations.GetOrAdd(file, _ => new Lazy<Task<MediaValidationResult>>(
                () => validate(path, scanCancellationToken))).Value;

        // A caller can stop waiting without cancelling validation needed by another chart.
        // The shared scan token still cancels the underlying decoder for the whole batch.
        var result = await validation.WaitAsync(ct).ConfigureAwait(false);
        scanCancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static ValidationFile? GetValidationFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? new ValidationFile(file.FullName, file.Length, file.LastWriteTimeUtc) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Let the media implementation preserve its invalid-path result or exception.
            return null;
        }
    }

    private sealed record ValidationFile(string Path, long Length, DateTime LastWriteTimeUtc);

    private sealed class ValidationFileComparer : IEqualityComparer<ValidationFile>
    {
        internal static readonly ValidationFileComparer Instance = new();
        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        public bool Equals(ValidationFile? x, ValidationFile? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null &&
                PathComparer.Equals(x.Path, y.Path) && x.Length == y.Length && x.LastWriteTimeUtc == y.LastWriteTimeUtc);

        public int GetHashCode(ValidationFile obj) =>
            HashCode.Combine(PathComparer.GetHashCode(obj.Path), obj.Length, obj.LastWriteTimeUtc);
    }
}
