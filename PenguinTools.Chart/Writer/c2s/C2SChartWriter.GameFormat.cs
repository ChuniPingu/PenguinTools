using System.Globalization;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Writer.c2s;

public partial class C2SChartWriter
{
    private const int GameResolution = 384;

    private string? FormatForGame(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var parts = lines.Select(C2SRoundTrip.Parts).ToArray();
        var sourceResolution = parts
            .Where(p => p.Length > 1 && p[0] == "RESOLUTION")
            .Select(p => decimal.Parse(p[1], CultureInfo.InvariantCulture)).Single();
        if (sourceResolution <= 0)
        {
            throw new FormatException("C2S resolution must be positive.");
        }

        var timingChanged = false;
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = FormatGameLine(lines[i], parts[i], sourceResolution, ref timingChanged);
        }

        if (!ValidateJudgeHeaders(parts) || !ValidateBpmTimeline(parts))
        {
            return null;
        }

        // Judge counts describe the final accepted statements, after ordering,
        // snapshot restoration and quantization. Saved totals cannot prove that
        // the emitted note relationships remain valid.
        var prefix = timingChanged ? "T_" : "T_JUDGE_";
        bool Retain(int index) => !(parts[index].FirstOrDefault() ?? "").StartsWith(prefix, StringComparison.Ordinal);
        var result = string.Join('\n', lines.Where((_, index) => Retain(index)));
        try
        {
            return result + ChartStatistics.Calculate(parts.Where((_, index) => Retain(index)),
                (tick, bpm) => new TimedDiagnosticException(
                    Msg.Create(MsgKeys.C2s_Judgement_step_collapsed, bpm.ToString("G9", CultureInfo.InvariantCulture)),
                    (int)Math.Clamp(tick * (Models.ChartResolution.UmiguriTick / GameResolution), int.MinValue, int.MaxValue)));
        }
        catch (TimedDiagnosticException ex)
        {
            Diagnostic.Report(ex.ToDiagnostic().WithPathFallback(OutPath));
            return null;
        }
    }

    private bool ValidateJudgeHeaders(string[][] rows)
    {
        var valid = true;
        for (var i = 0; i < rows.Length; i++)
        {
            var tag = rows[i].FirstOrDefault();
            if (tag is "PROGJUDGE_BPM" or "PROGJUDGE_AER" &&
                !C2sNumber.TryReadFiniteSingle(rows[i].ElementAtOrDefault(1), out _))
            {
                Diagnostic.Report(new LocationDiagnostic(Severity.Error,
                    Msg.Create(MsgKeys.C2s_Invalid_field, tag), i + 1, OutPath));
                valid = false;
            }
        }
        return valid;
    }

    private bool ValidateBpmTimeline(string[][] rows)
    {
        var records = rows.Select((row, index) => (Row: row, Line: index + 1))
            .Where(record => record.Row.Length > 0 && C2SRoundTrip.IsRecord(record.Row[0]))
            .Select(record =>
            {
                var tick = Number(record.Row[1]) * GameResolution + Number(record.Row[2]);
                var durationIndex = C2SRoundTrip.DurationIndex(record.Row[0]);
                var end = durationIndex < 0 ? tick : tick + Math.Max(0, Number(record.Row[durationIndex]));
                return (record.Row, record.Line, Tick: tick, End: end);
            })
            .ToArray();
        if (records.Length == 0)
        {
            return true;
        }

        var bpms = records.Where(record => record.Row[0] == "BPM").OrderBy(record => record.Tick).ToArray();
        var firstTick = Math.Min(0, records.Min(record => record.Tick));
        if (bpms.Length == 0 || bpms[0].Tick > firstTick)
        {
            Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.C2s_Bpm_timeline_not_covered), OutPath));
            return false;
        }

        float milliseconds = 0;
        var previousTick = 0m;
        float previousBpm = 0;
        foreach (var (row, line, tick, _) in bpms)
        {
            if (!C2sNumber.TryReadFiniteSingle(row[3], out var bpm) || bpm <= 0)
            {
                Diagnostic.Report(new LocationDiagnostic(Severity.Error,
                    Msg.Create(MsgKeys.C2s_Bpm_must_be_positive, row[3]), line, OutPath));
                return false;
            }

            milliseconds += SpanMilliseconds(tick - previousTick, previousBpm == 0 ? bpm : previousBpm);
            if (!float.IsFinite(milliseconds))
            {
                Diagnostic.Report(new LocationDiagnostic(Severity.Error,
                    Msg.Key(MsgKeys.C2s_Bpm_time_nonfinite), line, OutPath));
                return false;
            }
            previousBpm = bpm;
            previousTick = tick;
        }

        var last = records.MaxBy(record => record.End);
        if (!float.IsFinite(milliseconds + SpanMilliseconds(last.End - previousTick, previousBpm)))
        {
            Diagnostic.Report(new LocationDiagnostic(Severity.Error, Msg.Key(MsgKeys.C2s_Bpm_time_nonfinite), last.Line, OutPath));
            return false;
        }

        return true;
    }

    private static float SpanMilliseconds(decimal ticks, float bpm) =>
        ((float)(ticks * 4) * (1f / GameResolution)) * 60000f / bpm;
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    // Match the original Time.Round implementation's Math.Round semantics.
    private static long Round(decimal value) => checked((long)Math.Round(value));
    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatGameLine(string line, string[] p, decimal sourceResolution, ref bool timingChanged)
    {
        if (p.Length < 2)
        {
            return line;
        }

        if (p[0] == "RESOLUTION")
        {
            p[1] = Integer(GameResolution);
            return $"RESOLUTION\t{p[1]}";
        }
        if (p[0] == "CLK_DEF")
        {
            p[1] = Integer(Round(Number(p[1]) * GameResolution / sourceResolution));
            return "CLK_DEF\t" + p[1];
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
        var durationIndex = C2SRoundTrip.DurationIndex(p[0]);
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
