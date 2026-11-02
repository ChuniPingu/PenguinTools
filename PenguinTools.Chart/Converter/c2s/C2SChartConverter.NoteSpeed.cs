using System.Diagnostics.CodeAnalysis;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Converter.c2s;

public partial class C2SChartConverter
{
    [SuppressMessage("Reliability", "S1244",
        Justification = "Only an exact native float32 multiplier of one leaves the note speed unchanged; nearby multipliers still need a warning.")]
    private void WarnIfNoteSpeedIsLost(U.Chart chart, U.NoteSpeedEvent[] modifiers)
    {
        var warnZero = modifiers.Any(e => e.Speed == 0);
        var warnUndefined = (chart.Extras.EditorFormatVersion > 0 ||
            chart.Meta.FilePath.EndsWith(".ugc", StringComparison.OrdinalIgnoreCase) ||
            chart.Meta.FilePath.EndsWith(".mgxc", StringComparison.OrdinalIgnoreCase)) &&
            modifiers.Any(e => e.Speed is not (0 or 1));
        if (!warnZero && !warnUndefined)
        {
            return;
        }
        var timelines = chart.Events.Children.OfType<U.ScrollSpeedEvent>()
            .GroupBy(e => e.Timeline)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Tick).ToArray());
        foreach (var (note, tick, timeline, _, _) in UgcSpeedFields.Points(chart))
        {
            // The editor applies SPDMOD to each note, only on a defined TIL.
            // C2S ignores a zero DCM instead of using it as a speed multiplier.
            var modifier = modifiers.LastOrDefault(e => e.Tick.Original <= tick);
            if (modifier is null || modifier.Speed == 1)
            {
                continue;
            }
            string key;
            if (!timelines.TryGetValue(timeline, out var speeds))
            {
                if (!warnUndefined || modifier.Speed == 0 ||
                    (float)NativeNumber.ToDouble(modifier.Speed, preserveSingle: true) == 1)
                {
                    continue;
                }
                key = MsgKeys.Warn_Editor_undefined_timeline_speed;
                warnUndefined = false;
            }
            else
            {
                var speed = speeds.LastOrDefault(e => e.Tick.Original <= Math.Max(tick, 0))?.Speed ?? 1m;
                if (!warnZero || modifier.Speed != 0 || speed == 0)
                {
                    continue;
                }
                key = MsgKeys.Warn_Zero_note_speed_not_reproduced;
                warnZero = false;
            }
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, Msg.Key(key), tick) { Target = note });
            if (!warnZero && !warnUndefined)
            {
                return;
            }
        }
    }
}
