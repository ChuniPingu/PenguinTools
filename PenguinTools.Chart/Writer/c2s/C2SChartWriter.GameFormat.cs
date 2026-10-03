using System.Globalization;

namespace PenguinTools.Chart.Writer.c2s;

public partial class C2SChartWriter
{
    private const int GameResolution = 384;

    private static string FormatForGame(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var sourceResolution = lines.Select(C2SRoundTrip.Parts)
            .Where(p => p.Length > 1 && p[0] == "RESOLUTION")
            .Select(p => decimal.Parse(p[1], CultureInfo.InvariantCulture)).Single();
        if (sourceResolution <= 0)
            throw new FormatException("C2S resolution must be positive.");

        var timingChanged = false;
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = FormatGameLine(lines[i], sourceResolution, ref timingChanged);
        }

        // Saved judge/progress summaries describe the pre-quantization chart.
        // Keep them only when all note and event positions remain exact.
        if (timingChanged)
            lines = lines.Where(line => !(C2SRoundTrip.Parts(line).FirstOrDefault() ?? "").StartsWith("T_", StringComparison.Ordinal)).ToArray();
        var result = string.Join('\n', lines);
        return timingChanged ? result + ChartStatistics.Calculate(result) : result;
    }
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    // Match the original Time.Round implementation's Math.Round semantics.
    private static long Round(decimal value) => checked((long)Math.Round(value));
    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatGameLine(string line, decimal sourceResolution, ref bool timingChanged)
    {
        var p = C2SRoundTrip.Parts(line);
        if (p.Length < 2) return line;
        if (p[0] == "RESOLUTION")
        {
            return $"RESOLUTION\t{GameResolution}";
        }
        if (p[0] == "CLK_DEF")
        {
            return "CLK_DEF\t" + Integer(Round(Number(p[1]) * GameResolution / sourceResolution));
        }
        if (!C2SRoundTrip.IsRecord(p[0])) return line;

        var start = Number(p[1]) * GameResolution + Number(p[2]) * GameResolution / sourceResolution;
        var roundedStart = Round(start);
        timingChanged |= start != roundedStart;
        // Quantize absolute endpoints, not individual segment lengths, so
        // adjacent curve segments and AIR parents keep the same boundary.
        var durationIndex = p[0] switch
        {
            "HLD" or "HXD" or "SLC" or "SLD" or "SXC" or "SXD" or "SLA" => 5,
            "ASC" or "ASD" or "ALD" => 7,
            "AHD" or "AHX" or "ASX" => 6,
            "SLP" or "SFL" or "DCM" or "STP" => 3,
            _ => -1
        };
        if (durationIndex >= 0)
        {
            var end = start + Number(p[durationIndex]) * GameResolution / sourceResolution;
            var roundedEnd = Round(end);
            timingChanged |= end != roundedEnd;
            p[durationIndex] = Integer(checked(roundedEnd - roundedStart));
        }
        if (p[0] == "ALD")
        {
            var density = Number(p[5]) * GameResolution / sourceResolution;
            var roundedDensity = density > 0 ? Math.Max(1, Round(density)) : Round(density);
            timingChanged |= density != roundedDensity;
            p[5] = Integer(roundedDensity);
        }
        var measure = checked((long)decimal.Floor((decimal)roundedStart / GameResolution));
        p[1] = Integer(measure);
        p[2] = Integer(roundedStart - measure * GameResolution);
        return string.Join('\t', p);
    }
}
