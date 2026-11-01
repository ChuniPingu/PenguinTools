using PenguinTools.Chart.Models;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

internal static class C2sAirCrashPaths
{
    private readonly record struct Point(int Tick, int Lane, int Width, decimal Height, Color Color, int Density, AirLadderAttr Attr);

    internal static IEnumerable<C.AirCrash[]> Build(IEnumerable<C.AirCrash> segments, bool includeAxis = true)
    {
        var paths = new List<List<C.AirCrash>>();
        var tails = new Dictionary<Point, PriorityQueue<int, int>>();
        foreach (var segment in segments)
        {
            var attr = !includeAxis || segment.Attr == AirLadderAttr.Trace ? AirLadderAttr.DEF : segment.Attr;
            var start = new Point(segment.Tick.Original, segment.Lane, segment.Width, segment.Height.Original,
                segment.Color, segment.Density.Original, attr);
            if (!tails.TryGetValue(start, out var candidates) || !candidates.TryDequeue(out var root, out _))
            {
                root = paths.Count;
                paths.Add([]);
            }
            paths[root].Add(segment);
            var end = start with { Tick = segment.EndTick.Original, Lane = segment.EndLane, Width = segment.EndWidth, Height = segment.EndHeight.Original };
            if (!tails.TryGetValue(end, out var queue))
            {
                tails[end] = queue = new();
            }
            queue.Enqueue(root, root);
        }
        return paths.Select(path => path.ToArray());
    }
}
