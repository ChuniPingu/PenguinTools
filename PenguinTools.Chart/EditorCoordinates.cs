using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class EditorCoordinates
{
    internal static void UseFixedOrigin(U.Chart chart)
    {
        chart.Extras.FixedZeroOrigin |= ChartExtras.HasFixedZeroOrigin(chart.Meta.Copyright) ||
            chart.Events.Children.OfType<U.BookmarkEvent>().Any(b => ChartExtras.HasFixedZeroOrigin(b.Tag));
        chart.Extras.PrerollBar1Applied |= ChartExtras.HasPrerollBar1(chart.Meta.Copyright) ||
            chart.Events.Children.OfType<U.BookmarkEvent>().Any(b => ChartExtras.HasPrerollBar1(b.Tag));
        if (chart.Extras.UsesFixedCoordinateOrigin)
        {
            chart.Extras.ApplyCoordinateOrigin(chart.Meta);
            return;
        }
        var initialBeat = chart.Events.Children.OfType<U.BeatEvent>().LastOrDefault(b => b.Bar == 0);
        if (initialBeat is not null)
        {
            chart.Meta.BgmInitialNumerator = initialBeat.Numerator;
            chart.Meta.BgmInitialDenominator = initialBeat.Denominator;
        }
        if (chart.Events.Children.OfType<U.BpmEvent>().LastOrDefault(b => b.Tick.Original == 0) is { } bpm)
        {
            chart.Meta.BgmInitialBpm = bpm.Bpm;
        }
        var offset = chart.Meta.BgmEnableBarOffset
            ? checked((int)Math.Round(1920m * chart.Meta.BgmInitialNumerator / chart.Meta.BgmInitialDenominator)) : 0;
        if (offset != 0)
        {
            // These opaque records cannot be translated without their own coordinate contract.
            if (chart.Extras.LockedCurves.Count > 0 || chart.Extras.EditorImageEvents.Count > 0 ||
                chart.Extras.EditorHeaders.Any(l => l.StartsWith("@SPDFLD", StringComparison.OrdinalIgnoreCase)))
            {
                throw new DiagnosticException(Msg.Create(MsgKeys.MgCrit_Editor_target_feature,
                    "fixed origin", "legacy-origin locked curves, image events or relative speed fields"));
            }
            var notes = chart.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children))
                .Distinct().Select(n => (Note: n, Tick: n.Tick.Original)).ToArray();
            foreach (var (note, tick) in notes)
            {
                note.Tick = checked(tick + offset);
            }
            foreach (var e in chart.Events.Children)
            {
                if (e.Tick.Original > 0)
                {
                    e.Tick = checked(e.Tick.Original + (e is U.BreakingMarker ? 1 : offset));
                }
                if (e is U.BeatEvent { Bar: > 0 } beat)
                {
                    beat.Bar = checked(beat.Bar + 1);
                }
            }
            chart.Extras.ClickTicks = chart.Extras.ClickTicks.Select(t => checked(t + offset)).ToList();
            chart.Extras.PrerollBar1Applied = true;
            // The target's fixed timeline no longer derives an audio delay from SOFFSET.
            // Fold that existing delay into the target offset without changing the source.
            chart.Meta.BgmManualOffset += chart.Meta.BgmBarOffset;
            chart.Extras.HasSpeedSnapshot = false;
            chart.Extras.Meters.Clear();
            chart.Extras.MeterEditKey = null;
            chart.Extras.CurveProfiles.Clear();
        }
        chart.Extras.FixedZeroOrigin = true;
        chart.Meta.BgmFixedZeroOrigin = true;
    }
}
