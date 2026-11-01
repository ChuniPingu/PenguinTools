using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

/// <summary>Orders complete AIR paths before permanent matching tails can capture their continuations.</summary>
internal sealed class C2sAirScheduler
{
    private readonly record struct GroundCell(int Tick, int Lane, int Width, Type Kind);
    private readonly record struct AirCell(int Tick, int Lane, int Width, int Height, string Kind);

    private readonly List<C.Note> _notes;
    private readonly Dictionary<C.Note, int> _positions;
    private readonly List<C.Note[]> _nodes = [];
    private readonly Dictionary<C.Note, int> _nodeOf = [];
    private readonly List<HashSet<int>> _outgoing = [];
    private readonly List<int> _incoming = [];
    private readonly List<int> _priority = [];
    private readonly int _pathCount;
    private bool _cyclic;

    private C2sAirScheduler(List<C.Note> notes)
    {
        _notes = notes;
        _positions = notes.Select((note, index) => (note, index)).ToDictionary(x => x.note, x => x.index);
        var heads = new Dictionary<C.Note, C.Note>();
        C.Note Head(C.Note note)
        {
            var visited = new HashSet<C.Note>();
            var current = note;
            while (IsContinuation(current) && !heads.ContainsKey(current) && visited.Add(current))
            {
                current = ((C.IPairable)current).Parent!;
            }
            var head = heads.GetValueOrDefault(current, current);
            foreach (var member in visited)
            {
                heads[member] = head;
            }
            return head;
        }

        var children = notes.Where(IsContinuation).ToLookup(n => ((C.IPairable)n).Parent!);
        foreach (var group in notes.Where(n => n is C.IPairable).GroupBy(Head))
        {
            var ordered = new List<C.Note>();
            var pending = new Queue<C.Note>();
            var visited = new HashSet<C.Note>();
            pending.Enqueue(group.Key);
            while (pending.TryDequeue(out var note) && visited.Add(note))
            {
                if (_positions.ContainsKey(note))
                {
                    ordered.Add(note);
                }
                foreach (var child in children[note])
                {
                    pending.Enqueue(child);
                }
            }
            ordered.AddRange(group.Where(n => !visited.Contains(n)));
            AddNode(ordered.ToArray(), _positions[group.First()]);
        }
        _pathCount = _nodes.Count;

        var continuations = notes.Where(IsContinuation).GroupBy(Start).ToDictionary(g => g.Key,
            g => g.Select(n => _nodeOf[n]).Distinct().ToArray());
        for (var i = 0; i < _pathCount; i++)
        {
            if (_nodes[i][^1] is C.LongNote terminal && continuations.TryGetValue(End(terminal), out var outgoing))
            {
                foreach (var path in outgoing)
                {
                    AddEdge(path, i);
                }
            }
        }
    }

    internal static Dictionary<C.Note, int> ParentOrder(List<C.Note> notes)
    {
        var scheduler = new C2sAirScheduler(notes);
        var result = new Dictionary<C.Note, int>();
        for (var i = 0; i < scheduler._pathCount; i++)
        {
            if (scheduler._nodes[i][0] is C.IPairable { Parent: { } parent } &&
                scheduler._positions.TryGetValue(parent, out var position))
            {
                scheduler._priority[i] = position;
            }
        }
        foreach (var node in scheduler.Run())
        {
            if (scheduler._nodes[node][0] is C.IPairable { Parent: { } parent })
            {
                result.TryAdd(parent, result.Count);
            }
        }
        return result;
    }

    internal static List<C.Note> Schedule(List<C.Note> notes)
    {
        var scheduler = new C2sAirScheduler(notes);
        scheduler.AddGroundDependencies();
        var ordered = scheduler.Run();
        if (scheduler._cyclic)
        {
            // Direct C2S writes can retain a valid interleaved source order.
            // Reorder competing ground roots when complete AIR paths need it.
            var parents = ParentOrder(notes);
            var revised = notes.ToList();
            var slides = C2sSlideScheduler.Schedule(C2sSlidePaths.Build(notes.OfType<C.Slide>(), rounded: true), parents);
            var slideIndex = 0;
            for (var i = 0; i < revised.Count; i++)
            {
                if (revised[i] is C.Slide)
                {
                    revised[i] = slides[slideIndex++];
                }
            }
            foreach (var group in notes.Where(n => n is not C.Slide and not C.AirCrash and not C.IPairable).GroupBy(GroundEnd))
            {
                var records = group.OrderBy(n => parents.GetValueOrDefault(n, int.MaxValue)).ToArray();
                var slots = group.Select(n => scheduler._positions[n]).Order().ToArray();
                for (var i = 0; i < slots.Length; i++)
                {
                    revised[slots[i]] = records[i];
                }
            }
            if (!revised.SequenceEqual(notes))
            {
                scheduler = new C2sAirScheduler(revised);
                scheduler.AddGroundDependencies();
                ordered = scheduler.Run();
            }
        }
        return ordered.SelectMany(i => scheduler._nodes[i]).ToList();
    }

    private static bool IsContinuation(C.Note note) => note is
        C.AirSlide { Parent: C.AirSlide } or C.AirHold { Parent: C.AirHold };

