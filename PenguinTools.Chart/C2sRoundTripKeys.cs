using System.Globalization;
using PenguinTools.Chart.Models;

namespace PenguinTools.Chart;

using umgr = Models.umgr;

/// <summary>
/// Canonical fingerprints of UMGR soflan state. C2S SLA/SLP snapshots are restored
/// only while these keys still match the chart that produced them.
/// </summary>
internal static class C2sRoundTripKeys
{
    public static string FormatSlaSnapshot(IEnumerable<Models.c2s.Sla> notes) =>
        string.Join(
            ";",
            notes.Select(x =>
                $"{x.Tick.Original},{x.Timeline},{x.Lane},{x.Width},{x.Length.Original}"));

    public static string FormatSlpSnapshot(IEnumerable<Models.c2s.Slp> events) =>
        string.Join(
            ";",
            events.Select(x =>
                $"{x.Tick.Original},{x.Timeline},{x.Length.Original}," +
                x.Speed.ToString(CultureInfo.InvariantCulture)));

    public static string FormatAirSnapshot(IEnumerable<Models.c2s.Air> notes) =>
        string.Join(
            ";",
            notes.Select(x =>
                $"{x.Tick.Original},{x.Timeline},{x.Lane},{x.Width}," +
                $"{x.Direction},{x.Color},{C2sAirParentId(x.Parent)}"));

    private static string C2sAirParentId(Models.c2s.Note? parent) =>
        parent switch
        {
            Models.c2s.Tap => "TAP",
            Models.c2s.ExTap => "CHR",
            Models.c2s.Flick => "FLK",
            Models.c2s.Damage => "MNE",
            Models.c2s.Hold => "HLD",
            Models.c2s.Slide => "SLD",
            null => string.Empty,
            _ => parent.Id
        };

    public static string FormatSlaEditKey(umgr.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return string.Join(
            ";",
            Flatten(chart.Notes.Children)
                .Where(note =>
                    note.Timeline != 0 &&
                    note is not umgr.SoflanArea and not umgr.SoflanAreaJoint)
                .Select(note =>
                    $"{note.Tick.Original},{note.Lane},{note.Width},{note.Timeline}")
                .OrderBy(key => key, StringComparer.Ordinal));
    }

    public static string FormatSlpEditKey(umgr.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return string.Join(
            ";",
            chart.Events.Children
                .OfType<umgr.ScrollSpeedEvent>()
                .OrderBy(x => x.Timeline)
                .ThenBy(x => x.Tick.Original)
                .Select(x =>
                    $"{x.Tick.Original},{x.Timeline}," +
                    x.Speed.ToString(
                        "0.############################",
                        CultureInfo.InvariantCulture)));
    }

    public static string FormatAirEditKey(umgr.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return string.Join(
            ";",
            Flatten(chart.Notes.Children)
                .Where(note =>
                    note is umgr.Air
                        or umgr.AirSlide
                        or umgr.AirSlideJoint
                        or umgr.AirHold
                        or umgr.AirHoldJoint)
                .Select(note => note switch
                {
                    umgr.Air air =>
                        $"AIR,{air.Tick.Original},{air.Timeline}," +
                        $"{air.Lane},{air.Width},{air.Direction},{air.Color}," +
                        $"{AirParentType(air.PairNote)}",

                    umgr.AirSlide airSlide =>
                        $"AS,{airSlide.Tick.Original},{airSlide.Timeline}," +
                        $"{airSlide.Lane},{airSlide.Width}," +
                        $"{airSlide.Height.ToString(CultureInfo.InvariantCulture)}," +
                        $"{airSlide.Direction},{airSlide.Color}," +
                        $"{AirParentType(airSlide.PairNote)}",

                    umgr.AirSlideJoint joint =>
                        $"ASJ,{joint.Tick.Original},{joint.Timeline}," +
                        $"{joint.Lane},{joint.Width}," +
                        $"{joint.Height.ToString(CultureInfo.InvariantCulture)}," +
                        $"{joint.Joint}",

                    umgr.AirHold airHold =>
                        $"AH,{airHold.Tick.Original},{airHold.Timeline}," +
                        $"{airHold.Lane},{airHold.Width}," +
                        $"{airHold.Direction},{airHold.Color}," +
                        $"{AirParentType(airHold.PairNote)}",

                    umgr.AirHoldJoint joint =>
                        $"AHJ,{joint.Tick.Original},{joint.Timeline}," +
                        $"{joint.Lane},{joint.Width},{joint.Joint}",

                    _ => throw new InvalidOperationException()
                })
                .OrderBy(key => key, StringComparer.Ordinal));
    }

    private static string AirParentType(umgr.PositiveNote? parent) =>
        parent switch
        {
            umgr.Tap => "TAP",
            umgr.ExTap => "CHR",
            umgr.Flick => "FLK",
            umgr.Damage => "MNE",
            umgr.HoldJoint => "HLD",
            umgr.SlideJoint => "SLD",
            null => string.Empty,
            _ => parent.GetType().Name
        };

    private static IEnumerable<umgr.Note> Flatten(IEnumerable<umgr.Note> notes)
    {
        foreach (var note in notes)
        {
            yield return note;
            foreach (var child in Flatten(note.Children))
                yield return child;
        }
    }
}
