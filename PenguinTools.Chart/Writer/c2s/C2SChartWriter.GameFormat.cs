using System.Globalization;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Writer.c2s;

public partial class C2SChartWriter
{
    private const int GameResolution = 384;

    private string FormatForGame(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var sourceResolution = lines.Select(C2SRoundTrip.Parts)
            .Where(p => p.Length > 1 && p[0] == "RESOLUTION")
            .Select(p => decimal.Parse(p[1], CultureInfo.InvariantCulture)).Single();
        if (sourceResolution <= 0)
        {
            throw new FormatException("C2S resolution must be positive.");
        }

        var timingChanged = false;
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = FormatGameLine(lines[i], sourceResolution, ref timingChanged);
        }

        // Preserve a source-only judge rule without guessing extra notes. This
        // requires equal final records AND equal judge-related headers. Restore
        // source statement order too; a multiset alone cannot prove topology.
        if (!timingChanged && TryRestoreUnchangedJudgeSource(lines, out var restored))
        {
            Diagnostic.Report(new PathDiagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_C2s_source_judgements_retained), OutPath));
            return restored;
        }
        if (Chart.Extras.UnchangedJudgeSource is not null)
        {
            Diagnostic.Report(new PathDiagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_C2s_source_judgements_edited), OutPath));
        }

        // Judge counts describe the final accepted statements, after ordering,
        // snapshot restoration and quantization. Saved totals cannot prove that
        // the emitted note relationships remain valid.
        var prefix = timingChanged ? "T_" : "T_JUDGE_";
        lines = lines.Where(line => !(C2SRoundTrip.Parts(line).FirstOrDefault() ?? "")
            .StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        var result = string.Join('\n', lines);
        return result + ChartStatistics.Calculate(result);
    }

    private bool TryRestoreUnchangedJudgeSource(string[] output, out string result)
    {
        result = "";
        var source = Chart.Extras.UnchangedJudgeSource;
        if (string.IsNullOrEmpty(source) || C2sJudgeSource.Summary(source) is null)
        {
            return false;
        }

        var raw = source.Replace("\r", "").Split('\n');
        var resolutionRows = raw.Select(C2SRoundTrip.Parts).Where(p => p.Length == 2 && p[0] == "RESOLUTION").ToArray();
        if (resolutionRows.Length != 1 || !decimal.TryParse(resolutionRows[0][1], NumberStyles.Number,
                CultureInfo.InvariantCulture, out var resolution) || resolution <= 0)
        {
            return false;
        }

        bool changed = false;
        var normalized = raw.Select(line => FormatGameLine(line, resolution, ref changed)).ToArray();
        if (changed)
        {
            return false;
        }

        static string Tag(string line) => C2SRoundTrip.Parts(line).FirstOrDefault() ?? "";
        static string Canon(string line) => string.Join('\t', C2SRoundTrip.Parts(line).Select(p =>
            decimal.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                ? n.ToString("0.############################", CultureInfo.InvariantCulture) : p));
        static string[] Records(string[] rows) => rows.Where(l => C2SRoundTrip.IsRecord(Tag(l)))
            .Select(Canon).Order(StringComparer.Ordinal).ToArray();
        if (!Records(normalized).SequenceEqual(Records(output)))
        {
            return false;
        }

        var sourceNotes = normalized.Where(l => ChartExtras.NoteTags.Contains(Tag(l))).Select(Canon).ToArray();
        var outputNotes = output.Where(l => ChartExtras.NoteTags.Contains(Tag(l))).Select(Canon).ToArray();
        if (!C2sSlidePaths.PreservesPaths(outputNotes, sourceNotes, GameResolution))
        {
            return false;
        }

        // Equal records can still express different paths, AIR parents, or
        // same-tick event precedence. Restoring source order must preserve all
        // three, including edits that changed no individual record fields.
        static IEnumerable<string> EventGroups(string[] rows) => rows
            .Where(l => ChartExtras.EventTags.Contains(Tag(l))).Select(Canon)
            .GroupBy(l => string.Join('\t', C2SRoundTrip.Parts(l).Take(3)))
            .Select(group => string.Join('\n', group)).Order(StringComparer.Ordinal);
        if (!EventGroups(normalized).SequenceEqual(EventGroups(output)))
        {
            return false;
        }

        foreach (var key in new[] { "BPM_DEF", "MET_DEF", "PROGJUDGE_BPM", "PROGJUDGE_AER", "TUTORIAL" })
        {
            string[] Values(string[] rows) => rows.Where(l => Tag(l) == key).Select(Canon).ToArray();
            if (!Values(normalized).SequenceEqual(Values(output)))
            {
                return false;
            }
        }
        var headers = output.Where(l => !C2SRoundTrip.IsRecord(Tag(l)) &&
            !Tag(l).StartsWith("T_JUDGE_", StringComparison.Ordinal)).Where(l => !string.IsNullOrWhiteSpace(l));
        var records = normalized.Where(l => C2SRoundTrip.IsRecord(Tag(l)));
        var statistics = normalized.Where(l => Tag(l).StartsWith("T_JUDGE_", StringComparison.Ordinal)).Select(Canon);
        result = string.Join('\n', headers) + "\n\n" + string.Join('\n', records) + "\n\n" +
            string.Join('\n', statistics) + "\n";
        return true;
    }
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    // Match the original Time.Round implementation's Math.Round semantics.
    private static long Round(decimal value) => checked((long)Math.Round(value));
    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatGameLine(string line, decimal sourceResolution, ref bool timingChanged)
    {
        var p = C2SRoundTrip.Parts(line);
        if (p.Length < 2)
        {
            return line;
        }

        if (p[0] == "RESOLUTION")
        {
            return $"RESOLUTION\t{GameResolution}";
        }
        if (p[0] == "CLK_DEF")
        {
            return "CLK_DEF\t" + Integer(Round(Number(p[1]) * GameResolution / sourceResolution));
        }
        if (!C2SRoundTrip.IsRecord(p[0]))
        {
            return line;
        }

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
