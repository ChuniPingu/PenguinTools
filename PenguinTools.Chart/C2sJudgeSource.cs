using System.Globalization;

namespace PenguinTools.Chart;

internal static class C2sJudgeSource
{
    internal static readonly string[] Categories = ["TAP", "HLD", "SLD", "AIR", "FLK", "ALL"];

    internal static Dictionary<string, long>? Summary(string text)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var p = C2SRoundTrip.Parts(line);
            if (p.Length == 0 || !p[0].StartsWith("T_JUDGE_", StringComparison.Ordinal))
            {
                continue;
            }

            var category = p[0][8..];
            if (!Categories.Contains(category) || p.Length != 2 ||
                !long.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                !result.TryAdd(category, value))
            {
                return null;
            }
        }
        if (result.Count != Categories.Length || Categories.Take(5).Sum(k => (decimal)result[k]) != result["ALL"])
        {
            return null;
        }

        return result;
    }

    internal static void Capture(ChartExtras extras, string text)
    {
        var declared = Summary(text);
        if (declared is null)
        {
            return;
        }

        var computed = Summary(ChartStatistics.Calculate(text));
        if (computed is not null && Categories.Any(k => declared[k] != computed[k]))
        {
            extras.UnchangedJudgeSource = text;
        }
    }
}
