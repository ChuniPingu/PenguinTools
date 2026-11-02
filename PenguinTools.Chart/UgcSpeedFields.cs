using System.Globalization;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class UgcSpeedFields
{
    private readonly record struct Field(int Id, long Start, long End, int Lane, long Right);

    internal static void WarnIfUnsupported(U.Chart chart, string format, IDiagnosticSink diagnostics)
    {
        var definitions = new HashSet<int>();
        var fields = new List<Field>();
        foreach (var line in chart.Extras.EditorHeaders)
        {
            var args = line.Split('\t');
            if (args.Length < 4 || !Integer(args[1], out var id) || id <= 0)
            {
                continue;
            }
            if (args[0].Equals("@SPDDEF", StringComparison.OrdinalIgnoreCase) &&
                Integer(args[2], out _) &&
                decimal.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && speed != 1)
            {
                definitions.Add(id);
            }
            else if (args[0].Equals("@SPDFLD", StringComparison.OrdinalIgnoreCase) && args.Length >= 6 &&
                TryTick(chart, args[2], out var start) && Integer(args[3], out var lane) &&
                Integer(args[4], out var width) && Integer(args[5], out var length) && width > 0 && length > 0)
            {
                fields.Add(new Field(id, start, start + length, lane, (long)lane + width));
            }
        }
        if (definitions.Count == 0 || fields.Count == 0)
        {
            return;
        }
        if (chart.Notes.Children.SelectMany(n => n.Children).Any(CurveExpansion.IsControl))
        {
            chart = ChartCopy.Create(chart);
            CurveExpansion.Expand(chart);
        }

        foreach (var (note, tick, _, lane, width) in Points(chart))
        {
            // Later fields replace earlier assignments, even if their definition
            // is absent or has unit speed.
            var field = fields.LastOrDefault(f => tick >= f.Start && tick < f.End &&
                lane >= f.Lane && lane + width <= f.Right);
            if (!definitions.Contains(field.Id))
            {
                continue;
            }
            var message = Msg.Create(MsgKeys.Warn_Ugc_relative_speed_not_reproduced, format);
            diagnostics.Report(new TimedDiagnostic(Severity.Warning, message, tick) { Target = note });
            return;
        }
    }

    internal static void NormalizeResolution(List<string> headers, Func<int, int> scaleTick)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var args = headers[i].Split('\t');
            if (args.Length >= 4 && args[0].Equals("@SPDDEF", StringComparison.OrdinalIgnoreCase) &&
                Integer(args[2], out var offset))
            {
                args[2] = scaleTick(offset).ToString(CultureInfo.InvariantCulture);
            }
            else if (args.Length >= 6 && args[0].Equals("@SPDFLD", StringComparison.OrdinalIgnoreCase))
            {
                var position = args[2].Split('\'');
                if (position.Length != 2 || !Integer(position[1], out var tick) || !Integer(args[5], out var length))
                {
                    continue;
                }
                args[2] = $"{position[0]}'{scaleTick(tick).ToString(CultureInfo.InvariantCulture)}";
                args[5] = scaleTick(length).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                continue;
            }
            headers[i] = string.Join('\t', args);
        }
    }

    private static bool TryTick(U.Chart chart, string position, out long tick)
    {
        tick = 0;
        var parts = position.Split('\'');
        if (parts.Length != 2 || !Integer(parts[0], out var bar) || !Integer(parts[1], out var offset))
        {
            return false;
        }
        var beat = chart.Events.Children.OfType<U.BeatEvent>().Where(b => b.Bar <= bar).MaxBy(b => b.Bar);
        tick = beat is null ? bar * 1920L + offset :
            beat.Tick.Original + (bar - (long)beat.Bar) * (1920L * beat.Numerator / beat.Denominator) + offset;
        return true;
    }

    private static bool Integer(string value, out int number) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

    internal static IEnumerable<(U.Note Note, int Tick, int Timeline, decimal Lane, decimal Width)> Points(U.Chart chart)
    {
        foreach (var root in chart.Notes.Children.Where(n => n is not U.SoflanArea))
        {
            foreach (var note in root.Children.Prepend(root).Where(n => !CurveExpansion.IsControl(n)))
            {
                yield return (note, note.Tick.Original, note.Timeline, note.Lane, note.Width);
            }
            if (root is not U.AirCrash { Density.Original: > 0 and < int.MaxValue } crash || crash.LastChild is null)
            {
                continue;
            }
            var points = crash.Children.Prepend(crash).ToArray();
            var segment = 0;
            for (long tick = (long)crash.Tick.Original + crash.Density.Original;
                 tick < points[^1].Tick.Original; tick += crash.Density.Original)
            {
                while (segment + 1 < points.Length && tick >= points[segment + 1].Tick.Original)
                {
                    segment++;
                }
                var start = points[segment];
                var end = points[segment + 1];
                var fraction = (decimal)(tick - start.Tick.Original) / (end.Tick.Original - (long)start.Tick.Original);
                yield return (crash, (int)tick, start.Timeline, start.Lane + (end.Lane - (decimal)start.Lane) * fraction,
                    start.Width + (end.Width - (decimal)start.Width) * fraction);
            }
        }
    }
}
