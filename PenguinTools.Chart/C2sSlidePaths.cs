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
        var entries = source.Select(item => (Item: item, Point: describe(item)))
            .OrderBy(e => e.Point.Tick).ThenBy(e => e.Point.Lane).ThenBy(e => e.Point.Width).ToArray();
        var byStart = Enumerable.Range(0, entries.Length)
            .GroupBy(i => (entries[i].Point.Tick, entries[i].Point.Lane, entries[i].Point.Width))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var used = new bool[entries.Length];
        for (var root = 0; root < entries.Length; root++)
        {
            if (used[root])
            {
                continue;
            }

            var path = new List<T>();
            var current = root;
            while (current >= 0)
            {
                used[current] = true;
                var (item, point) = entries[current];
                path.Add(item);
                current = -1;
                if (!byStart.TryGetValue((point.EndTick, point.EndLane, point.EndWidth), out var candidates))
                {
                    break;
                }

                // Keep source order among equally styled paths. A geometry-only
                // fallback retains legacy charts that change line style mid-path.
                foreach (var candidate in candidates)
                {
                    if (used[candidate])
                    {
                        continue;
                    }
                    if (current < 0)
                    {
                        current = candidate;
                    }
                    if (entries[candidate].Point.NoLine == point.NoLine)
                    {
                        current = candidate;
                        break;
                    }
                }
            }
            yield return path.ToArray();
        }
    }

    internal static bool PreservesPaths(IEnumerable<string> before, IEnumerable<string> after, int resolution)
    {
        static bool IsSlide(string line) => C2SRoundTrip.Parts(line).FirstOrDefault() is "SLC" or "SLD" or "SXC" or "SXD";
        var original = before.Where(IsSlide).Select(s => C2SRoundTrip.Normalize(s, resolution)).ToArray();
        var reordered = after.Where(IsSlide).Select(s => C2SRoundTrip.Normalize(s, resolution)).ToArray();
        if (original.SequenceEqual(reordered))
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

        return Keys(original).SequenceEqual(Keys(reordered));
    }
}
