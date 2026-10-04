namespace PenguinTools.Media;

/// <summary>Media validation independent of whether a decoder runs in-process or externally.</summary>
public sealed record MediaValidationResult(bool IsSuccess, object? Failure = null)
{
    public bool IsFailure => !IsSuccess;
    public static MediaValidationResult Valid { get; } = new(true);
    public static MediaValidationResult Invalid(object cause) => new(false, cause);
}
