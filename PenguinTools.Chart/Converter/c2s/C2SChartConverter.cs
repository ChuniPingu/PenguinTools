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
        var originalIndex = Notes
            .Select((note, index) => (note, index))
            .ToDictionary(x => x.note, x => x.index);

        var slides = Notes.OfType<c2s.Slide>().ToList();
        if (slides.Count == 0)
            return;

        var pendingByKey = slides
            .GroupBy(s => new C2sSlidePosition(s.Tick.Round, s.Lane, s.Width))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => originalIndex[s]).ToList());

        var active = new Dictionary<C2sSlidePosition, Queue<umgr.Slide>>();
        var scheduled = new List<c2s.Slide>(slides.Count);

        foreach (var round in pendingByKey.Keys.Select(k => k.Tick).Distinct().OrderBy(t => t))
        {
            var keys = pendingByKey.Keys
                .Where(k => k.Tick == round)
                .OrderBy(k => k.Lane)
                .ThenBy(k => k.Width);

            foreach (var key in keys)
            {
                var pending = pendingByKey[key];

                while (pending.Count > 0)
                {
                    active.TryGetValue(key, out var queue);
                    queue ??= new Queue<umgr.Slide>();

                    c2s.Slide? pick = null;
                    if (queue.Count > 0)
                    {
                        var expected = queue.Peek();
                        pick = pending.FirstOrDefault(s =>
                            ReferenceEquals(_slideSegmentSources[s].SourceSlide, expected));
                    }

                    if (pick is null)
                    {
                        pick = pending.FirstOrDefault(s => _slideSegmentSources[s].IsRoot)
                               ?? pending[0];
                    }

                    pending.Remove(pick);
                    scheduled.Add(pick);

                    var source = _slideSegmentSources[pick];

                    if (queue.Count > 0)
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
        var others = Notes.Where(n => n is not c2s.Slide).ToList();
        var rounds = Notes
            .Select(n => n.Tick.Round)
            .Distinct()
            .OrderBy(r => r);

        var rebuilt = new List<c2s.Note>(Notes.Count);
        foreach (var round in rounds)
        {
            rebuilt.AddRange(scheduled.Where(s => s.Tick.Round == round));
            rebuilt.AddRange(
                others
                    .Where(n => n.Tick.Round == round)
                    .OrderBy(n => originalIndex[n]));
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

        var airs = Notes
            .OfType<c2s.IPairable>()
            .Where(p => p.Parent is c2s.Slide)
            .Cast<c2s.Note>()
            .ToList();

        if (airs.Count == 0)
            return;

        foreach (var group in airs.GroupBy(a => (Round: a.Tick.Round, a.Lane, a.Width)))
        {
            var cellAirs = group.ToList();
            var cell = group.Key;

            var lastSegments = Notes
                .OfType<c2s.Slide>()
                .Where(s =>
                    s.EndTick.Round == cell.Round &&
                    s.EndLane == cell.Lane &&
                    s.EndWidth == cell.Width &&
                    _slideSegmentSources.TryGetValue(s, out var src) &&
                    _positivePairRealTargets.ContainsKey(src.EndJoint))
                .ToList();

            var intendedParents = cellAirs
                .Select(a => intended.GetValueOrDefault((c2s.IPairable)a))
                .OfType<c2s.Slide>()
                .Distinct()
                .ToList();

            foreach (var startRoundGroup in lastSegments.GroupBy(s => s.Tick.Round))
            {
                var tied = startRoundGroup.ToList();
                if (tied.Count <= 1)
                    continue;

                var positions = Notes
                    .Select((note, index) => (note, index))
                    .ToDictionary(x => x.note, x => x.index);

                var desired = tied
                    .OrderBy(s =>
                    {
                        var idx = intendedParents.IndexOf(s);
                        return idx < 0 ? int.MaxValue : idx;
                    })
                    .ThenBy(s => positions[s])
                    .ToList();

                // Same start (lane, width) order belongs to the slide FIFO.
                foreach (var startKey in tied.GroupBy(s => (s.Lane, s.Width)))
                {
                    var scheduledOrder = startKey.OrderBy(s => positions[s]).ToList();
                    var desiredOrder = desired
                        .Where(s => s.Lane == startKey.Key.Lane && s.Width == startKey.Key.Width)
                        .ToList();

                    if (!scheduledOrder.SequenceEqual(desiredOrder))
                    {
                        desired = tied.OrderBy(s => positions[s]).ToList();
                        break;
                    }
                }

                ApplyNoteOrder(desired);
            }

            var noteIndex = Notes
                .Select((note, index) => (note, index))
                .ToDictionary(x => x.note, x => x.index);

            var orderedAirs = cellAirs
                .OrderBy(a =>
                {
                    if (intended.TryGetValue((c2s.IPairable)a, out var parent))
                        return noteIndex.GetValueOrDefault(parent, int.MaxValue);
                    return int.MaxValue;
                })
                .ThenBy(a => noteIndex[a])
                .ToList();

            ApplyNoteOrder(orderedAirs);
        }
    }

    private void ApplyNoteOrder(IReadOnlyList<c2s.Note> desiredOrder)
    {
        if (desiredOrder.Count <= 1)
            return;

        var slots = desiredOrder
            .Select(n => Notes.IndexOf(n))
            .OrderBy(i => i)
            .ToArray();

        for (var i = 0; i < slots.Length; i++)
            Notes[slots[i]] = desiredOrder[i];
    }

    private void ValidateOverlappingAirParents()
    {
        var intended = BuildIntendedAirParents();
        var used = new HashSet<c2s.Note>();
        var warned = new HashSet<(int Tick, int Lane, int Width)>();

        var pairables = Notes
            .Select((note, index) => (note, index))
            .Where(x => x.note is c2s.IPairable { Parent: c2s.Slide })
            .OrderBy(x => x.note.Tick.Round)
            .ThenBy(x => x.index)
            .Select(x => (c2s.IPairable)x.note);

        foreach (var pairable in pairables)
        {
            var note = (c2s.Note)pairable;
            var bound = FindSlidePairParent(note, used);
            if (bound is null)
                continue;

            used.Add(bound);

            if (!intended.TryGetValue(pairable, out var expected) ||
                ReferenceEquals(bound, expected))
                continue;

            var cell = (note.Tick.Original, note.Lane, note.Width);
            if (!warned.Add(cell))
                continue;

            Diagnostic.Report(new TimedDiagnostic(
                Severity.Warning,
                Msg.Key(MsgKeys.Mg_Overlapping_air_parent_slide),
                note.Tick.Original));
        }
    }

    private c2s.Note? FindSlidePairParent(c2s.Note note, HashSet<c2s.Note> used)
    {
        return Notes
            .Where(candidate => candidate is c2s.Slide)
            .Where(candidate => IsSlideAttachPoint(candidate, note))
            .OrderBy(candidate => used.Contains(candidate))
            .ThenBy(candidate => SlidePairDistance(candidate, note))
            .FirstOrDefault();
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
