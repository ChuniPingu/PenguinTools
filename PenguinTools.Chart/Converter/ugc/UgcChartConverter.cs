using PenguinTools.Chart.Models;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Converter.ugc;

using c2s = Models.c2s;
using umgr = Models.umgr;

public sealed class UgcChartConverter
{
    private readonly c2s.Chart _source;
    private readonly umgr.Chart _target = new();
    private readonly Dictionary<c2s.Note, umgr.PositiveNote> _positiveNotes = [];
    private readonly Dictionary<c2s.Note, Queue<umgr.NegativeNote>> _airActionsByParent = [];

    private readonly bool _debugTil;

    public UgcChartConverter(UgcConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.C2s);
        _source = request.C2s;
        _debugTil = request.DebugTil;
    }

    public OperationResult<umgr.Chart> Convert()
    {
        _target.Meta = _source.Meta;

        if (_target.Meta.C2sSlaSnapshot is null)
        {
            _target.Meta.C2sSlaSnapshot = C2sRoundTripKeys.FormatSlaSnapshot(
                _source.Notes.OfType<c2s.Sla>());
        }

        if (_target.Meta.C2sSlpSnapshot is null &&
            !_source.Events.Any(x => x.Id == "SFL"))
        {
            _target.Meta.C2sSlpSnapshot = C2sRoundTripKeys.FormatSlpSnapshot(
                _source.Events.OfType<c2s.Slp>());
        }

        if (_target.Meta.C2sAirSnapshot is null)
        {
            _target.Meta.C2sAirSnapshot = C2sRoundTripKeys.FormatAirSnapshot(
                _source.Notes.OfType<c2s.Air>());
        }

        if (_target.Meta.C2sMeterDefDenominator is null)
            _target.Meta.C2sMeterDefDenominator =
                _source.Meta.BgmInitialDenominator;

        if (_target.Meta.C2sMeterDefNumerator is null)
            _target.Meta.C2sMeterDefNumerator =
                _source.Meta.BgmInitialNumerator;

        if (_source.Meta.TryGetC2sJudgeSummary(
                out _,
                out _,
                out _,
                out _,
                out _,
                out _))
        {
            if (_source.Meta.C2sJudgeSldProxyBaseline is null)
            {
                _source.Meta.C2sJudgeSldProxyBaseline =
                    C2SJudgeSummaryCalculator.CalculateSlideProxy(
                        _source);
            }

            if (_source.Meta.C2sJudgeHldProxyBaseline is null)
            {
                _source.Meta.C2sJudgeHldProxyBaseline =
                    C2SJudgeSummaryCalculator.CalculateHoldProxy(
                        _source);
            }

            if (_source.Meta.C2sJudgeAirProxyBaseline is null)
            {
                _source.Meta.C2sJudgeAirProxyBaseline =
                    C2SJudgeSummaryCalculator.CalculateAirProxy(
                        _source);
            }
        }

        ConvertEvents();

        var notes = _source.Notes.Where(x => x is not c2s.Sla).ToArray();
        var slides = notes.OfType<c2s.Slide>().ToArray();
        var airCrashes = notes.OfType<c2s.AirCrash>().ToArray();

        foreach (var note in notes.Where(
                     x => x is not c2s.Air
                          and not c2s.AirSlide
                          and not c2s.AirHold
                          and not c2s.Slide
                          and not c2s.AirCrash))
            ConvertNote(note);

        ConvertSlides(slides);
        ConvertAirCrashes(airCrashes);

        var airSlides = notes.OfType<c2s.AirSlide>().ToArray();
        var airHolds = notes.OfType<c2s.AirHold>().ToArray();

        foreach (var note in notes)
        {
            switch (note)
            {
                case c2s.AirSlide airSlide
                    when airSlide.Parent is not c2s.AirSlide:
                    ConvertAirSlideChain(airSlide, airSlides);
                    break;

                case c2s.AirHold airHold
                    when airHold.Parent is not c2s.AirHold:
                    ConvertAirHoldChain(airHold, airHolds);
                    break;
            }
        }
        foreach (var note in notes.OfType<c2s.Air>()) ConvertNote(note);

        ApplySlaTimelines();
        if (_debugTil) EmitDebugTilMarkers();
        _target.Notes.Sort();

        _target.Meta.C2sSlaEditKey ??= C2sRoundTripKeys.FormatSlaEditKey(_target);
        _target.Meta.C2sSlpEditKey ??= C2sRoundTripKeys.FormatSlpEditKey(_target);
        _target.Meta.C2sAirEditKey ??= C2sRoundTripKeys.FormatAirEditKey(_target);

        return OperationResult<umgr.Chart>.Success(_target);
    }

    private void ConvertEvents()
    {
        foreach (var bpm in _source.Events.OfType<c2s.Bpm>())
            _target.Events.AppendChild(new umgr.BpmEvent { Tick = bpm.Tick, Bpm = bpm.Value });

        var meters = _source.Events.OfType<c2s.Met>().OrderBy(x => x.Tick).ToArray();
        var bar = 0;
        var previousTick = 0;
        var previousNumerator = 4;
        var previousDenominator = 4;
        foreach (var meter in meters)
        {
            // Game charts occasionally emit a trailing MET with a zero numerator/denominator
            // (e.g. music2918 Master). Skip them — zero-length bars break UGC bar formatting.
            if (meter.Numerator <= 0 || meter.Denominator <= 0) continue;

            var previousLength = ChartResolution.UmiguriTick * previousNumerator / previousDenominator;
            if (previousLength <= 0) previousLength = ChartResolution.UmiguriTick;
            bar += (meter.Tick.Original - previousTick) / previousLength;
            _target.Events.AppendChild(new umgr.BeatEvent
            {
                Tick = meter.Tick, Bar = bar,
                Numerator = meter.Numerator, Denominator = meter.Denominator
            });
            previousTick = meter.Tick.Original;
            previousNumerator = meter.Numerator;
            previousDenominator = meter.Denominator;
        }

        AddDurationEvents(_source.Events.OfType<c2s.Dcm>(),
            (tick, speed) => new umgr.NoteSpeedEvent { Tick = tick, Speed = speed });

#pragma warning disable CS0612
        var scrolls = _source.Events.OfType<c2s.SpeedEventBase>()
            .Where(x => x is c2s.Slp or c2s.Sfl)
            .GroupBy(x => x is c2s.Slp slp ? Math.Max(0, slp.Timeline) : 0);
#pragma warning restore CS0612
        foreach (var group in scrolls)
            AddDurationEvents(group, (tick, speed) => new umgr.ScrollSpeedEvent
                { Tick = tick, Speed = speed, Timeline = group.Key });
    }

    private void AddDurationEvents<T>(IEnumerable<T> source, Func<int, decimal, umgr.Event> factory)
        where T : c2s.SpeedEventBase
    {
        var events = source.OrderBy(x => x.Tick).ThenBy(x => x.Length).ToArray();
        foreach (var item in events)
        {
            _target.Events.AppendChild(factory(item.Tick.Original, item.Speed));
            var end = item.Tick.Original + item.Length.Original;
            var restored = events.Where(x => !ReferenceEquals(x, item) && x.Tick.Original <= end &&
                                              x.Tick.Original + x.Length.Original > end)
                .OrderByDescending(x => x.Tick).Select(x => x.Speed).FirstOrDefault(1m);
            _target.Events.AppendChild(factory(end, restored));
        }
    }

    private void ConvertNote(c2s.Note source)
    {
        switch (source)
        {
            case c2s.Tap x: AddPositive(x, new umgr.Tap()); break;
            case c2s.Damage x: AddPositive(x, new umgr.Damage()); break;
            case c2s.Flick x: AddPositive(x, new umgr.Flick()); break;
            case c2s.ExTap x:
                AddPositive(x, new umgr.ExTap
                {
                    Effect = x.Effect ?? ExEffect.UP,
                    Role = umgr.ExTapRole.Explicit
                });
                break;
            case c2s.Hold x: ConvertHold(x); break;
            case c2s.Air x: ConvertAir(x); break;
        }
    }

    private void AddPositive(c2s.Note source, umgr.PositiveNote target)
    {
        Copy(source, target);
        _target.Notes.AppendChild(target);
        _positiveNotes[source] = target;
    }

    private void ConvertHold(c2s.Hold source)
    {
        var hold = new umgr.Hold { Effect = source.Effect };
        Copy(source, hold);
        _target.Notes.AppendChild(hold);
        var tail = new umgr.HoldJoint { Tick = source.EndTick, Timeline = Timeline(source) };
        hold.AppendChild(tail);
        _positiveNotes[source] = tail;
    }

    private void ConvertSlides(IEnumerable<c2s.Slide> source)
    {
        var active = new Dictionary<SlidePathKey, Queue<OpenSlide>>();

        foreach (var entry in source
                     .Select((segment, index) => (Segment: segment, SourceOrder: index))
                     .OrderBy(x => x.Segment.Tick.Original)
                     .ThenBy(x => x.SourceOrder))
        {
            var segment = entry.Segment;
            var startKey = new SlidePathKey(
                segment.Tick.Original,
                segment.Lane,
                segment.Width);

            OpenSlide open;

            if (active.TryGetValue(startKey, out var startQueue) && startQueue.Count > 0)
            {
                open = startQueue.Dequeue();
                if (startQueue.Count == 0)
                    active.Remove(startKey);

                if (open.Slide.Effect is null && segment.Effect is { } effect)
                    open.Slide.Effect = effect;

                open.LastJoint.Joint = IntermediateJoint(open.LastSegment);
                open.LastJoint.NoLine = segment.NoLine;

                var joint = CreateSlideJoint(segment);
                open.Slide.AppendChild(joint);
                _positiveNotes[segment] = joint;

                open = new OpenSlide(open.Slide, joint, segment);
            }
            else
            {
                var slide = new umgr.Slide
                {
                    Effect = segment.Effect,
                    NoLine = segment.NoLine
                };

                Copy(segment, slide);
                _target.Notes.AppendChild(slide);

                var firstJoint = CreateSlideJoint(segment);
                slide.AppendChild(firstJoint);
                _positiveNotes[segment] = firstJoint;

                open = new OpenSlide(slide, firstJoint, segment);
            }

            var endKey = new SlidePathKey(
                segment.EndTick.Original,
                segment.EndLane,
                segment.EndWidth);

            if (!active.TryGetValue(endKey, out var endQueue))
            {
                endQueue = new Queue<OpenSlide>();
                active[endKey] = endQueue;
            }

            endQueue.Enqueue(open);
        }
    }

    private static umgr.SlideJoint CreateSlideJoint(c2s.Slide source) => new()
    {
        Tick = source.EndTick,
        Lane = source.EndLane,
        Width = source.EndWidth,
        Timeline = Timeline(source),
        Joint = source.Joint
    };

    private bool TryTakeAirAction(
        c2s.Air source,
        out umgr.NegativeNote action)
    {
        action = null!;

        if (source.Parent is null)
            return false;

        if (_airActionsByParent.TryGetValue(source.Parent, out var actions) &&
            actions.TryDequeue(out var candidate))
        {
            action = candidate!;
            return true;
        }

        return false;
    }

    private static Joint IntermediateJoint(c2s.Slide segment) =>
        segment.Joint;

    private void ConvertAir(c2s.Air source)
    {
        if (source.Parent is null)
            return;

        if (TryTakeAirAction(source, out var action))
        {
            switch (action)
            {
                case umgr.AirHold hold:
                    hold.Direction = source.Direction;
                    hold.Color = source.Color;
                    break;

                case umgr.AirSlide slide:
                    slide.Direction = source.Direction;
                    slide.Color = source.Color;
                    break;
            }

            return;
        }

        if (!_positiveNotes.TryGetValue(source.Parent, out var parent))
            return;

        // AirHold/AirSlide convert before Air and already MakePair the parent.
        switch (parent.PairNote)
        {
            case umgr.AirHold hold:
                hold.Direction = source.Direction;
                hold.Color = source.Color;
                return;

            case umgr.AirSlide slide:
                slide.Direction = source.Direction;
                slide.Color = source.Color;
                return;
        }

        var air = new umgr.Air
        {
            Direction = source.Direction,
            Color = source.Color
        };

        Copy(source, air);
        _target.Notes.AppendChild(air);
        PairAirAction(source.Parent, air);
    }

    private void PairAirAction(
        c2s.Note sourceParent,
        umgr.NegativeNote action)
    {
        if (!_positiveNotes.TryGetValue(sourceParent, out var parent))
            return;

        // UMGR only allows one NegativeNote to pair directly with a
        // PositiveNote. Keep the normal one-to-one case unchanged.
        if (parent.PairNote is null)
        {
            parent.MakePair(action);
            return;
        }

        // C2S may attach more than one AIR action to the same positive
        // parent. Pairing another action directly would detach the first one,
        // so additional actions use an internal carrier instead.
        var carrierParent = sourceParent switch
        {
            c2s.Tap =>
                umgr.AirActionCarrierParent.Tap,

            c2s.ExTap =>
                umgr.AirActionCarrierParent.ExTap,

            c2s.Flick =>
                umgr.AirActionCarrierParent.Flick,

            c2s.Damage =>
                umgr.AirActionCarrierParent.Damage,

            c2s.Hold =>
                umgr.AirActionCarrierParent.Hold,

            c2s.Slide =>
                umgr.AirActionCarrierParent.Slide,

            _ =>
                umgr.AirActionCarrierParent.None
        };

        if (carrierParent == umgr.AirActionCarrierParent.None)
        {
            parent.MakePair(action);
            return;
        }

        var carrierEffect = sourceParent switch
        {
            c2s.ExTap exTap =>
                exTap.Effect ?? ExEffect.UP,

            c2s.Hold hold =>
                hold.Effect ?? ExEffect.UP,

            c2s.Slide slide =>
                slide.Effect ?? ExEffect.UP,

            _ =>
                ExEffect.UP
        };

        var carrier = new umgr.ExTap
        {
            Tick = parent.Tick,
            Lane = parent.Lane,
            Width = parent.Width,
            Timeline = parent.Timeline,
            Effect = carrierEffect,
            Role = umgr.ExTapRole.AirActionCarrier,
            AirActionParent = carrierParent,

            AirActionParentJoint =
                sourceParent is c2s.Slide slideParent
                    ? slideParent.Joint
                    : Joint.D,

            AirActionParentIsEx =
                sourceParent switch
                {
                    c2s.Hold holdParent =>
                        holdParent.Effect is not null,

                    c2s.Slide slideEffectParent =>
                        slideEffectParent.Effect is not null,

                    _ =>
                        false
                }
        };

        _target.Notes.AppendChild(carrier);
        carrier.MakePair(action);
    }

    private void RegisterAirAction(
        c2s.Note parent,
        umgr.NegativeNote action)
    {
        if (!_airActionsByParent.TryGetValue(parent, out var actions))
        {
            actions = [];
            _airActionsByParent[parent] = actions;
        }

        actions.Enqueue(action);
    }

    private void ConvertAirSlideChain(
        c2s.AirSlide source,
        IReadOnlyList<c2s.AirSlide> allSegments)
    {
        var air = new umgr.AirSlide
        {
            Height = source.Height.Original,
            Color = source.Color
        };

        Copy(source, air);

        var segment = source;

        while (true)
        {
            var next = allSegments.FirstOrDefault(
                x => ReferenceEquals(x.Parent, segment));

            air.AppendChild(new umgr.AirSlideJoint
            {
                Tick = segment.EndTick,
                Lane = segment.EndLane,
                Width = segment.EndWidth,
                Timeline = Timeline(segment),
                Height = segment.EndHeight.Original,
                Joint = segment.Joint
            });

            if (next is null)
                break;

            segment = next;
        }

        _target.Notes.AppendChild(air);

        EnsureAirActionPaired(source, source.Parent, air);
    }

    private void ConvertAirHoldChain(
        c2s.AirHold source,
        IReadOnlyList<c2s.AirHold> allSegments)
    {
        var air = new umgr.AirHold
        {
            Color = source.Color
        };
        Copy(source, air);

        var segment = source;

        while (true)
        {
            var next = allSegments.FirstOrDefault(
                x => ReferenceEquals(x.Parent, segment));

            air.AppendChild(new umgr.AirHoldJoint
            {
                Tick = segment.EndTick,
                Timeline = Timeline(segment),
                Joint = segment.Joint
            });

            if (next is null)
                break;

            segment = next;
        }

        _target.Notes.AppendChild(air);

        EnsureAirActionPaired(source, source.Parent, air);
    }

    private c2s.Note? ResolveAirActionPairParent(c2s.Note? parent)
    {
        if (parent is not c2s.Slide slide)
            return parent;

        if (!_positiveNotes.TryGetValue(slide, out var mappedParent) ||
            mappedParent is not umgr.SlideJoint mappedJoint ||
            mappedJoint.Parent is not umgr.Slide mappedSlide ||
            ReferenceEquals(mappedSlide.LastChild, mappedJoint))
        {
            return parent;
        }

        var terminalMatches = _source.Notes
            .OfType<c2s.Slide>()
            .Where(candidate =>
                !ReferenceEquals(candidate, slide) &&
                candidate.Id == slide.Id &&
                candidate.Tick.Original == slide.Tick.Original &&
                candidate.Timeline == slide.Timeline &&
                candidate.Lane == slide.Lane &&
                candidate.Width == slide.Width &&
                candidate.EndTick.Original == slide.EndTick.Original &&
                candidate.EndLane == slide.EndLane &&
                candidate.EndWidth == slide.EndWidth &&
                candidate.Joint == slide.Joint &&
                candidate.NoLine == slide.NoLine &&
                candidate.Effect == slide.Effect)
            .Where(candidate =>
                _positiveNotes.TryGetValue(candidate, out var mappedCandidate) &&
                mappedCandidate is umgr.SlideJoint candidateJoint &&
                candidateJoint.Parent is umgr.Slide candidateSlide &&
                ReferenceEquals(candidateSlide.LastChild, candidateJoint))
            .ToArray();

        return terminalMatches.Length == 1
            ? terminalMatches[0]
            : parent;
    }

    private void EnsureAirActionPaired(
        c2s.Note source,
        c2s.Note? parent,
        umgr.NegativeNote action)
    {
        var pairParent = ResolveAirActionPairParent(parent);

        var canUseMappedParent =
            pairParent is not null &&
            _positiveNotes.TryGetValue(pairParent, out var positiveParent) &&
            (positiveParent is not umgr.SlideJoint slideJoint ||
             slideJoint.Parent is umgr.Slide mappedSlide &&
             ReferenceEquals(mappedSlide.LastChild, slideJoint));

        if (pairParent is not null && canUseMappedParent)
        {
            PairAirAction(pairParent, action);

            if (action.PairNote is not null)
            {
                if (parent is not null)
                    RegisterAirAction(parent, action);

                return;
            }
        }

        // A Slide action attached to an intermediate joint cannot be written
        // directly after that joint because MGXC long-note pairing is
        // sequential. Keep a dedicated carrier for that case, as well as for
        // unresolved parents.
        var carrier = new umgr.ExTap
        {
            Tick = source.Tick,
            Lane = source.Lane,
            Width = source.Width,
            Timeline = Timeline(source),
            Effect = parent switch
            {
                c2s.ExTap exTap => exTap.Effect ?? ExEffect.UP,
                c2s.Hold hold => hold.Effect ?? ExEffect.UP,
                c2s.Slide slide => slide.Effect ?? ExEffect.UP,
                _ => ExEffect.UP
            },
            Role = umgr.ExTapRole.AirActionCarrier,
            AirActionParent = parent switch
            {
                c2s.Tap => umgr.AirActionCarrierParent.Tap,
                c2s.ExTap => umgr.AirActionCarrierParent.ExTap,
                c2s.Flick => umgr.AirActionCarrierParent.Flick,
                c2s.Damage => umgr.AirActionCarrierParent.Damage,
                c2s.Hold => umgr.AirActionCarrierParent.Hold,
                c2s.Slide => umgr.AirActionCarrierParent.Slide,
                _ => umgr.AirActionCarrierParent.Tap
            },
            AirActionParentJoint = parent is c2s.Slide slideParent
                ? slideParent.Joint
                : Joint.D,
            AirActionParentIsEx = parent switch
            {
                c2s.Hold holdParent => holdParent.Effect is not null,
                c2s.Slide slideEffectParent => slideEffectParent.Effect is not null,
                _ => false
            }
        };

        _target.Notes.AppendChild(carrier);
        carrier.MakePair(action);

        if (parent is null)
            return;

        RegisterAirAction(parent, action);
    }

    private readonly record struct OpenSlide(
        umgr.Slide Slide,
        umgr.SlideJoint LastJoint,
        c2s.Slide LastSegment);

    private readonly record struct SlidePathKey(
        int Tick,
        int Lane,
        int Width);

    private readonly record struct AirCrashPathKey(
        int Tick,
        int Lane,
        int Width,
        decimal Height,
        Color Color,
        int Density,
        AirLadderAttr Attr);

    private static AirLadderAttr AirCrashPathAttr(AirLadderAttr attr) =>
        attr == AirLadderAttr.Trace ? AirLadderAttr.DEF : attr;

    private static AirCrashPathKey AirCrashStartKey(c2s.AirCrash note) => new(
        note.Tick.Original,
        note.Lane,
        note.Width,
        note.Height.Original,
        note.Color,
        note.Density.Original,
        AirCrashPathAttr(note.Attr));

    private static AirCrashPathKey AirCrashEndKey(c2s.AirCrash note) => new(
        note.EndTick.Original,
        note.EndLane,
        note.EndWidth,
        note.EndHeight.Original,
        note.Color,
        note.Density.Original,
        AirCrashPathAttr(note.Attr));

    private void ConvertAirCrashes(IEnumerable<c2s.AirCrash> source)
    {
        var active = new Dictionary<AirCrashPathKey, Queue<umgr.AirCrash>>();

        foreach (var entry in source
                     .Select((segment, index) => (Segment: segment, SourceOrder: index))
                     .OrderBy(x => x.Segment.Tick.Original)
                     .ThenBy(x => x.SourceOrder))
        {
            var segment = entry.Segment;
            var startKey = AirCrashStartKey(segment);
            umgr.AirCrash crash;

            if (active.TryGetValue(startKey, out var startQueue) && startQueue.Count > 0)
            {
                crash = startQueue.Dequeue();
                if (startQueue.Count == 0)
                    active.Remove(startKey);
            }
            else
            {
                crash = new umgr.AirCrash
                {
                    Height = segment.Height.Original,
                    Color = segment.Color,
                    Density = segment.Density,
                    Attr = segment.Attr
                };
                Copy(segment, crash);
                _target.Notes.AppendChild(crash);
            }

            crash.AppendChild(new umgr.AirCrashJoint
            {
                Tick = segment.EndTick,
                Lane = segment.EndLane,
                Width = segment.EndWidth,
                Timeline = Timeline(segment),
                Height = segment.EndHeight.Original
            });

            var endKey = AirCrashEndKey(segment);
            if (!active.TryGetValue(endKey, out var endQueue))
            {
                endQueue = new Queue<umgr.AirCrash>();
                active[endKey] = endQueue;
            }

            endQueue.Enqueue(crash);
        }
    }

    private static void Copy(c2s.Note source, umgr.Note target)
    {
        target.Tick = source.Tick;
        target.Lane = source.Lane;
        target.Width = source.Width;
        target.Timeline = Timeline(source);
    }

    private static int Timeline(c2s.Note note) => Math.Max(0, note.Timeline);

    private void ApplySlaTimelines()
    {
        var regions = _source.Notes.OfType<c2s.Sla>().ToArray();
        foreach (var note in Flatten(_target.Notes.Children))
        {
            var timeline = regions.Where(x => Contains(x, note)).Select(x => x.Timeline).DefaultIfEmpty(note.Timeline).Max();
            note.Timeline = Math.Max(0, timeline);
        }
    }

    private static IEnumerable<umgr.Note> Flatten(IEnumerable<umgr.Note> notes)
    {
        foreach (var note in notes)
        {
            yield return note;
            foreach (var child in Flatten(note.Children)) yield return child;
        }
    }

    private static bool Contains(c2s.Sla sla, umgr.Note note)
    {
        var end = sla.Tick.Original + sla.Length.Original;
        return note.Tick.Original >= sla.Tick.Original && note.Tick.Original < end &&
               note.Lane >= sla.Lane && note.Lane + note.Width <= sla.Lane + sla.Width;
    }

    private void EmitDebugTilMarkers()
    {
        foreach (var sla in _source.Notes.OfType<c2s.Sla>())
        {
            var crash = new umgr.AirCrash
            {
                Tick = sla.Tick,
                Lane = sla.Lane,
                Width = sla.Width,
                Timeline = 0,
                Height = 0,
                Color = Color.NON,
                Density = 0
            };
            crash.AppendChild(new umgr.AirCrashJoint
            {
                Tick = sla.Tick.Original + sla.Length.Original,
                Lane = sla.Lane,
                Width = sla.Width,
                Timeline = 0,
                Height = 0
            });
            _target.Notes.AppendChild(crash);
        }
    }
}
