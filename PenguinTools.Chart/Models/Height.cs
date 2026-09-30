/*
   This code is based on the original implementation from:
   https://github.com/inonote/MargreteOnline
*/

namespace PenguinTools.Chart.Models;

public readonly record struct Height(decimal Original) : IComparable<Height>
{
    public decimal Result => Original / 20m + 1m;

    public int CompareTo(Height other)
    {
        return Original.CompareTo(other.Original);
    }

    public static Height operator -(Height a, Height b)
    {
        return a.Original - b.Original;
    }

    public static implicit operator Height(decimal value)
    {
        return new Height(value);
    }
}