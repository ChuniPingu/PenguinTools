namespace PenguinTools.Chart.Models;

public readonly record struct Position(int Measure, int Offset);

public readonly record struct Time(int Original) : IComparable<Time>
{
    public int Round
    {
        get
        {
            // An odd grid step has no integer midpoint.
            const int half = ChartResolution.SingleTick / 2;
            var biased = (long)Original + (Original < 0 ? -half : half);
            return (int)(biased / ChartResolution.SingleTick) * ChartResolution.SingleTick;
        }
    }
    public int Scaled => Round / ChartResolution.SingleTick;

    public Position Position
    {
        get
        {
            var rounded = Round;
            return new(rounded / ChartResolution.UmiguriTick, rounded % ChartResolution.UmiguriTick / ChartResolution.SingleTick);
        }
    }

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
