using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Converter.c2s;

using umgr = Models.umgr;
using c2s = Models.c2s;

public partial class C2SChartConverter
{
    public C2SChartConverter(C2SConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Mgxc);

        Mgxc = request.Mgxc;
    }

    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private umgr.Chart Mgxc { get; }
    private c2s.Chart C2s { get; } = new();
    private List<c2s.Note> Notes => C2s.Notes;
    private List<c2s.Event> Events => C2s.Events;

    private bool RestoreSlaSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sSlaSnapshot;

        if (snapshot is null)
            return false;

        if (Mgxc.Meta.C2sSlaEditKey is { } editKey &&
            editKey != C2sRoundTripKeys.FormatSlaEditKey(Mgxc))
        {
            Mgxc.Meta.C2sSlaSnapshot = null;
            Mgxc.Meta.C2sSlaEditKey = null;
            return false;
        }

        if (snapshot.Length == 0)
            return true;

        var restored = new List<c2s.Sla>();

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
                return false;

            restored.Add(new c2s.Sla
            {
                Tick = tick,
                Timeline = timeline,
                Lane = lane,
                Width = width,
                Length = length
            });
        }

        foreach (var sla in restored)
            Notes.Add(sla);

        return true;
    }

    private bool RestoreSlpSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sSlpSnapshot;

        if (snapshot is null)
            return false;

        if (Mgxc.Meta.C2sSlpEditKey is { } editKey &&
            editKey != C2sRoundTripKeys.FormatSlpEditKey(Mgxc))
        {
            Mgxc.Meta.C2sSlpSnapshot = null;
            Mgxc.Meta.C2sSlpEditKey = null;
            return false;
        }

        var restored = new List<c2s.Slp>();

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
                    return false;

                restored.Add(new c2s.Slp
                {
                    Tick = tick,
                    Timeline = timeline,
                    Length = length,
                    Speed = speed
                });
            }
        }

        Events.RemoveAll(x => x is c2s.Slp);
        Events.AddRange(restored);

        return true;
    }

    private bool RestoreAirSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sAirSnapshot;

        if (snapshot is null)
            return false;

        if (Mgxc.Meta.C2sAirEditKey is { } editKey &&
            editKey != C2sRoundTripKeys.FormatAirEditKey(Mgxc))
        {
            Mgxc.Meta.C2sAirSnapshot = null;
            Mgxc.Meta.C2sAirEditKey = null;
            return false;
        }

        var restored = new List<c2s.Air>();

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
                    return false;
                }

                var parent = CreateAirSnapshotParent(fields[6]);

                if (parent is null)
                    return false;

                restored.Add(new c2s.Air
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

        Notes.RemoveAll(x => x is c2s.Air);
        Notes.AddRange(restored);

        return true;
    }

    private static c2s.Note? CreateAirSnapshotParent(string id) =>
        id switch
        {
            "TAP" => new c2s.Tap(),
            "CHR" => new c2s.ExTap(),
            "MNE" => new c2s.Damage(),
            "FLK" => new c2s.Flick(),

            "HLD" => new c2s.Hold(),
            "HXD" => new c2s.Hold
            {
                Effect = ExEffect.UP
            },

            "SLC" => new c2s.Slide
            {
                Joint = Joint.C
            },
            "SLD" => new c2s.Slide
            {
                Joint = Joint.D
            },
            "SXC" => new c2s.Slide
            {
                Joint = Joint.C,
                Effect = ExEffect.UP
            },
            "SXD" => new c2s.Slide
            {
                Joint = Joint.D,
                Effect = ExEffect.UP
            },

            _ => null
        };

    private void RestoreMeterDefSnapshot()
    {
        if (Mgxc.Meta.C2sMeterDefDenominator is { } denominator)
            C2s.Meta.BgmInitialDenominator = denominator;

        if (Mgxc.Meta.C2sMeterDefNumerator is { } numerator)
            C2s.Meta.BgmInitialNumerator = numerator;
    }

    public OperationResult<c2s.Chart> Convert()
    {
        Diagnostic.TimeCalculator = Mgxc.GetCalculator();
        try
        {
            C2s.Meta = Mgxc.Meta;

            var restoredSla = RestoreSlaSnapshot();

            foreach (var note in Mgxc.Notes.Children)
            {
                if (restoredSla && note is umgr.SoflanArea)
                    continue;

                ConvertNote(note);
            }
            ResolvePairings();
            RestoreAirSnapshot();
            ConvertEvent(Mgxc);

            ScheduleC2sSlidePaths();
            ScheduleC2sAirParents();
            ValidateOverlappingAirParents();
            ValidateAmbiguousC2sSlidePaths();
            ValidateLongNoteLengths();
            ApplyBgmBarOffset();
            RestoreSlpSnapshot();
            RestoreMeterDefSnapshot();

            return ValidatePairings()
                ? OperationResult<c2s.Chart>.Success(C2s).WithDiagnostics(Diagnostic)
                : OperationResult<c2s.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.Report(ex);
            return OperationResult<c2s.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
    }

    private void ScheduleC2sSlidePaths()
    {
        var slideCount = 0;
        for (var i = 0; i < Notes.Count; i++)
        {
            if (Notes[i] is c2s.Slide)
                slideCount++;
        }

        if (slideCount == 0)
            return;

        var originalIndex = new Dictionary<c2s.Note, int>(Notes.Count);
        for (var i = 0; i < Notes.Count; i++)
            originalIndex[Notes[i]] = i;

        // One bucket per (Round, lane, width), already in list order.
        var pendingByKey =
            new Dictionary<C2sSlidePosition, LinkedList<c2s.Slide>>();
        var pendingBySource =
            new Dictionary<(C2sSlidePosition Key, umgr.Slide Source),
                LinkedListNode<c2s.Slide>>();

        for (var i = 0; i < Notes.Count; i++)
        {
            if (Notes[i] is not c2s.Slide slide)
                continue;

            var key = new C2sSlidePosition(
                slide.Tick.Round,
                slide.Lane,
                slide.Width);

            if (!pendingByKey.TryGetValue(key, out var pending))
            {
                pending = new LinkedList<c2s.Slide>();
                pendingByKey[key] = pending;
            }

            var node = pending.AddLast(slide);
            var source = _slideSegmentSources[slide].SourceSlide;
            pendingBySource[(key, source)] = node;
        }

        var keysByRound = pendingByKey.Keys
            .GroupBy(k => k.Tick)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(k => k.Lane).ThenBy(k => k.Width).ToArray())
            .ToArray();

        var active = new Dictionary<C2sSlidePosition, Queue<umgr.Slide>>();
        var scheduled = new List<c2s.Slide>(slideCount);

        foreach (var keys in keysByRound)
        {
            foreach (var key in keys)
            {
                var pending = pendingByKey[key];

                while (pending.Count > 0)
                {
                    active.TryGetValue(key, out var queue);

                    LinkedListNode<c2s.Slide>? pickNode = null;
                    if (queue is { Count: > 0 } &&
                        pendingBySource.TryGetValue(
                            (key, queue.Peek()),
                            out var continuation) &&
                        continuation.List == pending)
                    {
                        pickNode = continuation;
                    }

                    if (pickNode is null)
                    {
                        for (var node = pending.First;
                             node is not null;
                             node = node.Next)
                        {
                            if (_slideSegmentSources[node.Value].IsRoot)
                            {
                                pickNode = node;
                                break;
                            }
                        }

                        pickNode ??= pending.First;
                    }

                    var pick = pickNode!.Value;
                    var source = _slideSegmentSources[pick];
                    pendingBySource.Remove((key, source.SourceSlide));
                    pending.Remove(pickNode);
                    scheduled.Add(pick);

                    if (queue is { Count: > 0 })
                    {
                        queue.Dequeue();
                        if (queue.Count == 0)
                            active.Remove(key);
                    }

                    var end = new C2sSlidePosition(
                        pick.EndTick.Round,
                        pick.EndLane,
                        pick.EndWidth);

                    if (!active.TryGetValue(end, out var endQueue))
                    {
                        endQueue = new Queue<umgr.Slide>();
                        active[end] = endQueue;
                    }

                    endQueue.Enqueue(source.SourceSlide);
                }
            }
        }

        RebuildNotesWithScheduledSlides(scheduled, originalIndex);
    }

    private void RebuildNotesWithScheduledSlides(
        List<c2s.Slide> scheduled,
        Dictionary<c2s.Note, int> originalIndex)
    {
        // Single-pass group: avoid O(rounds × notes) rescans.
        var slidesByRound = new Dictionary<int, List<c2s.Slide>>();
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

        var othersByRound = new Dictionary<int, List<c2s.Note>>();
        foreach (var note in Notes)
        {
            if (note is c2s.Slide)
                continue;

            var round = note.Tick.Round;
            if (!othersByRound.TryGetValue(round, out var list))
            {
                list = [];
                othersByRound[round] = list;
            }

            list.Add(note);
        }

        foreach (var list in othersByRound.Values)
            list.Sort((a, b) => originalIndex[a].CompareTo(originalIndex[b]));

        var rounds = slidesByRound.Keys
            .Concat(othersByRound.Keys)
            .Distinct()
            .OrderBy(r => r);

        var rebuilt = new List<c2s.Note>(Notes.Count);
        foreach (var round in rounds)
        {
            if (slidesByRound.TryGetValue(round, out var slides))
                rebuilt.AddRange(slides);
            if (othersByRound.TryGetValue(round, out var others))
                rebuilt.AddRange(others);
        }

        Notes.Clear();
        Notes.AddRange(rebuilt);
    }

    private Dictionary<c2s.IPairable, c2s.Note> BuildIntendedAirParents()
    {
        var intended = new Dictionary<c2s.IPairable, c2s.Note>();
        foreach (var (source, root) in _negativePairRoots)
        {
            if (source.PairNote is null)
                continue;

            if (_positivePairRealTargets.TryGetValue(source.PairNote, out var real))
                intended[root] = real;
        }

        return intended;
    }

    private void ScheduleC2sAirParents()
    {
        var intended = BuildIntendedAirParents();
        if (intended.Count == 0)
            return;

        var airsByCell =
            new Dictionary<(int Round, int Lane, int Width), List<c2s.Note>>();
        var noteIndex = new Dictionary<c2s.Note, int>(Notes.Count);

        for (var i = 0; i < Notes.Count; i++)
        {
            var note = Notes[i];
            noteIndex[note] = i;

            if (note is not c2s.IPairable { Parent: c2s.Slide })
                continue;

            var key = (note.Tick.Round, note.Lane, note.Width);
            if (!airsByCell.TryGetValue(key, out var list))
            {
                list = [];
                airsByCell[key] = list;
            }

            list.Add(note);
        }

        if (airsByCell.Count == 0)
            return;

        // Index last segments that can own Air once; Air cells look up by end cell.
        var lastSegmentsByEnd =
            new Dictionary<(int Round, int Lane, int Width), List<c2s.Slide>>();
        foreach (var note in Notes)
        {
            if (note is not c2s.Slide slide)
                continue;
            if (!_slideSegmentSources.TryGetValue(slide, out var src))
                continue;
            if (!_positivePairRealTargets.ContainsKey(src.EndJoint))
                continue;

            var end = (slide.EndTick.Round, slide.EndLane, slide.EndWidth);
            if (!lastSegmentsByEnd.TryGetValue(end, out var list))
            {
                list = [];
                lastSegmentsByEnd[end] = list;
            }

            list.Add(slide);
        }

        foreach (var (cell, cellAirs) in airsByCell)
        {
            lastSegmentsByEnd.TryGetValue(cell, out var lastSegments);
            lastSegments ??= [];

            var intendedParentRank = new Dictionary<c2s.Slide, int>();
            var rank = 0;
            foreach (var air in cellAirs)
            {
                if (intended.TryGetValue((c2s.IPairable)air, out var parent) &&
                    parent is c2s.Slide slide &&
                    intendedParentRank.TryAdd(slide, rank))
                {
                    rank++;
                }
            }

            foreach (var startRoundGroup in lastSegments.GroupBy(s => s.Tick.Round))
            {
                var tied = startRoundGroup.ToList();
                if (tied.Count <= 1)
                    continue;

                var desired = tied
                    .OrderBy(s =>
                        intendedParentRank.TryGetValue(s, out var r)
                            ? r
                            : int.MaxValue)
                    .ThenBy(s => noteIndex[s])
                    .ToList();

                // Same start (lane, width) order belongs to the slide FIFO.
                var overrideWithScheduled = false;
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
                        overrideWithScheduled = true;
                        break;
                    }
                }

                if (overrideWithScheduled)
                    desired = tied.OrderBy(s => noteIndex[s]).ToList();

                ApplyNoteOrder(desired, noteIndex);
            }

            var orderedAirs = cellAirs
                .OrderBy(a =>
                {
                    if (intended.TryGetValue((c2s.IPairable)a, out var parent))
                        return noteIndex.GetValueOrDefault(parent, int.MaxValue);
                    return int.MaxValue;
                })
                .ThenBy(a => noteIndex[a])
                .ToList();

            ApplyNoteOrder(orderedAirs, noteIndex);
        }
    }

    private void ApplyNoteOrder(
        IReadOnlyList<c2s.Note> desiredOrder,
        Dictionary<c2s.Note, int> noteIndex)
    {
        if (desiredOrder.Count <= 1)
            return;

        var slots = new int[desiredOrder.Count];
        for (var i = 0; i < desiredOrder.Count; i++)
            slots[i] = noteIndex[desiredOrder[i]];
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
            return;

        var used = new HashSet<c2s.Note>();
        var warned = new HashSet<(int Tick, int Lane, int Width)>();

        // Index slide attach cells once. Same cell can host start and end parents.
        var candidatesByCell =
            new Dictionary<(int Tick, int Lane, int Width), List<c2s.Note>>();

        void AddCandidate(c2s.Note slide, int tick, int lane, int width)
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
            if (note is not c2s.Slide slide)
                continue;

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

        var pairables = Notes
            .Select((note, index) => (note, index))
            .Where(x => x.note is c2s.IPairable { Parent: c2s.Slide })
            .OrderBy(x => x.note.Tick.Round)
            .ThenBy(x => x.index)
            .Select(x => (c2s.IPairable)x.note);

        foreach (var pairable in pairables)
        {
            var note = (c2s.Note)pairable;
            var cell = (note.Tick.Original, note.Lane, note.Width);
            if (!candidatesByCell.TryGetValue(cell, out var candidates))
                continue;

            var bound = FindSlidePairParent(note, candidates, used);
            if (bound is null)
                continue;

            used.Add(bound);

            if (!intended.TryGetValue(pairable, out var expected) ||
                ReferenceEquals(bound, expected))
                continue;

            if (!warned.Add(cell))
                continue;

            Diagnostic.Report(new TimedDiagnostic(
                Severity.Warning,
                Msg.Key(MsgKeys.Mg_Overlapping_air_parent_slide),
                note.Tick.Original));
        }
    }

    private static c2s.Note? FindSlidePairParent(
        c2s.Note note,
        List<c2s.Note> candidates,
        HashSet<c2s.Note> used)
    {
        c2s.Note? best = null;
        var bestUsed = false;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!IsSlideAttachPoint(candidate, note))
                continue;

            var candidateUsed = used.Contains(candidate);
            var distance = SlidePairDistance(candidate, note);

            if (best is not null)
            {
                if (candidateUsed && !bestUsed)
                    continue;
                if (candidateUsed == bestUsed && distance >= bestDistance)
                    continue;
            }

            best = candidate;
            bestUsed = candidateUsed;
            bestDistance = distance;
        }

        return best;
    }

    private static bool IsSlideAttachPoint(c2s.Note candidate, c2s.Note note)
    {
        if (candidate is c2s.LongNote longNote &&
            longNote.EndTick.Original == note.Tick.Original &&
            longNote.EndLane == note.Lane &&
            longNote.EndWidth == note.Width)
            return true;

        return candidate.Tick.Original == note.Tick.Original &&
               candidate.Lane == note.Lane &&
               candidate.Width == note.Width;
    }

    private static int SlidePairDistance(c2s.Note candidate, c2s.Note note)
    {
        if (candidate is c2s.LongNote longNote)
            return Math.Abs(longNote.EndTick.Original - note.Tick.Original);

        return Math.Abs(candidate.Tick.Original - note.Tick.Original);
    }

    private void ValidateAmbiguousC2sSlidePaths()
    {
        // Replay the endpoint-based FIFO linking that C2S readers use. Times
        // are rounded here because distinct UMIGURI ticks can serialize to the
        // same 1/384 C2S tick and become ambiguous only after conversion.
        // Order matches the writer: Round, then scheduled list index.
        var active = new Dictionary<C2sSlidePosition, Queue<OpenC2sSlidePath>>();

        foreach (var segment in Notes
                     .OfType<c2s.Slide>()
                     .Select((slide, index) => new { Slide = slide, SourceOrder = index })
                     .OrderBy(x => x.Slide.Tick.Round)
                     .ThenBy(x => x.SourceOrder))
        {
            var note = segment.Slide;
            var source = _slideSegmentSources[note];
            var start = new C2sSlidePosition(
                note.Tick.Round,
                note.Lane,
                note.Width);

            OpenC2sSlidePath? open = null;
            if (active.TryGetValue(start, out var queue) && queue.Count > 0)
            {
                open = queue.Dequeue();
                if (queue.Count == 0)
                    active.Remove(start);
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
        foreach (var longNote in Notes.OfType<c2s.LongNote>())
        {
            var length = longNote.Length.Original;
            if (length >= ChartResolution.SingleTick) continue;

            var tick = longNote.Tick.Original;
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Length_smaller_than_unit, length,
                ChartResolution.UmiguriTick / ChartResolution.SingleTick);
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick)
            {
                Target = longNote
            });
        }

        foreach (var sla in Notes.OfType<c2s.Sla>())
        {
            if (sla.Length.Original >= ChartResolution.SingleTick) continue;
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Length_smaller_than_unit, sla.Length.Original,
                ChartResolution.UmiguriTick / ChartResolution.SingleTick);
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, sla.Tick.Original)
            {
                Target = sla
            });
        }
    }

    private void ApplyBgmBarOffset()
    {
        if (!Mgxc.Meta.BgmEnableBarOffset) return;

        var offset = (int)Math.Round((decimal)ChartResolution.UmiguriTick / Mgxc.Meta.BgmInitialDenominator *
                                     Mgxc.Meta.BgmInitialNumerator);
        foreach (var e in Events.Where(e => e.Tick.Original != 0)) e.Tick = e.Tick.Original + offset;
        foreach (var n in Notes)
        {
            n.Tick = n.Tick.Original + offset;
            if (n is c2s.LongNote longNote) longNote.EndTick = longNote.EndTick.Original + offset;
        }
    }

    private bool ValidatePairings()
    {
        var hasError = false;
        foreach (var air in Notes.OfType<c2s.Air>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(
                new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_parent_null), air.Tick.Original)
                {
                    Target = air
                });
            hasError = true;
        }

        foreach (var airSlide in Notes.OfType<c2s.AirSlide>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null),
                airSlide.Tick.Original)
            {
                Target = airSlide
            });
            hasError = true;
        }

        foreach (var airHold in Notes.OfType<c2s.AirHold>().Where(a => a.Parent is null))
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
        umgr.Slide SourceSlide,
        umgr.SlideJoint EndJoint,
        bool IsRoot);

    private sealed record OpenC2sSlidePath(
        umgr.Slide SourceSlide,
        umgr.SlideJoint EndJoint);
}
