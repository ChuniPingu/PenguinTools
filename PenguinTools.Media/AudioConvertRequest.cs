namespace PenguinTools.Media;

public sealed record AudioConvertRequest(
    Meta Meta,
    string OutFolder,
    string WorkingAudioPath,
    ulong HcaEncryptionKey)
{
    public const ulong DefaultHcaEncryptionKey = 32931609366120192UL;
}