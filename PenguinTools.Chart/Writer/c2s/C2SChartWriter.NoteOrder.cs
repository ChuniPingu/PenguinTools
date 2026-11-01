using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart.Writer.c2s;

public partial class C2SChartWriter
{
    private static List<C.Note> OrderNoteDependencies(List<C.Note> notes)
    {
        var positions = notes.Select((note, index) => (note, index))
            .ToDictionary(x => x.note, x => x.index);
        var followers = notes.Select(_ => new HashSet<int>()).ToArray();
        var pending = new int[notes.Count];

        void Before(C.Note parent, C.Note child)
        {
            if (positions.TryGetValue(parent, out var from) &&
                positions.TryGetValue(child, out var to) && from != to &&
                followers[from].Add(to))
            {
                pending[to]++;
            }
        }

        foreach (var note in notes)
        {
            if (note is C.IPairable { Parent: { } parent })
            {
                Before(parent, note);
            }
        }

        // Retain the chosen consumption order at each ambiguous AIR cell while
        // postponing a continuation whose parent moved behind another AIR root.
        foreach (var group in notes.Where(n => n is C.IPairable { Parent: not null })
                     .GroupBy(n =>
                     {
                         var parent = ((C.IPairable)n).Parent!;
                         var kind = parent is C.AirSlide ? parent.Id : parent.GetType().Name;
                         var height = parent is C.AirSlide && n is C.AirSlide air ? air.Height.Result : 0;
                         return (n.Tick.Round, n.Lane, n.Width, kind, height);
                     }))
        {
            var ordered = group.ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                Before(ordered[i - 1], ordered[i]);
            }
        }

        foreach (var path in notes.OfType<C.AirCrash>().GroupBy(n => n.PathRoot ?? n))
        {
            var ordered = path.ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                Before(ordered[i - 1], ordered[i]);
            }
        }

        var ready = new PriorityQueue<int, int>();
        for (var i = 0; i < pending.Length; i++)
        {
            if (pending[i] == 0)
            {
                ready.Enqueue(i, i);
            }
        }
        var result = new List<C.Note>(notes.Count);
        while (ready.TryDequeue(out var index, out _))
        {
            result.Add(notes[index]);
            foreach (var next in followers[index])
            {
                if (--pending[next] == 0)
                {
                    ready.Enqueue(next, next);
                }
            }
        }
        return result.Count == notes.Count ? result : notes;
    }
}
