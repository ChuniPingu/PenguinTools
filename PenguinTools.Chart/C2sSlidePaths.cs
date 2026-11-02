using System.Globalization;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

internal static class C2sSlidePaths
{
    internal readonly record struct Segment(decimal Tick, int Lane, int Width,
        decimal EndTick, int EndLane, int EndWidth, bool NoLine);

    internal static IEnumerable<C.Slide[]> Build(IEnumerable<C.Slide> slides, bool rounded = false) =>
        Build(slides, s => new Segment(rounded ? s.Tick.Round : s.Tick.Original, s.Lane, s.Width,
            rounded ? s.EndTick.Round : s.EndTick.Original, s.EndLane, s.EndWidth, s.NoLine));

    internal static IEnumerable<T[]> Build<T>(IEnumerable<T> source, Func<T, Segment> describe)
    {
        var paths = new List<List<T>>();
        var tails = new Dictionary<(decimal Tick, int Lane, int Width, bool NoLine), PriorityQueue<int, int>>();
        foreach (var item in source)
        {
            var point = describe(item);
            var start = (point.Tick, point.Lane, Math.Clamp(point.Width, 1, 16), point.NoLine);
            if (!tails.TryGetValue(start, out var candidates) || !candidates.TryDequeue(out var root, out _))
            {
                root = paths.Count;
                paths.Add([]);
            }
            paths[root].Add(item);
            var end = (point.EndTick, point.EndLane, Math.Clamp(point.EndWidth, 1, 16), point.NoLine);
            if (!tails.TryGetValue(end, out var queue))
            {
                tails[end] = queue = new();
            }
            // Records advance the first matching path in creation order, even
            // when another path arrived at this endpoint earlier.
            queue.Enqueue(root, root);
        }
        return paths.Select(path => path.ToArray());
    }

    internal static bool PreservesPaths(IEnumerable<string> before, IEnumerable<string> after, int resolution)
    {
        var beforeRecords = before.ToArray();
        var afterRecords = after.ToArray();
        static bool IsSlide(string line) => C2SRoundTrip.Parts(line).FirstOrDefault() is "SLC" or "SLD" or "SXC" or "SXD";
        var original = beforeRecords.Where(IsSlide).Select(s => C2SRoundTrip.Normalize(s, resolution)).ToArray();
        var reordered = afterRecords.Where(IsSlide).Select(s => C2SRoundTrip.Normalize(s, resolution)).ToArray();
        if (beforeRecords.SequenceEqual(afterRecords))
        {
            return true;
        }

        static Segment Describe(string line)
        {
            var p = C2SRoundTrip.Parts(line);
            var tick = decimal.Parse(p[1], CultureInfo.InvariantCulture);
            return new Segment(tick, int.Parse(p[3], CultureInfo.InvariantCulture), int.Parse(p[4], CultureInfo.InvariantCulture),
                tick + decimal.Parse(p[5], CultureInfo.InvariantCulture), int.Parse(p[6], CultureInfo.InvariantCulture),
                int.Parse(p[7], CultureInfo.InvariantCulture), p[8] == "NCL");
        }
        static IEnumerable<string> Keys(string[] records) => Build(records, Describe)
            .Select(path => string.Join('\n', path)).Order(StringComparer.Ordinal);

        return Keys(original).SequenceEqual(Keys(reordered)) &&
            C2sNoteRelations.Keys(beforeRecords, resolution).SequenceEqual(C2sNoteRelations.Keys(afterRecords, resolution));
    }
}
