namespace PenguinTools.Chart.Models;

public static class ChartResolution
{
    public const int UmiguriTick = 1920;
    public const int ChunithmTick = 384;
    public const int SingleTick = UmiguriTick / ChunithmTick;
    public const decimal TickFactor = (decimal)ChunithmTick / UmiguriTick;
}
