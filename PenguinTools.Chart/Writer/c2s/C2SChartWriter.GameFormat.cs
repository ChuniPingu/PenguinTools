using System.Globalization;

namespace PenguinTools.Chart.Writer.c2s;

public partial class C2SChartWriter
{
    private static string FormatForGame(string text)
    {
        const int gameResolution = 384;
        var lines = text.Replace("\r", "").Split('\n');
        var sourceResolution = lines.Select(C2SRoundTrip.Parts)
            .Where(p => p.Length > 1 && p[0] == "RESOLUTION")
            .Select(p => decimal.Parse(p[1], CultureInfo.InvariantCulture)).Single();
        if (sourceResolution <= 0)
            throw new FormatException("C2S resolution must be positive.");

        decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
        // Match the original Time.Round implementation's Math.Round semantics.
        long Round(decimal value) => checked((long)Math.Round(value));
        string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
        var timingChanged = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var p = C2SRoundTrip.Parts(lines[i]);
            if (p.Length < 2) continue;
            if (p[0] == "RESOLUTION")
            {
                lines[i] = $"RESOLUTION\t{gameResolution}";
                continue;
            }
            if (p[0] == "CLK_DEF")
            {
                lines[i] = "CLK_DEF\t" + Integer(Round(Number(p[1]) * gameResolution / sourceResolution));
                continue;
            }
            if (!C2SRoundTrip.IsRecord(p[0])) continue;

            var start = Number(p[1]) * gameResolution + Number(p[2]) * gameResolution / sourceResolution;
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
                var end = start + Number(p[durationIndex]) * gameResolution / sourceResolution;
                var roundedEnd = Round(end);
                timingChanged |= end != roundedEnd;
                p[durationIndex] = Integer(checked(roundedEnd - roundedStart));
            }
            if (p[0] == "ALD")
            {
                var density = Number(p[5]) * gameResolution / sourceResolution;
                var roundedDensity = density > 0 ? Math.Max(1, Round(density)) : Round(density);
                timingChanged |= density != roundedDensity;
                p[5] = Integer(roundedDensity);
            }
            var measure = checked((long)decimal.Floor((decimal)roundedStart / gameResolution));
            p[1] = Integer(measure);
            p[2] = Integer(roundedStart - measure * gameResolution);
            lines[i] = string.Join('\t', p);
        }

        // Saved judge/progress summaries describe the pre-quantization chart.
        // Keep them only when all note and event positions remain exact.
        if (timingChanged)
            lines = lines.Where(line => !(C2SRoundTrip.Parts(line).FirstOrDefault() ?? "").StartsWith("T_", StringComparison.Ordinal)).ToArray();
        var result = string.Join('\n', lines);
        return timingChanged ? result + ChartStatistics.Calculate(result) : result;
    }
}
