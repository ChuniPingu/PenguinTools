using System.Globalization;
using PenguinTools.Chart.Models;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;
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

    public static string FormatSlaEditKey(UmgrModel.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return string.Join(
            ";",
            Flatten(chart.Notes.Children)
                .Where(note =>
                    note is not UmgrModel.SoflanArea and not UmgrModel.SoflanAreaJoint)
                .Select(note =>
                    string.Create(CultureInfo.InvariantCulture, $"{note.Tick.Original},{note.Lane},{note.Width},{note.Timeline}") +
                    (note is UmgrModel.AirCrash crash
                        ? string.Create(CultureInfo.InvariantCulture, $",ALD,{crash.Density.Original},{crash.Color}") : "") +
                    (CurveExpansion.IsControl(note) ? ",BEZIER" : ""))
                .OrderBy(key => key, StringComparer.Ordinal));
    }

    public static string FormatSlpEditKey(UmgrModel.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return string.Join(
            ";",
            chart.Events.Children
                .OfType<UmgrModel.ScrollSpeedEvent>()
                .OrderBy(x => x.Timeline)
                .ThenBy(x => x.Tick.Original)
                .Select(x =>
                    $"{x.Tick.Original},{x.Timeline}," +
                    x.Speed.ToString(
                        "0.############################",
                        CultureInfo.InvariantCulture)));
    }

    public static string FormatAirEditKey(UmgrModel.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return string.Join(
            ";",
            Flatten(chart.Notes.Children)
                .Where(note =>
                    note is UmgrModel.Air
                        or UmgrModel.AirSlide
                        or UmgrModel.AirSlideJoint
                        or UmgrModel.AirHold
                        or UmgrModel.AirHoldJoint)
                .Select(AirNoteEditKey)
                .OrderBy(key => key, StringComparer.Ordinal));
    }

    internal static string FormatAirPathEditKey(UmgrModel.Chart chart)
    {
        // Retain complete paths and pair ownership, not only a multiset of points.
        // Swapping continuations between coincident AIR roots is still an edit.
        var paths = chart.Notes.Children.OfType<UmgrModel.NegativeNote>()
            .Where(note => note is UmgrModel.Air or UmgrModel.AirSlide or UmgrModel.AirHold)
            .Select(FormatAirPathKey)
            .Order(StringComparer.Ordinal);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", paths))));
    }

    internal static string FormatAirPathKey(UmgrModel.NegativeNote note)
    {
        static string Path(UmgrModel.Note point) => EditorProjection.Key(point) + "[" +
            string.Join(";", point.Children.Select(Path)) + "]";
        static string AirPath(UmgrModel.Note point) => AirNoteEditKey(point) + "[" +
            string.Join(";", point.Children.Select(AirPath)) + "]";
        return AirPath(note) + "@" + (note.PairNote is { } pair
            ? EditorProjection.Key(pair) + ":" + Path(pair.Parent is UmgrModel.Hold or UmgrModel.Slide
                ? pair.Parent : pair)
            : "");
    }

    private static string AirNoteEditKey(UmgrModel.Note note) => note switch
    {
        UmgrModel.Air air =>
            $"AIR,{air.Tick.Original},{air.Timeline}," +
            $"{air.Lane},{air.Width},{air.Direction},{air.Color}," +
            $"{AirParentType(air.PairNote)}",

        UmgrModel.AirSlide airSlide =>
            $"AS,{airSlide.Tick.Original},{airSlide.Timeline}," +
            $"{airSlide.Lane},{airSlide.Width}," +
            $"{airSlide.Height.ToString("G29", CultureInfo.InvariantCulture)}," +
            $"{airSlide.Direction},{airSlide.Color}," +
            $"{AirParentType(airSlide.PairNote)}",

        UmgrModel.AirSlideJoint joint =>
            $"ASJ,{joint.Tick.Original},{joint.Timeline}," +
            $"{joint.Lane},{joint.Width}," +
            $"{joint.Height.ToString("G29", CultureInfo.InvariantCulture)}," +
            $"{joint.Joint}" + (joint.IsCurveControl ? ",BEZIER" : ""),

        UmgrModel.AirHold airHold =>
            $"AH,{airHold.Tick.Original},{airHold.Timeline}," +
            $"{airHold.Lane},{airHold.Width}," +
            $"{airHold.Direction},{airHold.Color}," +
            $"{AirParentType(airHold.PairNote)}",

        UmgrModel.AirHoldJoint joint =>
            $"AHJ,{joint.Tick.Original},{joint.Timeline}," +
            $"{joint.Lane},{joint.Width},{joint.Joint}",

        _ => throw new InvalidOperationException()
    };

    private static string AirParentType(UmgrModel.PositiveNote? parent) =>
        parent switch
        {
            UmgrModel.Tap => "TAP",
            UmgrModel.ExTap => "CHR",
            UmgrModel.Flick => "FLK",
            UmgrModel.Damage => "MNE",
            UmgrModel.HoldJoint => "HLD",
            UmgrModel.SlideJoint => "SLD",
            null => string.Empty,
            _ => parent.GetType().Name
        };

    private static IEnumerable<UmgrModel.Note> Flatten(IEnumerable<UmgrModel.Note> notes)
    {
        foreach (var note in notes)
        {
            yield return note;
            foreach (var child in Flatten(note.Children))
            {
                yield return child;
            }
        }
    }
}
