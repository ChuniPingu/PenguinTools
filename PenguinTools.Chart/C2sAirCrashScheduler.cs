using PenguinTools.Chart.Models;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

internal static class C2sAirCrashScheduler
{
    private readonly record struct Point(int Tick, int Lane, int Width, int Height, Color Color, int Density);

    internal static List<C.AirCrash> Schedule(IEnumerable<C.AirCrash> source)
    {
        var segments = source.ToList();
        // Parsed C2S records already encode their paths through statement order.
        if (segments.Count == 0 || segments.Any(n => n.PathRoot is null))
        {
            return segments;
        }
        var paths = segments.GroupBy(n => n.PathRoot!).Select(g => g.ToArray()).ToArray();
        var outgoing = new List<List<int>>();
        var incoming = new List<int>();
        var starts = new Dictionary<Point, HashSet<int>>();
        var ends = new Dictionary<Point, List<int>>();
        int AddNode()
        {
            outgoing.Add([]);
            incoming.Add(0);
            return incoming.Count - 1;
        }
        for (var i = 0; i < paths.Length; i++)
        {
            AddNode();
            foreach (var segment in paths[i])
            {
                var start = At(segment, false);
                if (!starts.TryGetValue(start, out var candidates))
                {
                    starts[start] = candidates = [];
                }
                candidates.Add(i);
            }
            var end = At(paths[i][^1], true);
            if (!ends.TryGetValue(end, out var tails))
            {
                ends[end] = tails = [];
            }
            tails.Add(i);
        }
        foreach (var (end, tails) in ends)
        {
            if (!starts.TryGetValue(end, out var candidates))
            {
                continue;
            }
            // Complete paths passing through a cell before a permanent tail
            // can capture them. Positive durations make these edges acyclic.
            var barrier = AddNode();
            foreach (var candidate in candidates)
            {
                outgoing[candidate].Add(barrier);
                incoming[barrier]++;
            }
            foreach (var tail in tails)
            {
                outgoing[barrier].Add(tail);
                incoming[tail]++;
            }
        }
        var ready = new PriorityQueue<int, int>();
        var emitted = new bool[incoming.Count];
        for (var i = 0; i < incoming.Count; i++)
        {
            if (incoming[i] == 0)
            {
                ready.Enqueue(i, i < paths.Length ? i : -1);
            }
        }
        var result = new List<C.AirCrash>(segments.Count);
        var remaining = paths.Length;
        var fallback = 0;
        while (remaining > 0)
        {
            if (!ready.TryDequeue(out var node, out _))
            {
                // Keep malformed paths available for the length diagnostic.
                while (emitted[fallback])
                {
                    fallback++;
                }
                node = fallback;
            }
            if (emitted[node])
            {
                continue;
            }
            emitted[node] = true;
            if (node < paths.Length)
            {
                result.AddRange(paths[node]);
                remaining--;
            }
            foreach (var next in outgoing[node])
            {
                if (--incoming[next] == 0)
                {
                    ready.Enqueue(next, next < paths.Length ? next : -1);
                }
            }
        }
        return result;
    }

    private static Point At(C.AirCrash note, bool end) => new(
        end ? note.EndTick.Round : note.Tick.Round,
        end ? note.EndLane : note.Lane,
        Math.Clamp(end ? note.EndWidth : note.Width, 1, 16),
        C2sHeight.Key(end ? note.EndHeight : note.Height), note.Color,
        note.Density.Original > 0 ? Math.Max(1, note.Density.Scaled) : note.Density.Scaled);
}
