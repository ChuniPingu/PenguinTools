using System.Globalization;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

public sealed record EditorNumberProjection(string Kind, string Key, List<decimal> Values);

internal static class EditorNumbers
{
    private static readonly string[] Kinds = ["MAINBPM", "BPM", "TIL", "SPDMOD"];
    private sealed record Slot(string Position, Func<decimal> Read, Action<decimal> Write);

    internal static void Project(U.Chart chart, IDiagnosticSink diagnostics)
    {
        chart.Extras.EditorNumberProjections.Clear();
        foreach (var kind in Kinds)
        {
            var slots = Slots(chart, kind).ToArray();
            var values = slots.Select(s => s.Read()).ToList();
            foreach (var slot in slots)
            {
                var original = slot.Read();
                var projected = NativeNumber.Project(original,
                    kind is "MAINBPM" or "BPM" ? NativeNumberKind.Bpm : NativeNumberKind.Speed, out var adjusted);
                slot.Write(projected);
                if (adjusted)
                {
                    var field = kind == "MAINBPM" ? kind : $"{kind}@{slot.Position}";
                    diagnostics.Report(new Diagnostic(Severity.Warning, Msg.Create(MsgKeys.Warn_Number_adjusted, field,
                        original.ToString("G29", CultureInfo.InvariantCulture), projected.ToString("G29", CultureInfo.InvariantCulture))));
                }
            }
            if (!values.SequenceEqual(slots.Select(s => s.Read())))
            {
                chart.Extras.EditorNumberProjections.Add(new(kind, Key(slots), values));
            }
        }
    }

    internal static void Restore(U.Chart chart)
    {
        foreach (var saved in chart.Extras.EditorNumberProjections)
        {
            var slots = Slots(chart, saved.Kind).ToArray();
            if (saved.Values.Count != slots.Length || saved.Key != Key(slots))
            {
                continue;
            }
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i].Write(saved.Values[i]);
            }
        }
    }

    private static string Key(IEnumerable<Slot> slots) =>
        C2SRoundTrip.OrderedViewHash(slots.Select(s =>
            $"{s.Position}\t{s.Read().ToString("G29", CultureInfo.InvariantCulture)}"));

    private static IEnumerable<Slot> Slots(U.Chart chart, string kind)
    {
        switch (kind)
        {
            case "MAINBPM":
                yield return new("MAINBPM", () => chart.Meta.MainBpm, value => chart.Meta.MainBpm = value);
                break;
            case "BPM":
                foreach (var e in chart.Events.Children.OfType<U.BpmEvent>().OrderBy(e => e.Tick.Original))
                {
                    yield return new(e.Tick.Original.ToString(CultureInfo.InvariantCulture), () => e.Bpm, value => e.Bpm = value);
                }
                break;
            case "TIL":
                foreach (var e in chart.Events.Children.OfType<U.ScrollSpeedEvent>()
                             .OrderBy(e => e.Timeline).ThenBy(e => e.Tick.Original))
                {
                    yield return new(string.Create(CultureInfo.InvariantCulture, $"{e.Timeline}:{e.Tick.Original}"),
                        () => e.Speed, value => e.Speed = value);
                }
                break;
            case "SPDMOD":
                foreach (var e in chart.Events.Children.OfType<U.NoteSpeedEvent>().OrderBy(e => e.Tick.Original))
                {
                    yield return new(e.Tick.Original.ToString(CultureInfo.InvariantCulture), () => e.Speed, value => e.Speed = value);
                }
                break;
        }
    }
}
