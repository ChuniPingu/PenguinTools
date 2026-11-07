using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class UgcNoteOrder
{
    internal static void Apply(U.Chart chart)
    {
        var roots = chart.Notes.Children.ToArray();
        var outgoing = roots.Select(_ => new HashSet<int>()).ToArray();
        var incoming = new int[roots.Length];
        var attachments = roots.SelectMany((root, owner) => new[] { root }.Concat(root.Children)
            .OfType<U.PositiveNote>().Select((note, position) => new
            {
                Owner = owner,
                Position = position,
                Note = note,
                Rank = note.PairNote switch
                {
                    U.AirHold => 0,
                    U.AirSlide => 1,
                    U.Air { Direction: AirDirection.IR } => 2,
                    _ => -1
                }
            })).Where(a => a.Rank >= 0);

        // Native UGC first binds AIR arrows to ground notes, then AIR-HOLD
        // and AIR-SLIDE claim the first free Up AIR at the same position.
        // Keep complete ground blocks in that order across all ground kinds.
        foreach (var cell in attachments.GroupBy(a => (a.Note.Tick.Original, a.Note.Lane, a.Note.Width)))
        {
            var points = cell.ToArray();
            foreach (var before in points)
            {
                foreach (var after in points.Where(a => a.Rank > before.Rank))
                {
                    if (before.Owner == after.Owner)
                    {
                        if (before.Position > after.Position)
                        {
                            Fail(before.Note);
                        }
                        continue;
                    }
                    if (outgoing[before.Owner].Add(after.Owner))
                    {
                        incoming[after.Owner]++;
                    }
                }
            }
        }

        var ready = new PriorityQueue<int, int>();
        for (var i = 0; i < roots.Length; i++)
        {
            if (incoming[i] == 0)
            {
                ready.Enqueue(i, i);
            }
        }
        var order = new List<int>(roots.Length);
        while (ready.TryDequeue(out var index, out _))
        {
            order.Add(index);
            foreach (var next in outgoing[index])
            {
                if (--incoming[next] == 0)
                {
                    ready.Enqueue(next, next);
                }
            }
        }
        if (order.Count != roots.Length)
        {
            Fail(roots[Array.FindIndex(incoming, n => n > 0)]);
        }
        if (order.Select(i => roots[i]).SequenceEqual(roots))
        {
            return;
        }
        foreach (var root in roots)
        {
            chart.Notes.RemoveChild(root);
        }
        foreach (var index in order)
        {
            chart.Notes.AppendChild(roots[index]);
        }
    }

    private static void Fail(U.Note note) => throw new TimedDiagnosticException(
        Msg.Key(MsgKeys.MgCrit_Ugc_air_attachment_order), note.Tick.Original, note);
}
