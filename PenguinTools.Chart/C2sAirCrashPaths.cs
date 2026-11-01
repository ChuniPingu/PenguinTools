using PenguinTools.Chart.Models;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

internal static class C2sAirCrashPaths
{
    private readonly record struct Point(int Tick, int Lane, int Width, int Height, Color Color, int Density);

    internal static IEnumerable<C.AirCrash[]> Build(IEnumerable<C.AirCrash> segments)
    {
        var paths = new List<List<C.AirCrash>>();
        var tails = new Dictionary<Point, PriorityQueue<int, int>>();
        foreach (var segment in segments)
        {
            // Continuations retain the root's axis and compare quantized heights.
            var start = new Point(segment.Tick.Original, segment.Lane, Math.Clamp(segment.Width, 1, 16), C2sHeight.Key(segment.Height),
                segment.Color, segment.Density.Original);
            if (!tails.TryGetValue(start, out var candidates) || !candidates.TryDequeue(out var root, out _))
            {
                root = paths.Count;
                paths.Add([]);
            }
            paths[root].Add(segment);
            var end = start with { Tick = segment.EndTick.Original, Lane = segment.EndLane, Width = Math.Clamp(segment.EndWidth, 1, 16), Height = C2sHeight.Key(segment.EndHeight) };
            if (!tails.TryGetValue(end, out var queue))
            {
                tails[end] = queue = new();
            }
            queue.Enqueue(root, root);
        }
        return paths.Select(path => path.ToArray());
    }
}
