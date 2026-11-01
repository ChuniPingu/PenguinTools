using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class ChartCopy
{
    internal static U.Chart Create(U.Chart source)
    {
        var notes = new Dictionary<U.Note, U.Note>();
        var copy = new U.Chart
        {
            Meta = source.Meta with { },
            Extras = source.Extras.Copy(),
            Notes = CopyTree(source.Notes, notes),
            Events = CopyTree(source.Events, [])
        };
        foreach (var (original, cloned) in notes)
        {
            if (cloned is U.PositiveNote positive)
            {
                positive.PairNote = original is U.PositiveNote { PairNote: { } pair } && notes.TryGetValue(pair, out var target)
                    ? (U.NegativeNote)target : null;
            }
            if (cloned is U.NegativeNote negative)
            {
                negative.PairNote = original is U.NegativeNote { PairNote: { } pair } && notes.TryGetValue(pair, out var target)
                    ? (U.PositiveNote)target : null;
            }
        }
        return copy;
    }

    private static T CopyTree<T>(T node, Dictionary<T, T> copies) where T : U.TimeNode<T>
    {
        var copy = node.CopyNode();
        copies.Add(node, copy);
        foreach (var child in node.Children)
        {
            copy.AppendChild(CopyTree(child, copies));
        }
        return copy;
    }
}
