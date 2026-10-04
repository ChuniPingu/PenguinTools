namespace PenguinTools.Chart.Models;

public readonly record struct Position(int Measure, int Offset);

public readonly record struct Time(int Original) : IComparable<Time>
{
    public int Round => (int)Math.Round((decimal)Original / ChartResolution.SingleTick) * ChartResolution.SingleTick;
    public int Scaled => (int)(Round * ChartResolution.TickFactor);

    public Position Position => new(Round / ChartResolution.UmiguriTick,
        (int)(Round % ChartResolution.UmiguriTick * ChartResolution.TickFactor));

    public int CompareTo(Time other)
    {
        return Original.CompareTo(other.Original);
    }

    public static bool operator <(Time left, Time right) => left.CompareTo(right) < 0;
    public static bool operator <=(Time left, Time right) => left.CompareTo(right) <= 0;
    public static bool operator >(Time left, Time right) => left.CompareTo(right) > 0;
    public static bool operator >=(Time left, Time right) => left.CompareTo(right) >= 0;

    public static implicit operator Time(int value)
    {
        return new Time(value);
    }
}
