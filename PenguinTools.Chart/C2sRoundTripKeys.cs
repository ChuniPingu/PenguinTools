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
