using System.Globalization;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

internal static class C2sNativeLongPaths
{
    private readonly record struct Point(int Tick, int Lane, int Width, string Tag, int Height, int SecondHeight,
        int Flags, string Attribute);

    internal static IEnumerable<C.NativeLongNote[]> Build(IEnumerable<C.NativeLongNote> segments)
    {
        var paths = new List<List<C.NativeLongNote>>();
        var tails = new Dictionary<Point, PriorityQueue<int, int>>();
        foreach (var segment in segments)
        {
            var fields = segment.Fields;
            var solid = segment.Id == "ASO";
            var flags = !solid && int.TryParse(fields.ElementAtOrDefault(5), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            var attribute = solid ? fields.ElementAtOrDefault(7) switch
            {
                "RED" or "ORN" or "YEL" or "LIM" or "GRN" or "AQA" or "CYN" or "DGR" or "BLU" or
                    "PPL" or "VLT" or "PNK" or "GRY" or "BLK" or "NON" => fields[7],
                _ => "DEF"
            } : fields.ElementAtOrDefault(6) switch
            {
                "AxisY" or "AxisZ" or "Trace" when segment.Id == "HHX" => fields[6],
                _ => "DEF"
            };
            var start = new Point(segment.Tick.Original, segment.Lane, segment.Width, segment.Id,
                Height(fields[0]), solid ? Height(fields[1]) : 0, flags, attribute);
            if (!tails.TryGetValue(start, out var candidates) || !candidates.TryDequeue(out var root, out _))
            {
                root = paths.Count;
                paths.Add([]);
            }
            paths[root].Add(segment);
            var end = start with
            {
                Tick = segment.EndTick.Original,
                Lane = segment.EndLane,
                Width = segment.EndWidth,
                Height = Height(fields[solid ? 5 : 4]),
                SecondHeight = solid ? Height(fields[6]) : 0
            };
            if (!tails.TryGetValue(end, out var queue))
            {
                tails[end] = queue = new();
            }
            queue.Enqueue(root, root);
        }
        return paths.Select(path => path.ToArray());
    }

    private static int Height(string value) => C2sHeight.Key(decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
}
