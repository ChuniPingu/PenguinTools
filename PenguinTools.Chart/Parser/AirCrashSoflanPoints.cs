using PenguinTools.Chart.Models;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser;

/// <summary>Virtual SLA constraints for the rungs generated between AIR-CRUSH joints.</summary>
internal static class AirCrashSoflanPoints
{
    private const float LaneTolerance = 0.00001f;

    public static IEnumerable<U.Note> Create(U.Chart chart, int tickOffset) =>
        chart.Notes.Children.OfType<U.AirCrash>().SelectMany(crash => Sample(crash, tickOffset));

    private static IEnumerable<U.Note> Sample(U.AirCrash crash, int offset)
    {
        if (crash.Density.Original <= 0 || crash.Density.Original == int.MaxValue)
        {
            yield break;
        }

        var joints = crash.Children.OfType<U.AirCrashJoint>().Prepend(crash.AsChild()).ToArray();
        if (joints.Length < 2)
        {
            yield break;
        }

        var ticks = joints.Select(joint => new Time(checked(joint.Tick.Original + offset)).Round).ToArray();
        var interval = Math.Max(ChartResolution.SingleTick, crash.Density.Round);
        var segment = 0;
        for (long tick = (long)ticks[0] + interval; tick < ticks[^1]; tick += interval)
        {
            while (segment + 1 < ticks.Length && tick >= ticks[segment + 1])
            {
                segment++;
            }
            if (segment + 1 == ticks.Length)
            {
                yield break;
            }

            var start = joints[segment];
            var end = joints[segment + 1];
            var fraction = (float)(tick - ticks[segment]) / (ticks[segment + 1] - ticks[segment]);
            var left = start.Lane + (end.Lane - start.Lane) * fraction;
            var width = start.Width + (end.Width - start.Width) * fraction;
            // Integer SLA boundaries contain the fractional span iff they contain this enclosure.
            var lane = (int)MathF.Floor(left + LaneTolerance);
            var right = (int)MathF.Ceiling(left + width - LaneTolerance);
            var point = new U.AirCrashJoint
            {
                Tick = checked((int)(tick - offset)),
                Timeline = start.Timeline,
                Lane = lane,
                Width = right - lane,
                Height = start.Height + (end.Height - start.Height) * (decimal)fraction
            };
            point.MakeVirtual(crash);
            yield return point;
        }
    }
}
