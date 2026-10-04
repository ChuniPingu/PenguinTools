using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

using C2sModel = PenguinTools.Chart.Models.c2s;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Converter.c2s;

public partial class C2SChartConverter
{
    public C2SChartConverter(C2SConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Mgxc);

        Mgxc = request.Mgxc;
    }

    private DiagnosticCollector Diagnostic { get; } = new();
    private UmgrModel.Chart Mgxc { get; }
    private C2sModel.Chart C2s { get; } = new();
    private List<C2sModel.Note> Notes => C2s.Notes;
    private List<C2sModel.Event> Events => C2s.Events;

    private bool RestoreSlaSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sSlaSnapshot;

        if (snapshot is null)
        {
            return false;
        }

        if (!(Mgxc.Extras.BinarySnapshotValid && Mgxc.Extras.SlaModelKey == C2sRoundTripKeys.FormatSlaEditKey(Mgxc)) &&
            Mgxc.Meta.C2sSlaEditKey is { } editKey &&
            editKey != C2sRoundTripKeys.FormatSlaEditKey(Mgxc))
        {
            Mgxc.Meta.C2sSlaSnapshot = null;
            Mgxc.Meta.C2sSlaEditKey = null;
            return false;
        }

        if (snapshot.Length == 0)
        {
            return true;
        }

        var restored = new List<C2sModel.Sla>();

        foreach (var entry in snapshot.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = entry.Split(',');

            if (fields.Length != 5 ||
                !int.TryParse(fields[0], out var tick) ||
                !int.TryParse(fields[1], out var timeline) ||
                !int.TryParse(fields[2], out var lane) ||
                !int.TryParse(fields[3], out var width) ||
                !int.TryParse(fields[4], out var length))
            {
                return false;
            }

            restored.Add(new C2sModel.Sla
            {
                Tick = tick,
                Timeline = timeline,
                Lane = lane,
                Width = width,
                Length = length
            });
        }

        foreach (var sla in restored)
        {
            Notes.Add(sla);
        }

        return true;
    }

    private void RestoreSlpSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sSlpSnapshot;

        if (snapshot is null)
        {
            return;
        }

        if (Mgxc.Meta.C2sSlpEditKey is { } editKey &&
            editKey != C2sRoundTripKeys.FormatSlpEditKey(Mgxc))
        {
            Mgxc.Meta.C2sSlpSnapshot = null;
            Mgxc.Meta.C2sSlpEditKey = null;
            return;
        }

        var restored = new List<C2sModel.Slp>();

        if (snapshot.Length != 0)
        {
            foreach (var entry in snapshot.Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = entry.Split(',');

                if (fields.Length != 4 ||
                    !int.TryParse(fields[0], out var tick) ||
                    !int.TryParse(fields[1], out var timeline) ||
                    !int.TryParse(fields[2], out var length) ||
                    !decimal.TryParse(
                        fields[3],
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var speed))
                {
                    return;
                }

                restored.Add(new C2sModel.Slp
                {
                    Tick = tick,
                    Timeline = timeline,
                    Length = length,
                    Speed = speed
                });
            }
        }

        Events.RemoveAll(x => x is C2sModel.Slp);
        Events.AddRange((IEnumerable<C2sModel.Event>)restored);

    }

    private void RestoreAirSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sAirSnapshot;

        if (snapshot is null)
        {
            return;
        }

        if (!(Mgxc.Extras.BinarySnapshotValid && Mgxc.Extras.AirModelKey == C2sRoundTripKeys.FormatAirEditKey(Mgxc)) &&
            Mgxc.Meta.C2sAirEditKey is { } editKey &&
            editKey != C2sRoundTripKeys.FormatAirEditKey(Mgxc))
        {
            Mgxc.Meta.C2sAirSnapshot = null;
            Mgxc.Meta.C2sAirEditKey = null;
            return;
        }

        var restored = new List<C2sModel.Air>();

        if (snapshot.Length != 0)
        {
            foreach (var entry in snapshot.Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = entry.Split(',');

                if (fields.Length != 7 ||
                    !int.TryParse(fields[0], out var tick) ||
                    !int.TryParse(fields[1], out var timeline) ||
                    !int.TryParse(fields[2], out var lane) ||
                    !int.TryParse(fields[3], out var width) ||
                    !Enum.TryParse<AirDirection>(fields[4], out var direction) ||
                    !Enum.TryParse<Color>(fields[5], out var color))
                {
                    return;
                }

                var parent = CreateAirSnapshotParent(fields[6]);

                if (parent is null)
                {
                    return;
                }

                restored.Add(new C2sModel.Air
                {
                    Tick = tick,
                    Timeline = timeline,
                    Lane = lane,
                    Width = width,
                    Direction = direction,
                    Color = color,
                    Parent = parent
                });
            }
        }

        Notes.RemoveAll(x => x is C2sModel.Air);
        Notes.AddRange((IEnumerable<C2sModel.Note>)restored);

    }

    private static C2sModel.Note? CreateAirSnapshotParent(string id) =>
        id switch
        {
            "TAP" => new C2sModel.Tap(),
            "CHR" => new C2sModel.ExTap(),
            "MNE" => new C2sModel.Damage(),
            "FLK" => new C2sModel.Flick(),

            "HLD" => new C2sModel.Hold(),
            "HXD" => new C2sModel.Hold
            {
                Effect = ExEffect.UP
            },

            "SLC" => new C2sModel.Slide
            {
                Joint = Joint.C
            },
            "SLD" => new C2sModel.Slide
            {
                Joint = Joint.D
            },
            "SXC" => new C2sModel.Slide
            {
                Joint = Joint.C,
                Effect = ExEffect.UP
            },
            "SXD" => new C2sModel.Slide
            {
                Joint = Joint.D,
                Effect = ExEffect.UP
            },

            _ => null
        };

    private void RestoreMeterDefSnapshot()
    {
        if (Mgxc.Meta.C2sMeterDefDenominator is { } denominator)
        {
            C2s.Meta.BgmInitialDenominator = denominator;
        }

        if (Mgxc.Meta.C2sMeterDefNumerator is { } numerator)
        {
            C2s.Meta.BgmInitialNumerator = numerator;
        }
    }

    public OperationResult<C2sModel.Chart> Convert()
    {
        Diagnostic.TimeCalculator = Mgxc.GetCalculator();
        try
        {
            C2s.Meta = Mgxc.Meta;
            if (Mgxc.Extras.ParsedEventModelKey != C2SRoundTrip.ViewHash(ChartExtras.EventView(Mgxc).Split('\n')))
            {
                Mgxc.Extras.UnchangedEventKinds.Clear();
                Mgxc.Extras.CheckEventView(Mgxc);
            }
            C2s.Extras = Mgxc.Extras;

            var restoredSla = RestoreSlaSnapshot();

            foreach (var note in Mgxc.Notes.Children)
            {
                if (restoredSla && note is UmgrModel.SoflanArea)
                {
                    continue;
                }

                ConvertNote(note);
            }
            ResolvePairings();
            RestoreAirSnapshot();
            ConvertEvent(Mgxc);
            if (C2s.Extras.MeterEditKey == ChartExtras.BeatKey(Mgxc))
            {
                Events.RemoveAll(e => e is C2sModel.Met);
                Events.AddRange(C2s.Extras.Meters.Select(m => new C2sModel.Met
                { Tick = m.Tick, Numerator = m.Numerator, Denominator = m.Denominator }));
            }

            ScheduleC2sSlidePaths();
            ScheduleC2sAirParents();
            ValidateOverlappingAirParents();
            ValidateAmbiguousC2sSlidePaths();
            ValidateLongNoteLengths();
            // Audio preroll does not change the chart coordinate origin.
            RestoreSlpSnapshot();
            if (C2s.Extras.HasSpeedSnapshot && C2s.Extras.BinarySnapshotValid && C2s.Extras.SpeedModelKey == ChartExtras.SpeedKey(Mgxc))
            {
                Events.RemoveAll(e => e is C2sModel.SpeedEventBase);
                foreach (var saved in C2s.Extras.Speeds)
                {
#pragma warning disable CS0612
                    C2sModel.SpeedEventBase restored = saved.Tag switch
                    {
                        "SLP" => new C2sModel.Slp { Timeline = saved.Timeline },
                        "SFL" => new C2sModel.Sfl(),
                        "STP" => new C2sModel.Stop(),
                        _ => new C2sModel.Dcm()
                    };
#pragma warning restore CS0612
                    restored.Tick = saved.Tick;
                    restored.Length = saved.Length;
                    restored.Speed = saved.Speed;
                    Events.Add(restored);
                }
            }
            RestoreMeterDefSnapshot();

            return ValidatePairings()
                ? OperationResult<C2sModel.Chart>.Success(C2s).WithDiagnostics(Diagnostic)
                : OperationResult<C2sModel.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.Report(ex);
            return OperationResult<C2sModel.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
    }

    private readonly Dictionary<UmgrModel.Slide, int> _slideRootOrder = [];

    private void ScheduleC2sSlidePaths()
    {
        var slideCount = 0;
        for (var i = 0; i < Notes.Count; i++)
        {
            if (Notes[i] is C2sModel.Slide)
            {
                slideCount++;
            }
        }

        if (slideCount == 0)
        {
            return;
        }

        var originalIndex = new Dictionary<C2sModel.Note, int>(Notes.Count);
        for (var i = 0; i < Notes.Count; i++)
        {
            originalIndex[Notes[i]] = i;
        }

        // Readers follow each root's path in source order. Arrival order at a
        // shared endpoint can differ when the paths have different segments.
        foreach (var root in Notes.OfType<C2sModel.Slide>()
                     .Where(n => _slideSegmentSources[n].IsRoot)
                     .OrderBy(n => n.Tick.Round).ThenBy(n => n.Lane).ThenBy(n => n.Width)
                     .ThenBy(n => originalIndex[n]))
        {
            _slideRootOrder.TryAdd(_slideSegmentSources[root].SourceSlide, _slideRootOrder.Count);
        }

        // One bucket per (Round, lane, width), already in list order.
        var pendingByKey =
            new Dictionary<C2sSlidePosition, LinkedList<C2sModel.Slide>>();
        var pendingBySource =
            new Dictionary<(C2sSlidePosition Key, UmgrModel.Slide Source),
                LinkedListNode<C2sModel.Slide>>();

        CollectPendingSlides(pendingByKey, pendingBySource);

        var keysByRound = pendingByKey.Keys
            .GroupBy(k => k.Tick)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(k => k.Lane).ThenBy(k => k.Width).ToArray())
            .ToArray();

        var active = new Dictionary<C2sSlidePosition, Queue<UmgrModel.Slide>>();
        var scheduled = new List<C2sModel.Slide>(slideCount);

        foreach (var key in keysByRound.SelectMany(keys => keys))
        {
            SchedulePendingSlides(key, pendingByKey[key], pendingBySource, active, scheduled);
        }

        RebuildNotesWithScheduledSlides(scheduled, originalIndex);
    }

    private void CollectPendingSlides(Dictionary<C2sSlidePosition, LinkedList<C2sModel.Slide>> pendingByKey,
        Dictionary<(C2sSlidePosition Key, UmgrModel.Slide Source), LinkedListNode<C2sModel.Slide>> pendingBySource)
    {
        for (var i = 0; i < Notes.Count; i++)
        {
            if (Notes[i] is not C2sModel.Slide slide)
            {
                continue;
            }

            var key = new C2sSlidePosition(
                slide.Tick.Round,
                slide.Lane,
                slide.Width);

            if (!pendingByKey.TryGetValue(key, out var pending))
            {
                pending = new LinkedList<C2sModel.Slide>();
                pendingByKey[key] = pending;
            }

            var node = pending.AddLast(slide);
            var source = _slideSegmentSources[slide].SourceSlide;
            pendingBySource[(key, source)] = node;
        }

    }

    private void SchedulePendingSlides(C2sSlidePosition key, LinkedList<C2sModel.Slide> pending,
        Dictionary<(C2sSlidePosition Key, UmgrModel.Slide Source), LinkedListNode<C2sModel.Slide>> pendingBySource,
        Dictionary<C2sSlidePosition, Queue<UmgrModel.Slide>> active, List<C2sModel.Slide> scheduled)
    {
        while (pending.Count > 0)
        {
            active.TryGetValue(key, out var queue);
            if (queue is { Count: > 1 })
            {
                active[key] = queue = new Queue<UmgrModel.Slide>(queue.OrderBy(s => _slideRootOrder[s]));
            }

            var pickNode = SelectPendingSlide(key, pending, pendingBySource, queue);

            var pick = pickNode.Value;
            var source = _slideSegmentSources[pick];
            pendingBySource.Remove((key, source.SourceSlide));
            pending.Remove(pickNode);
            scheduled.Add(pick);

            if (queue is { Count: > 0 })
            {
                queue.Dequeue();
                if (queue.Count == 0)
                {
                    active.Remove(key);
                }
            }

            var end = new C2sSlidePosition(
                pick.EndTick.Round,
                pick.EndLane,
                pick.EndWidth);

            if (!active.TryGetValue(end, out var endQueue))
            {
                endQueue = new Queue<UmgrModel.Slide>();
                active[end] = endQueue;
            }

            endQueue.Enqueue(source.SourceSlide);
        }
    }

    private LinkedListNode<C2sModel.Slide> SelectPendingSlide(C2sSlidePosition key, LinkedList<C2sModel.Slide> pending,
        Dictionary<(C2sSlidePosition Key, UmgrModel.Slide Source), LinkedListNode<C2sModel.Slide>> pendingBySource,
        Queue<UmgrModel.Slide>? queue)
    {
        if (queue is { Count: > 0 } &&
            pendingBySource.TryGetValue(
                (key, queue.Peek()),
                out var continuation) &&
            continuation.List == pending)
        {
            return continuation;
        }

        for (var node = pending.First;
             node is not null;
             node = node.Next)
        {
            if (_slideSegmentSources[node.Value].IsRoot)
            {
                return node;
            }
        }

        return pending.First!;

    }

    private void IndexAirCells(Dictionary<(int Round, int Lane, int Width), List<C2sModel.Note>> airsByCell, Dictionary<C2sModel.Note, int> noteIndex)
    {
        for (var i = 0; i < Notes.Count; i++)
        {
            var note = Notes[i];
            noteIndex[note] = i;

            if (note is not C2sModel.IPairable { Parent: C2sModel.Slide })
            {
                continue;
            }

            var key = (note.Tick.Round, note.Lane, note.Width);
            if (!airsByCell.TryGetValue(key, out var list))
            {
                list = [];
                airsByCell[key] = list;
            }

            list.Add(note);
        }

    }

    private void IndexAirParentEnds(Dictionary<(int Round, int Lane, int Width), List<C2sModel.Slide>> lastSegmentsByEnd)
    {
        foreach (var note in Notes)
        {
            if (note is not C2sModel.Slide slide)
            {
                continue;
            }

            if (!_slideSegmentSources.TryGetValue(slide, out var src))
            {
                continue;
            }

            if (!_positivePairRealTargets.ContainsKey(src.EndJoint))
            {
                continue;
            }

            var end = (slide.EndTick.Round, slide.EndLane, slide.EndWidth);
            if (!lastSegmentsByEnd.TryGetValue(end, out var list))
            {
                list = [];
                lastSegmentsByEnd[end] = list;
            }

            list.Add(slide);
        }

    }

    private void ScheduleAirParentCell(List<C2sModel.Slide> lastSegments, List<C2sModel.Note> cellAirs,
        Dictionary<C2sModel.IPairable, C2sModel.Note> intended, Dictionary<C2sModel.Note, int> noteIndex)
    {
        var intendedParentRank = new Dictionary<C2sModel.Slide, int>();
        var rank = 0;
        foreach (var air in cellAirs)
        {
            if (intended.TryGetValue((C2sModel.IPairable)air, out var parent) &&
                parent is C2sModel.Slide slide &&
                intendedParentRank.TryAdd(slide, rank))
            {
                rank++;
            }
        }

        foreach (var startRoundGroup in lastSegments.GroupBy(s => s.Tick.Round))
        {
            var tied = startRoundGroup.ToList();
            if (tied.Count <= 1)
            {
                continue;
            }

            var desired = tied
                .OrderBy(s =>
                    intendedParentRank.TryGetValue(s, out var r)
                        ? r
                        : int.MaxValue)
                .ThenBy(s => noteIndex[s])
                .ToList();

            if (OverridesSlideFifo(tied, desired, noteIndex))
            {
                desired = tied.OrderBy(s => noteIndex[s]).ToList();
            }

            ApplyNoteOrder((IReadOnlyList<C2sModel.Note>)desired, noteIndex);
        }

    }

    private static bool OverridesSlideFifo(List<C2sModel.Slide> tied, List<C2sModel.Slide> desired, Dictionary<C2sModel.Note, int> noteIndex)
    {
        // Same start (lane, width) order belongs to the slide FIFO.
        foreach (var startKey in tied.GroupBy(s => (s.Lane, s.Width)))
        {
            var scheduledOrder = startKey
                .OrderBy(s => noteIndex[s])
                .ToList();
            var desiredOrder = desired
                .Where(s =>
                    s.Lane == startKey.Key.Lane &&
                    s.Width == startKey.Key.Width)
                .ToList();

            if (!scheduledOrder.SequenceEqual(desiredOrder))
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildNotesWithScheduledSlides(
        List<C2sModel.Slide> scheduled,
        Dictionary<C2sModel.Note, int> originalIndex)
    {
        // Single-pass group: avoid O(rounds × notes) rescans.
        var slidesByRound = new Dictionary<int, List<C2sModel.Slide>>();
        foreach (var slide in scheduled)
        {
            var round = slide.Tick.Round;
            if (!slidesByRound.TryGetValue(round, out var list))
            {
                list = [];
                slidesByRound[round] = list;
            }

            list.Add(slide);
        }

        var othersByRound = new Dictionary<int, List<C2sModel.Note>>();
        foreach (var note in Notes)
        {
            if (note is C2sModel.Slide)
            {
                continue;
            }

            var round = note.Tick.Round;
            if (!othersByRound.TryGetValue(round, out var list))
            {
                list = [];
                othersByRound[round] = list;
            }

            list.Add(note);
        }

        foreach (var list in othersByRound.Values)
        {
            list.Sort((a, b) => originalIndex[a].CompareTo(originalIndex[b]));
        }

        var rounds = slidesByRound.Keys
            .Concat(othersByRound.Keys)
            .Distinct()
            .OrderBy(r => r);

        var rebuilt = new List<C2sModel.Note>(Notes.Count);
        foreach (var round in rounds)
        {
            if (slidesByRound.TryGetValue(round, out var slides))
            {
                rebuilt.AddRange((IEnumerable<C2sModel.Note>)slides);
            }

            if (othersByRound.TryGetValue(round, out var others))
            {
                rebuilt.AddRange(others);
            }
        }

        Notes.Clear();
        Notes.AddRange(rebuilt);
    }

    private Dictionary<C2sModel.IPairable, C2sModel.Note> BuildIntendedAirParents()
    {
        var intended = new Dictionary<C2sModel.IPairable, C2sModel.Note>();
        foreach (var (source, root) in _negativePairRoots)
        {
            if (source.PairNote is null)
            {
                continue;
            }

            if (_positivePairRealTargets.TryGetValue(source.PairNote, out var real))
            {
                intended[root] = real;
            }
        }

        return intended;
    }

    private void ScheduleC2sAirParents()
    {
        var intended = BuildIntendedAirParents();
        if (intended.Count == 0)
        {
            return;
        }

        var airsByCell =
            new Dictionary<(int Round, int Lane, int Width), List<C2sModel.Note>>();
        var noteIndex = new Dictionary<C2sModel.Note, int>(Notes.Count);

        IndexAirCells(airsByCell, noteIndex);

        if (airsByCell.Count == 0)
        {
            return;
        }

        // Index last segments that can own Air once; Air cells look up by end cell.
        var lastSegmentsByEnd =
            new Dictionary<(int Round, int Lane, int Width), List<C2sModel.Slide>>();
        IndexAirParentEnds(lastSegmentsByEnd);

        foreach (var (cell, cellAirs) in airsByCell)
        {
            lastSegmentsByEnd.TryGetValue(cell, out var lastSegments);
            lastSegments ??= [];

            ScheduleAirParentCell(lastSegments, cellAirs, intended, noteIndex);

            var orderedAirs = cellAirs
                .OrderBy(a =>
                {
                    if (intended.TryGetValue((C2sModel.IPairable)a, out var parent))
                    {
                        return noteIndex.GetValueOrDefault(parent, int.MaxValue);
                    }

                    return int.MaxValue;
                })
                .ThenBy(a => noteIndex[a])
                .ToList();

            ApplyNoteOrder(orderedAirs, noteIndex);
        }
    }

    private void ApplyNoteOrder(
        IReadOnlyList<C2sModel.Note> desiredOrder,
        Dictionary<C2sModel.Note, int> noteIndex)
    {
        if (desiredOrder.Count <= 1)
        {
            return;
        }

        var slots = new int[desiredOrder.Count];
        for (var i = 0; i < desiredOrder.Count; i++)
        {
            slots[i] = noteIndex[desiredOrder[i]];
        }

        Array.Sort(slots);

        for (var i = 0; i < slots.Length; i++)
        {
            var note = desiredOrder[i];
            Notes[slots[i]] = note;
            noteIndex[note] = slots[i];
        }
    }

    private void ValidateOverlappingAirParents()
    {
        var intended = BuildIntendedAirParents();
        if (intended.Count == 0)
        {
            return;
        }

        var used = new HashSet<C2sModel.Note>();
        var warned = new HashSet<(int Tick, int Lane, int Width)>();

        var candidatesByCell = BuildSlideAttachCandidates();

        var pairables = Notes
            .Select((note, index) => (note, index))
            .Where(x => x.note is C2sModel.IPairable { Parent: C2sModel.Slide })
            .OrderBy(x => x.note.Tick.Round)
            .ThenBy(x => x.index)
            .Select(x => (C2sModel.IPairable)x.note);

        foreach (var pairable in pairables)
        {
            var note = (C2sModel.Note)pairable;
            var cell = (note.Tick.Original, note.Lane, note.Width);
            if (!candidatesByCell.TryGetValue(cell, out var candidates))
            {
                continue;
            }

            var bound = FindSlidePairParent(note, candidates, used);
            if (bound is null)
            {
                continue;
            }

            used.Add(bound);

            if (!intended.TryGetValue(pairable, out var expected) ||
                ReferenceEquals(bound, expected))
            {
                continue;
            }

            if (!warned.Add(cell))
            {
                continue;
            }

            Diagnostic.Report(new TimedDiagnostic(
                Severity.Warning,
                Msg.Key(MsgKeys.Mg_Overlapping_air_parent_slide),
                note.Tick.Original));
        }
    }

    private Dictionary<(int Tick, int Lane, int Width), List<C2sModel.Note>> BuildSlideAttachCandidates()
    {
        // Index slide attach cells once. Same cell can host start and end parents.
        var candidatesByCell =
            new Dictionary<(int Tick, int Lane, int Width), List<C2sModel.Note>>();

        void AddCandidate(C2sModel.Note slide, int tick, int lane, int width)
        {
            var key = (tick, lane, width);
            if (!candidatesByCell.TryGetValue(key, out var list))
            {
                list = [];
                candidatesByCell[key] = list;
            }

            list.Add(slide);
        }

        foreach (var note in Notes)
        {
            if (note is not C2sModel.Slide slide)
            {
                continue;
            }

            AddCandidate(slide, slide.Tick.Original, slide.Lane, slide.Width);

            if (slide.EndTick.Original != slide.Tick.Original ||
                slide.EndLane != slide.Lane ||
                slide.EndWidth != slide.Width)
            {
                AddCandidate(
                    slide,
                    slide.EndTick.Original,
                    slide.EndLane,
                    slide.EndWidth);
            }
        }

        return candidatesByCell;
    }

    private static C2sModel.Note? FindSlidePairParent(
        C2sModel.Note note,
        List<C2sModel.Note> candidates,
        HashSet<C2sModel.Note> used)
    {
        C2sModel.Note? best = null;
        var bestUsed = false;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!IsSlideAttachPoint(candidate, note))
            {
                continue;
            }

            var candidateUsed = used.Contains(candidate);
            var distance = SlidePairDistance(candidate, note);

            if (best is not null && IsWorseParent(candidateUsed, bestUsed, distance, bestDistance))
            {
                continue;
            }

            best = candidate;
            bestUsed = candidateUsed;
            bestDistance = distance;
        }

        return best;
    }

    private static bool IsWorseParent(bool candidateUsed, bool bestUsed, int distance, int bestDistance) =>
        candidateUsed && !bestUsed || candidateUsed == bestUsed && distance >= bestDistance;

    private static bool IsSlideAttachPoint(C2sModel.Note candidate, C2sModel.Note note)
    {
        if (candidate is C2sModel.LongNote longNote &&
            longNote.EndTick.Original == note.Tick.Original &&
            longNote.EndLane == note.Lane &&
            longNote.EndWidth == note.Width)
        {
            return true;
        }

        return candidate.Tick.Original == note.Tick.Original &&
               candidate.Lane == note.Lane &&
               candidate.Width == note.Width;
    }

    private static int SlidePairDistance(C2sModel.Note candidate, C2sModel.Note note)
    {
        if (candidate is C2sModel.LongNote longNote)
        {
            return Math.Abs(longNote.EndTick.Original - note.Tick.Original);
        }

        return Math.Abs(candidate.Tick.Original - note.Tick.Original);
    }

    private void ValidateAmbiguousC2sSlidePaths()
    {
        // Replay endpoint linking in root source order. Times
        // are rounded here because distinct UMIGURI ticks can serialize to the
        // same 1/384 C2S tick and become ambiguous only after conversion.
        // Order matches the writer: Round, then scheduled list index.
        var active = new Dictionary<C2sSlidePosition, Queue<OpenC2sSlidePath>>();

        foreach (var note in Notes
                     .OfType<C2sModel.Slide>()
                     .Select((slide, index) => new { Slide = slide, SourceOrder = index })
                     .OrderBy(x => x.Slide.Tick.Round)
                     .ThenBy(x => x.SourceOrder)
                     .Select(x => x.Slide))
        {
            var source = _slideSegmentSources[note];
            var start = new C2sSlidePosition(
                note.Tick.Round,
                note.Lane,
                note.Width);

            OpenC2sSlidePath? open = null;
            if (active.TryGetValue(start, out var queue) && queue.Count > 0)
            {
                if (queue.Count > 1)
                {
                    active[start] = queue = new Queue<OpenC2sSlidePath>(queue.OrderBy(p => _slideRootOrder[p.SourceSlide]));
                }

                open = queue.Dequeue();
                if (queue.Count == 0)
                {
                    active.Remove(start);
                }
            }

            if (source.IsRoot &&
                open is not null &&
                !ReferenceEquals(open.SourceSlide, source.SourceSlide))
            {
                var message = Msg.Create(
                    MsgKeys.Mg_Ambiguous_c2s_slide_path,
                    start.Lane,
                    start.Width);

                Diagnostic.Report(new TimedDiagnostic(
                    Severity.Information,
                    message,
                    start.Tick)
                {
                    Target = NotePairDiagnosticTarget.From(
                            source.SourceSlide,
                            open.EndJoint)
                        .WithTime(
                            Diagnostic.TimeCalculator!,
                            start.Tick)
                });
            }

            var end = new C2sSlidePosition(
                note.EndTick.Round,
                note.EndLane,
                note.EndWidth);

            if (!active.TryGetValue(end, out var endQueue))
            {
                endQueue = new Queue<OpenC2sSlidePath>();
                active[end] = endQueue;
            }

            endQueue.Enqueue(new OpenC2sSlidePath(
                open?.SourceSlide ?? source.SourceSlide,
                source.EndJoint));
        }
    }

    private void ValidateLongNoteLengths()
    {
        foreach (var longNote in Notes.OfType<C2sModel.LongNote>())
        {
            var length = longNote.Length.Original;
            if (length >= ChartResolution.SingleTick)
            {
                continue;
            }

            var tick = longNote.Tick.Original;
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Length_smaller_than_unit, length,
                ChartResolution.UmiguriTick / ChartResolution.SingleTick);
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick)
            {
                Target = longNote
            });
        }

        foreach (var sla in Notes.OfType<C2sModel.Sla>())
        {
            if (sla.Length.Original >= ChartResolution.SingleTick)
            {
                continue;
            }

            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Length_smaller_than_unit, sla.Length.Original,
                ChartResolution.UmiguriTick / ChartResolution.SingleTick);
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, sla.Tick.Original)
            {
                Target = sla
            });
        }
    }

    private bool ValidatePairings()
    {
        var hasError = false;
        foreach (var air in Notes.OfType<C2sModel.Air>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(
                new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_parent_null), air.Tick.Original)
                {
                    Target = air
                });
            hasError = true;
        }

        foreach (var airSlide in Notes.OfType<C2sModel.AirSlide>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null),
                airSlide.Tick.Original)
            {
                Target = airSlide
            });
            hasError = true;
        }

        foreach (var airHold in Notes.OfType<C2sModel.AirHold>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null),
                airHold.Tick.Original)
            {
                Target = airHold
            });
            hasError = true;
        }

        return !hasError;
    }

    private readonly record struct C2sSlidePosition(
        int Tick,
        int Lane,
        int Width);

    private sealed record C2sSlideSegmentSource(
        UmgrModel.Slide SourceSlide,
        UmgrModel.SlideJoint EndJoint,
        bool IsRoot);

    private sealed record OpenC2sSlidePath(
        UmgrModel.Slide SourceSlide,
        UmgrModel.SlideJoint EndJoint);
}
