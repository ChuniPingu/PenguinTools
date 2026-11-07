using System.Diagnostics.CodeAnalysis;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

/// <summary>Orders complete AIR paths before permanent matching tails can capture their continuations.</summary>
internal sealed class C2sAirScheduler
{
    private readonly record struct GroundCell(int Tick, int Lane, int Width, Type Kind);
    private readonly record struct AirCell(int Tick, int Lane, int Width, int Height, string Kind);

    private readonly List<C.Note> _notes;
    private readonly IReadOnlyDictionary<C.Note, C.Note> _groundParents;
    private readonly Dictionary<C.Note, int> _positions;
    private readonly List<C.Note[]> _nodes = [];
    private readonly Dictionary<C.Note, int> _nodeOf = [];
    private readonly List<HashSet<int>> _outgoing = [];
    private readonly List<int> _incoming = [];
    private readonly List<int> _priority = [];
    private readonly int _pathCount;
    private bool _cyclic;

    private C2sAirScheduler(List<C.Note> notes, IReadOnlyDictionary<C.Note, C.Note> groundParents)
    {
        _notes = notes;
        _groundParents = groundParents;
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
        // For positive rounded durations, continuing paths finish strictly
        // later than the matching terminal. This is an acyclic partial order;
        // ParentOrder uses it to coordinate ground roots sharing an AIR cell.
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

    private C.Note? GroundParent(C.Note note) =>
        _groundParents.TryGetValue(note, out var parent) ? parent : ((C.IPairable)note).Parent;

    private static Dictionary<C.Note, int> ParentOrder(List<C.Note> notes, IReadOnlyDictionary<C.Note, C.Note> groundParents)
    {
        var scheduler = new C2sAirScheduler(notes, groundParents);
        var result = new Dictionary<C.Note, int>();
        for (var i = 0; i < scheduler._pathCount; i++)
        {
            if (scheduler.GroundParent(scheduler._nodes[i][0]) is { } parent &&
                scheduler._positions.TryGetValue(parent, out var position))
            {
                scheduler._priority[i] = position;
            }
        }
        foreach (var node in scheduler.Run())
        {
            if (scheduler.GroundParent(scheduler._nodes[node][0]) is { } parent)
            {
                result.TryAdd(parent, result.Count);
            }
        }
        return result;
    }

    internal static List<C.Note> Schedule(List<C.Note> notes, IEnumerable<C.Slide[]>? slidePaths = null)
    {
        using var crashes = C2sAirCrashScheduler.Schedule(notes.OfType<C.AirCrash>()).GetEnumerator();
        notes = notes.Select(note => note is C.AirCrash && crashes.MoveNext() ? crashes.Current : note).ToList();
        var groundParents = ResolveGroundParents(notes);
        if (slidePaths is not null)
        {
            notes = ScheduleGroundParents(notes, slidePaths, groundParents);
        }

        var scheduler = new C2sAirScheduler(notes, groundParents);
        scheduler.AddGroundDependencies();
        var ordered = scheduler.Run();
        if (scheduler._cyclic)
        {
            // Direct C2S writes can retain a valid interleaved source order.
            // Reorder competing ground roots when complete AIR paths need it.
            var revised = ScheduleGroundParents(notes, C2sSlidePaths.Build(notes.OfType<C.Slide>(), rounded: true), groundParents);
            if (!revised.SequenceEqual(notes))
            {
                scheduler = new C2sAirScheduler(revised, groundParents);
                scheduler.AddGroundDependencies();
                ordered = scheduler.Run();
            }
        }
        return ordered.SelectMany(i => scheduler._nodes[i]).ToList();
    }

    private static Dictionary<C.Note, C.Note> ResolveGroundParents(List<C.Note> notes)
    {
        var roots = new Dictionary<C.Note, C.Note>();
        foreach (var path in C2sSlidePaths.Build(notes.OfType<C.Slide>(), rounded: true))
        {
            foreach (var segment in path)
            {
                roots[segment] = path[0];
            }
        }
        foreach (var path in C2sAirCrashPaths.Build(notes.OfType<C.AirCrash>()))
        {
            foreach (var segment in path)
            {
                roots[segment] = path[0];
            }
        }
        foreach (var note in notes.Where(n => n is not C.IPairable))
        {
            roots.TryAdd(note, note);
        }

        var positions = notes.Select((note, index) => (note, index)).ToDictionary(x => x.note, x => x.index);
        var candidates = roots.Keys.GroupBy(GroundEnd).ToDictionary(g => g.Key,
            g => g.OrderBy(n => positions[roots[n]]).ThenBy(n => positions[n]).ToArray());
        var used = notes.OfType<C.IPairable>().Select(p => p.Parent)
            .Where(p => p is not null && roots.ContainsKey(p)).Select(p => roots[p!]).ToHashSet();
        var resolved = new Dictionary<C.Note, C.Note>();
        foreach (var head in notes.Where(n => n is C.IPairable))
        {
            var pair = (C.IPairable)head;
            if (pair.Parent is not { } parent || parent is C.IPairable || roots.ContainsKey(parent) ||
                !candidates.TryGetValue(new GroundCell(head.Tick.Round, head.Lane, Math.Clamp(head.Width, head is C.AirSlide ? 0 : 1, 16),
                    parent.GetType()), out var matching))
            {
                continue;
            }
            var attachment = matching.FirstOrDefault(n => !used.Contains(roots[n]));
            if (attachment is not null)
            {
                // Editor carriers retain only the source parent kind. Resolve
                // the C2S statement's matching endpoint before reordering roots.
                resolved[head] = attachment;
                used.Add(roots[attachment]);
            }
        }
        return resolved;
    }

    private static List<C.Note> ScheduleGroundParents(List<C.Note> notes, IEnumerable<C.Slide[]> slidePaths,
        IReadOnlyDictionary<C.Note, C.Note> groundParents)
    {
        var parents = ParentOrder(notes, groundParents);
        var slides = C2sSlideScheduler.Schedule(slidePaths, parents);
        // Establish ground-path order; endpoint dependencies interleave AIR later.
        var revised = slides.Cast<C.Note>().Concat(notes.Where(n => n is not C.Slide)).ToList();
        var positions = revised.Select((note, index) => (note, index)).ToDictionary(x => x.note, x => x.index);
        foreach (var group in revised.Where(n => n is not C.Slide and not C.AirCrash and not C.IPairable).GroupBy(GroundEnd))
        {
            var records = group.OrderBy(n => parents.GetValueOrDefault(n, int.MaxValue)).ToArray();
            var slots = group.Select(n => positions[n]).Order().ToArray();
            for (var i = 0; i < slots.Length; i++)
            {
                revised[slots[i]] = records[i];
            }
        }
        return revised;
    }

    private static bool IsContinuation(C.Note note) => note is
        C.AirSlide { Parent: C.AirSlide } or C.AirHold { Parent: C.AirHold };

    private static AirCell Start(C.Note note) => note switch
    {
        C.AirSlide slide => new(slide.Tick.Round, slide.Lane, Math.Clamp(slide.Width, 0, 16), C2sHeight.Key(slide.Height), slide.Parent!.Id),
        _ => new(note.Tick.Round, note.Lane, Math.Clamp(note.Width, 1, 16), 0, "AHD")
    };

    private static AirCell End(C.LongNote note) => note switch
    {
        C.AirSlide slide => new(slide.EndTick.Round, slide.EndLane, Math.Clamp(slide.EndWidth, 0, 16), C2sHeight.Key(slide.EndHeight), slide.Id),
        _ => new(note.EndTick.Round, note.Lane, Math.Clamp(note.Width, 1, 16), 0, "AHD")
    };

    private static GroundCell GroundEnd(C.Note note) => note switch
    {
        C.Hold hold => new(hold.EndTick.Round, hold.Lane, Math.Clamp(hold.Width, 1, 16), note.GetType()),
        C.LongNote longNote => new(longNote.EndTick.Round, longNote.EndLane, Math.Clamp(longNote.EndWidth, note is C.Slide ? 0 : 1, 16), note.GetType()),
        C.Sla area => new(new Models.Time(checked(area.Tick.Original + area.Length.Original)).Round,
            area.Lane, Math.Clamp(area.Width, 1, 16), note.GetType()),
        _ => new(note.Tick.Round, note.Lane, Math.Clamp(note.Width, 1, 16), note.GetType())
    };

    [SuppressMessage("Reliability", "S4158",
        Justification = "The local Register function populates successors from SLIDE and AIR-CRUSH paths before attachment lookup.")]
    [SuppressMessage("Maintainability", "S2589",
        Justification = "The local Register function populates roots, so a matched attachment and root can both be non-null.")]
    private void AddGroundDependencies()
    {
        var roots = new Dictionary<C.Note, C.Note>();
        var successors = new Dictionary<C.Note, C.Note>();
        void Register(IEnumerable<C.Note> path)
        {
            var members = path.ToArray();
            for (var index = 0; index < members.Length; index++)
            {
                roots[members[index]] = members[0];
                if (index + 1 < members.Length)
                {
                    successors[members[index]] = members[index + 1];
                }
            }
        }
        foreach (var path in C2sSlidePaths.Build(_notes.OfType<C.Slide>(), rounded: true))
        {
            Register(path);
        }

        foreach (var path in C2sAirCrashPaths.Build(_notes.OfType<C.AirCrash>()))
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

        var candidates = roots.Keys.GroupBy(GroundEnd).ToDictionary(g => g.Key,
            g => g.OrderBy(n => _positions[roots[n]]).ThenBy(n => _positions[n]).ToArray());
        var owners = new Dictionary<int, int>();
        var used = new HashSet<C.Note>();
        for (var i = 0; i < _pathCount; i++)
        {
            var head = _nodes[i][0];
            var parent = GroundParent(head);
            C.Note? attachment = null;
            C.Note? root = null;
            if (parent is not null && roots.TryGetValue(parent, out var actual))
            {
                root = actual;
                attachment = parent;
            }
            else if (parent is not null && candidates.TryGetValue(
                         new GroundCell(head.Tick.Round, head.Lane, Math.Clamp(head.Width, head is C.AirSlide ? 0 : 1, 16), parent.GetType()), out var matching))
            {
                attachment = matching.FirstOrDefault(n => !used.Contains(roots[n]));
                root = attachment is null ? null : roots[attachment];
            }

            owners[i] = root is null ? _positions[head] : _positions[root];
            if (root is null || attachment is null)
            {
                continue;
            }

            used.Add(root);
            AddEdge(_nodeOf[attachment], i);
            // C2S matches an AIR declaration against the current ground endpoint.
            // Preserve that match before a later SLIDE row extends the path;
            // runtime AIR still starts at the completed ground path's final end.
            if (successors.TryGetValue(attachment, out var successor))
            {
                AddEdge(i, _nodeOf[successor]);
            }
        }
        foreach (var group in Enumerable.Range(0, _pathCount)
                     .Where(i => _nodes[i][0] is C.IPairable { Parent: not null })
                     .GroupBy(i => new GroundCell(_nodes[i][0].Tick.Round, _nodes[i][0].Lane, Math.Clamp(_nodes[i][0].Width, _nodes[i][0] is C.AirSlide ? 0 : 1, 16),
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