    private static AirCell Start(C.Note note) => note switch
    {
        C.AirSlide slide => new(slide.Tick.Round, slide.Lane, slide.Width, C2sHeight.Key(slide.Height), slide.Parent!.Id),
        _ => new(note.Tick.Round, note.Lane, note.Width, 0, "AHD")
    };

    private static AirCell End(C.LongNote note) => note switch
    {
        C.AirSlide slide => new(slide.EndTick.Round, slide.EndLane, slide.EndWidth, C2sHeight.Key(slide.EndHeight), slide.Id),
        _ => new(note.EndTick.Round, note.Lane, note.Width, 0, "AHD")
    };

    private static GroundCell GroundEnd(C.Note note) => note switch
    {
        C.Hold hold => new(hold.EndTick.Round, hold.Lane, hold.Width, note.GetType()),
        C.LongNote longNote => new(longNote.EndTick.Round, longNote.EndLane, longNote.EndWidth, note.GetType()),
        C.Sla area => new(new Models.Time(checked(area.Tick.Original + area.Length.Original)).Round,
            area.Lane, area.Width, note.GetType()),
        _ => new(note.Tick.Round, note.Lane, note.Width, note.GetType())
    };

    private void AddGroundDependencies()
    {
        var roots = new Dictionary<C.Note, C.Note>();
        var tails = new Dictionary<C.Note, C.Note>();
        void Register(IEnumerable<C.Note> path)
        {
            var members = path.ToArray();
            foreach (var member in members)
            {
                roots[member] = members[0];
            }
            tails[members[0]] = members[^1];
        }
        foreach (var path in C2sSlidePaths.Build(_notes.OfType<C.Slide>(), rounded: true))
        {
            Register(path);
        }

        foreach (var path in C2sAirCrashPaths.Build(_notes.OfType<C.AirCrash>(), includeAxis: false))
        {
            Register(path);
        }

        int? previous = null;
        foreach (var note in _notes.Where(n => n is not C.IPairable))
        {
            if (!roots.ContainsKey(note))
            {
                Register([note]);
            }

            var node = AddNode([note], _positions[note]);
            if (previous is { } before)
            {
                AddEdge(before, node);
            }

            previous = node;
        }

        var candidates = tails.GroupBy(p => GroundEnd(p.Value)).ToDictionary(g => g.Key,
            g => g.Select(p => p.Key).OrderBy(n => _positions[n]).ToArray());
        var owners = new Dictionary<int, int>();
        var used = new HashSet<C.Note>();
        for (var i = 0; i < _pathCount; i++)
        {
            var head = _nodes[i][0];
            var parent = ((C.IPairable)head).Parent;
            C.Note? root = null;
            if (parent is not null && roots.TryGetValue(parent, out var actual))
            {
                root = actual;
            }
            else if (parent is not null && candidates.TryGetValue(
                         new GroundCell(head.Tick.Round, head.Lane, head.Width, parent.GetType()), out var matching))
            {
                root = matching.FirstOrDefault(n => !used.Contains(n));
            }

            owners[i] = root is null ? _positions[head] : _positions[root];
            if (root is null)
            {
                continue;
            }

            used.Add(root);
            AddEdge(_nodeOf[tails[root]], i);
        }
        foreach (var group in Enumerable.Range(0, _pathCount)
                     .Where(i => _nodes[i][0] is C.IPairable { Parent: not null })
                     .GroupBy(i => new GroundCell(_nodes[i][0].Tick.Round, _nodes[i][0].Lane, _nodes[i][0].Width,
                         ((C.IPairable)_nodes[i][0]).Parent!.GetType())))
        {
            previous = null;
            foreach (var path in group.OrderBy(i => owners[i]).ThenBy(i => _priority[i]))
            {
                if (previous is { } before)
                {
                    AddEdge(before, path);
                }

                previous = path;
            }
        }
    }

    private int AddNode(C.Note[] records, int priority)
    {
        int index = _nodes.Count;
        _nodes.Add(records);
        _outgoing.Add([]);
        _incoming.Add(0);
        _priority.Add(priority);
        foreach (var note in records)
        {
            _nodeOf[note] = index;
        }

        return index;
    }

    private void AddEdge(int before, int after)
    {
        if (_outgoing[before].Add(after))
        {
            _incoming[after]++;
        }
    }

    private List<int> Run()
    {
        var ready = new PriorityQueue<int, int>();
        var emitted = new bool[_nodes.Count];
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_incoming[i] == 0)
            {
                ready.Enqueue(i, _priority[i]);
            }
        }
        var result = new List<int>(_nodes.Count);
        while (result.Count < _nodes.Count)
        {
            if (!ready.TryDequeue(out var node, out _))
            {
                // Degenerate geometry can make path dependencies cyclic.
                _cyclic = true;
                node = Array.FindIndex(emitted, value => !value);
            }
            if (emitted[node])
            {
                continue;
            }

            emitted[node] = true;
            result.Add(node);
            foreach (var next in _outgoing[node])
            {
                if (--_incoming[next] == 0)
                {
                    ready.Enqueue(next, _priority[next]);
                }
            }
        }
        return result;
    }
}
