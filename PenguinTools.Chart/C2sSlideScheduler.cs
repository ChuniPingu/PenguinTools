using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

/// <summary>Orders explicit SLIDE paths for C2S's first-matching-tail reader.</summary>
internal sealed class C2sSlideScheduler
{
    private readonly record struct Position(int Tick, int Lane, int Width, bool NoLine);
    private readonly record struct AirPosition(int Tick, int Lane, int Width);

    private readonly C.Slide[][] _paths;
    private readonly List<List<int>> _outgoing = [];
    private readonly List<int> _incoming = [];

    private C2sSlideScheduler(IEnumerable<C.Slide[]> paths, IReadOnlyDictionary<C.Note, int>? airParentOrder)
    {
        _paths = paths.ToArray();
        var starts = new Dictionary<Position, HashSet<int>>();
        var tails = new Dictionary<Position, List<int>>();
        var airCells = new Dictionary<AirPosition, List<int>>();
        for (var i = 0; i < _paths.Length; i++)
        {
            AddNode();
            foreach (var slide in _paths[i])
            {
                var key = new Position(slide.Tick.Round, slide.Lane, Math.Clamp(slide.Width, 1, 16), slide.NoLine);
                if (!starts.TryGetValue(key, out var pathsAtStart))
                {
                    starts[key] = pathsAtStart = [];
                }
                pathsAtStart.Add(i);
            }
            var tail = _paths[i][^1];
            var end = new Position(tail.EndTick.Round, tail.EndLane, Math.Clamp(tail.EndWidth, 1, 16), tail.NoLine);
            if (!tails.TryGetValue(end, out var pathsAtEnd))
            {
                tails[end] = pathsAtEnd = [];
            }
            pathsAtEnd.Add(i);
            var cell = new AirPosition(tail.EndTick.Round, tail.EndLane, Math.Clamp(tail.EndWidth, 1, 16));
            if (!airCells.TryGetValue(cell, out var airPaths))
            {
                airCells[cell] = airPaths = [];
            }
            airPaths.Add(i);
        }

        foreach (var (cell, ending) in tails)
        {
            if (starts.TryGetValue(cell, out var outgoing))
            {
                // Complete every path that passes through this cell before
                // leaving a permanent matching tail there. Whole paths avoid
                // the deadlock caused by advancing one of those paths halfway.
                // With positive rounded durations, the passing path ends later:
                // these edges strictly decrease final time and cannot cycle.
                AddBarrier(outgoing, ending);
            }
        }
        if (airParentOrder is not null)
        {
            foreach (var group in airCells.Values)
            {
                // AIR ignores line type and uses ground-root creation order.
                // These paths end at the same time, so the shared AIR rank can
                // break ties without reversing a continuation dependency.
                var attached = group.Where(i => airParentOrder.ContainsKey(_paths[i][^1]))
                    .OrderBy(i => airParentOrder[_paths[i][^1]]).ToArray();
                for (var i = 1; i < attached.Length; i++)
                {
                    AddBarrier([attached[i - 1]], [attached[i]]);
                }
                AddBarrier(attached, group.Where(i => !airParentOrder.ContainsKey(_paths[i][^1])));
            }
        }
    }

    internal static List<C.Slide> Schedule(IEnumerable<C.Slide[]> paths, IReadOnlyDictionary<C.Note, int>? airParentOrder = null) =>
        new C2sSlideScheduler(paths, airParentOrder).Run();

    private int AddNode()
    {
        _outgoing.Add([]);
        _incoming.Add(0);
        return _outgoing.Count - 1;
    }

    private void AddBarrier(IEnumerable<int> before, IEnumerable<int> after)
    {
        var predecessors = before.ToArray();
        var successors = after.ToArray();
        if (predecessors.Length == 0 || successors.Length == 0)
        {
            return;
        }
        // A shared node avoids quadratic edges for heavily overlapping charts.
        var barrier = AddNode();
        foreach (var predecessor in predecessors)
        {
            _outgoing[predecessor].Add(barrier);
            _incoming[barrier]++;
        }
        foreach (var successor in successors)
        {
            _outgoing[barrier].Add(successor);
            _incoming[successor]++;
        }
    }

    private List<C.Slide> Run()
    {
        var ready = new PriorityQueue<int, int>();
        var emitted = new bool[_incoming.Count];
        for (var i = 0; i < _incoming.Count; i++)
        {
            if (_incoming[i] == 0)
            {
                ready.Enqueue(i, i < _paths.Length ? i : -1);
            }
        }
        var result = new List<C.Slide>();
        var remaining = _paths.Length;
        var fallback = 0;
        while (remaining > 0)
        {
            if (!ready.TryDequeue(out var node, out _))
            {
                // Zero-length or reversed paths can make the dependencies
                // cyclic. Retain the records for the converter's diagnostics.
                while (emitted[fallback])
                {
                    fallback++;
                }
                node = fallback;
            }
            if (emitted[node])
            {
                continue;
            }
            emitted[node] = true;
            if (node < _paths.Length)
            {
                result.AddRange(_paths[node]);
                remaining--;
            }
            foreach (var next in _outgoing[node])
            {
                if (--_incoming[next] == 0)
                {
                    ready.Enqueue(next, next < _paths.Length ? next : -1);
                }
            }
        }
        return result;
    }
}
