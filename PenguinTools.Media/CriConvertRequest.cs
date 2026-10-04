namespace PenguinTools.Media;

public sealed record CriConvertRequest(
    string Wav, string Acb, string Awb, string Name,
    long PreviewStartMs, long PreviewStopMs, ulong HcaKey);
