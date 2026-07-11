namespace PenguinTools.Core;

public sealed record MessageDescriptor(
    string Key,
    IReadOnlyDictionary<string, object?>? Args = null)
{
    public override string ToString() => Key;
}