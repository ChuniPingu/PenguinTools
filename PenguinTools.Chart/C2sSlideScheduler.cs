using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

/// <summary>Orders explicit SLIDE paths for C2S's first-matching-tail reader.</summary>
internal sealed class C2sSlideScheduler
{
    private readonly record struct Position(int Tick, int Lane, int Width, bool NoLine);

    private readonly Dictionary<C.Slide, C.Slide> _roots = [];
    private readonly HashSet<C.Slide> _tails = [];
    private readonly Dictionary<C.Slide, int> _rootOrder = [];
    private readonly Dictionary<Position, LinkedList<C.Slide>> _pending = [];
    private readonly Dictionary<(Position, C.Slide), Queue<LinkedListNode<C.Slide>>> _bySource = [];
    private readonly Dictionary<Position, HashSet<Position>> _waitingTails = [];
    private readonly Dictionary<Position, PriorityQueue<C.Slide, int>> _active = [];
    private readonly PriorityQueue<Position, (int, int, int, bool)> _ready = new();
    private readonly HashSet<Position> _queued = [];

    private C2sSlideScheduler(IEnumerable<C.Slide[]> paths)
    {
        foreach (var path in paths)
        {
            foreach (var slide in path)
            {
                Add(slide, path[0]);
            }
            var tail = path[^1];
            _tails.Add(tail);
            if (!_waitingTails.TryGetValue(End(tail), out var starts))
            {
                _waitingTails[End(tail)] = starts = [];
            }
            starts.Add(Start(tail));
        }
    }

    internal static List<C.Slide> Schedule(IEnumerable<C.Slide[]> paths) => new C2sSlideScheduler(paths).Run();

    private void Add(C.Slide slide, C.Slide root)
    {
        _roots[slide] = root;
        var start = Start(slide);
        if (!_pending.TryGetValue(start, out var list))
        {
            _pending[start] = list = new();
        }
        if (!_bySource.TryGetValue((start, root), out var queue))
        {
            _bySource[(start, root)] = queue = new();
        }
        queue.Enqueue(list.AddLast(slide));
    }

    private List<C.Slide> Run()
    {
        foreach (var key in _pending.Keys)
        {
            Enqueue(key);
        }
        var scheduled = new List<C.Slide>(_roots.Count);
        while (scheduled.Count < _roots.Count)
        {
            var pick = TakeNext();
            scheduled.Add(pick.Value);
            Emit(pick);
        }
        return scheduled;
    }

    private LinkedListNode<C.Slide> TakeNext()
    {
        while (_ready.TryDequeue(out var key, out _))
        {
            _queued.Remove(key);
            if (Next(key) is { } pick)
            {
                return pick;
            }
        }

        // Degenerate/reversed segments can make the constraints unsatisfiable.
        // Keep them for the converter's existing path diagnostics.
        var fallback = _pending.Where(p => p.Value.Count > 0)
            .MinBy(p => (p.Key.Tick, p.Key.Lane, p.Key.Width, p.Key.NoLine)).Key;
        return Continuation(fallback) ?? _pending[fallback].First!;
    }

    private LinkedListNode<C.Slide>? Continuation(Position key) =>
        _active.TryGetValue(key, out var paths) && paths.TryPeek(out var root, out _) &&
        _bySource.TryGetValue((key, root), out var segments) ? segments.Peek() : null;

    private LinkedListNode<C.Slide>? Next(Position key)
    {
        if (_active.ContainsKey(key))
        {
            var pick = Continuation(key);
            return pick is not null && !WaitsForOutgoing(pick.Value) ? pick : null;
        }

        for (var node = _pending[key].First; node is not null; node = node.Next)
        {
            if (ReferenceEquals(_roots[node.Value], node.Value) && !WaitsForOutgoing(node.Value))
            {
                return node;
            }
        }
        return null;
    }

    private bool WaitsForOutgoing(C.Slide note)
    {
        // A finished path remains a continuation candidate forever. Emit
        // every segment starting at its endpoint first, so neither a new
        // head nor another path's continuation can be captured by it.
        var end = End(note);
        return _tails.Contains(note) && end != Start(note) &&
            _pending.TryGetValue(end, out var outgoing) && outgoing.Count > 0;
    }

    private void Enqueue(Position key)
    {
        if (_pending.ContainsKey(key) && !_queued.Contains(key) && Next(key) is not null)
        {
            _queued.Add(key);
            _ready.Enqueue(key, (key.Tick, key.Lane, key.Width, key.NoLine));
        }
    }

    private void Emit(LinkedListNode<C.Slide> pick)
    {
        var note = pick.Value;
        var key = Start(note);
        var root = _roots[note];
        _pending[key].Remove(pick);
        var sourceQueue = _bySource[(key, root)];
        sourceQueue.Dequeue();
        if (sourceQueue.Count == 0)
        {
            _bySource.Remove((key, root));
        }
        _rootOrder.TryAdd(root, _rootOrder.Count);
        if (_active.TryGetValue(key, out var paths))
        {
            paths.Dequeue();
            if (paths.Count == 0)
            {
                _active.Remove(key);
            }
        }

        var end = End(note);
        if (!_active.TryGetValue(end, out var endPaths))
        {
            _active[end] = endPaths = new();
        }
        endPaths.Enqueue(root, _rootOrder[root]);
        Enqueue(key);
        Enqueue(end);
        if (_pending[key].Count == 0 && _waitingTails.TryGetValue(key, out var tails))
        {
            foreach (var tail in tails)
            {
                Enqueue(tail);
            }
        }
    }

    private static Position Start(C.Slide slide) => new(slide.Tick.Round, slide.Lane, slide.Width, slide.NoLine);
    private static Position End(C.Slide slide) => new(slide.EndTick.Round, slide.EndLane, slide.EndWidth, slide.NoLine);
}
