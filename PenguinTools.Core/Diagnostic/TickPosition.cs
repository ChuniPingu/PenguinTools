namespace PenguinTools.Core.Diagnostic;

public readonly record struct TickPosition(int Bar, int Beat, int TickOffset)
{
    public override string ToString()
    {
        return $"{Bar}:{Beat}.{TickOffset}";
    }
}
