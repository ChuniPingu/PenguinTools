using PenguinTools.Chart.Models;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;

using C2sModel = PenguinTools.Chart.Models.c2s;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Converter.ugc;

public sealed class UgcChartConverter
{
    private readonly C2sModel.Chart _source;
    private readonly UmgrModel.Chart _target = new();
    private readonly Dictionary<C2sModel.Note, UmgrModel.PositiveNote> _positiveNotes = [];
    private readonly Dictionary<C2sModel.Note, Queue<UmgrModel.NegativeNote>> _airActionsByParent = [];
    private readonly HashSet<C2sModel.Note> _usedAirSegments = [];

    private int ParentSourceOrder(C2sModel.Note? parent)
    {
        if (parent is null)
        {
            return int.MaxValue;
        }

        if (!_positiveNotes.TryGetValue((C2sModel.Note)parent, out var mapped))
        {
            return int.MaxValue;
        }

        var root = mapped.Parent is UmgrModel.Slide or UmgrModel.Hold ? mapped.Parent : mapped;
        return _source.Notes.Select((n, i) => (n, i))
            .Where(x => _positiveNotes.TryGetValue(x.n, out var p) &&
                ReferenceEquals(p.Parent is UmgrModel.Slide or UmgrModel.Hold ? p.Parent : p, root))
            .Select(x => x.i).DefaultIfEmpty(int.MaxValue).Min();
    }

    private void RebindGroundParents(C2sModel.Note[] notes)
    {
        var cursors = new Dictionary<(int, int, int, Type), int>();
        foreach (var note in notes)
        {
            if (note is not C2sModel.IPairable pair || pair.Parent is not { } old ||
                old is C2sModel.AirSlide or C2sModel.AirHold)
            {
                continue;
            }

            var candidates = _positiveNotes.Where(p => p.Key.GetType() == old.GetType() &&
                p.Value.Tick == note.Tick && p.Value.Lane == note.Lane && p.Value.Width == note.Width &&
                (p.Value.Parent is not UmgrModel.Slide slide || ReferenceEquals(slide.LastChild, p.Value)))
                .OrderBy(p => ParentSourceOrder(p.Key)).Select(p => p.Key).ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            var key = (note.Tick.Original, note.Lane, note.Width, old.GetType());
            var cursor = cursors.GetValueOrDefault(key);
            pair.Parent = candidates[Math.Min(cursor, candidates.Length - 1)];
            cursors[key] = cursor + 1;
        }
    }

    private readonly bool _debugTil;

    public UgcChartConverter(UgcConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.C2s);
        _source = request.C2s;
        _debugTil = request.DebugTil;
    }

    public OperationResult<UmgrModel.Chart> Convert()
    {
        _target.Meta = _source.Meta;
        _target.Extras = _source.Extras;

        CaptureSourceSnapshots();

        ConvertEvents();
        _target.Extras.Meters = _source.Events.OfType<C2sModel.Met>()
            .Select(m => new MeterSnapshot(m.Tick.Original, m.Numerator, m.Denominator)).ToList();
        _target.Extras.MeterEditKey = ChartExtras.BeatKey(_target);
        _target.Extras.HasSpeedSnapshot = true;
        _target.Extras.Speeds = _source.Events.OfType<C2sModel.SpeedEventBase>()
            .Select(e => new SpeedSnapshot(e.Id, e.Tick.Original, e.Length.Original,
                e.Speed, e is C2sModel.Slp slp ? slp.Timeline : 0)).ToList();

        var notes = _source.Notes.Where(x => x is not C2sModel.Sla).ToArray();
        var slides = notes.OfType<C2sModel.Slide>().ToArray();
        var airCrashes = notes.OfType<C2sModel.AirCrash>().ToArray();

        foreach (var note in notes.Where(
                     x => x is not C2sModel.Air
                          and not C2sModel.AirSlide
                          and not C2sModel.AirHold
                          and not C2sModel.Slide
                          and not C2sModel.AirCrash))
        {
            ConvertNote(note);
        }

        ConvertSlides(slides);
        ConvertAirCrashes(airCrashes);

        RebindGroundParents(notes);
        var airSlides = notes.OfType<C2sModel.AirSlide>().OrderBy(n => n.Tick.Original).ThenBy(n => n.Lane).ThenBy(n => n.Width).ToArray();
        var airHolds = notes.OfType<C2sModel.AirHold>().ToArray();

        foreach (var note in notes.OrderBy(n => n is C2sModel.IPairable p && p.Parent is not C2sModel.AirSlide and not C2sModel.AirHold ? ParentSourceOrder(p.Parent) : int.MaxValue))
        {
            switch (note)
            {
                case C2sModel.AirSlide airSlide
                    when airSlide.Parent is not C2sModel.AirSlide:
                    ConvertAirSlideChain(airSlide, airSlides);
                    break;

                case C2sModel.AirHold airHold
                    when airHold.Parent is not C2sModel.AirHold:
                    ConvertAirHoldChain(airHold, airHolds);
                    break;
            }
        }
        foreach (var note in notes.OfType<C2sModel.Air>())
        {
            ConvertNote(note);
        }

        ApplySlaTimelines();
        if (_debugTil)
        {
            EmitDebugTilMarkers();
        }

        _target.Notes.Sort();

        _target.Meta.C2sSlaEditKey ??= C2sRoundTripKeys.FormatSlaEditKey(_target);
        _target.Meta.C2sSlpEditKey ??= C2sRoundTripKeys.FormatSlpEditKey(_target);
        _target.Meta.C2sAirEditKey ??= C2sRoundTripKeys.FormatAirEditKey(_target);
        _target.Extras.SpeedModelKey = ChartExtras.SpeedKey(_target);
        if (_source.Extras.InteropSourceText is { } sourceText)
        {
            _target.Extras.SourceSnapshot = C2SRoundTrip.Decode(C2SRoundTrip.Encode(sourceText, ChartExtras.EventView(_target)));
        }

        return OperationResult<UmgrModel.Chart>.Success(_target);
    }

    private void CaptureSourceSnapshots()
    {
        if (_target.Meta.C2sSlaSnapshot is null)
        {
            _target.Meta.C2sSlaSnapshot = C2sRoundTripKeys.FormatSlaSnapshot(
                _source.Notes.OfType<C2sModel.Sla>());
        }

        if (_target.Meta.C2sSlpSnapshot is null &&
            !_source.Events.Any(x => x.Id == "SFL"))
        {
            _target.Meta.C2sSlpSnapshot = C2sRoundTripKeys.FormatSlpSnapshot(
                _source.Events.OfType<C2sModel.Slp>());
        }

        if (_target.Meta.C2sAirSnapshot is null)
        {
            _target.Meta.C2sAirSnapshot = C2sRoundTripKeys.FormatAirSnapshot(
                _source.Notes.OfType<C2sModel.Air>());
        }

        if (_target.Meta.C2sMeterDefDenominator is null)
        {
            _target.Meta.C2sMeterDefDenominator =
                _source.Meta.BgmInitialDenominator;
        }

        if (_target.Meta.C2sMeterDefNumerator is null)
        {
            _target.Meta.C2sMeterDefNumerator =
                _source.Meta.BgmInitialNumerator;
        }

        CaptureJudgeBaselines();
    }

    private void CaptureJudgeBaselines()
    {
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

    }

    private void ConvertEvents()
    {
        foreach (var bpm in _source.Events.OfType<C2sModel.Bpm>())
        {
            _target.Events.AppendChild(new UmgrModel.BpmEvent { Tick = bpm.Tick, Bpm = bpm.Value });
        }

        var meters = _source.Events.OfType<C2sModel.Met>().OrderBy(x => x.Tick).ToArray();
        var bar = 0;
        var previousTick = 0;
        var previousNumerator = 4;
        var previousDenominator = 4;
        foreach (var meter in meters)
        {
            // A zero numerator is a real, zero-duration visual bar.
            if (meter.Numerator < 0 || meter.Denominator <= 0)
            {
                continue;
            }

            var previousLength = ChartResolution.UmiguriTick * previousNumerator / previousDenominator;
            if (previousLength == 0)
            {
                bar++;
            }
            else
            {
                bar += (meter.Tick.Original - previousTick) / previousLength;
            }

            _target.Events.AppendChild(new UmgrModel.BeatEvent
            {
                Tick = meter.Tick,
                Bar = bar,
                Numerator = meter.Numerator,
                Denominator = meter.Denominator
            });
            previousTick = meter.Tick.Original;
            previousNumerator = meter.Numerator;
            previousDenominator = meter.Denominator;
        }

        AddDurationEvents(_source.Events.OfType<C2sModel.Dcm>(),
            (tick, speed) => new UmgrModel.NoteSpeedEvent { Tick = tick, Speed = speed });

#pragma warning disable CS0612
        var scrolls = _source.Events.OfType<C2sModel.SpeedEventBase>()
            .Where(x => x is C2sModel.Slp or C2sModel.Sfl or C2sModel.Stop)
            .GroupBy(x => x is C2sModel.Slp slp ? Math.Max(0, slp.Timeline) : 0);
#pragma warning restore CS0612
        foreach (var group in scrolls)
        {
            AddDurationEvents(group, (tick, speed) => new UmgrModel.ScrollSpeedEvent
            { Tick = tick, Speed = speed, Timeline = group.Key });
        }
    }

    private void AddDurationEvents<T>(IEnumerable<T> source, Func<int, decimal, UmgrModel.Event> factory)
        where T : C2sModel.SpeedEventBase
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

    private void ConvertNote(C2sModel.Note source)
    {
        switch (source)
        {
            case C2sModel.Tap x:
                AddPositive(x, new UmgrModel.Tap());
                break;
            case C2sModel.Damage x:
                AddPositive(x, new UmgrModel.Damage());
                break;
            case C2sModel.Flick x:
                AddPositive(x, new UmgrModel.Flick());
                break;
            case C2sModel.ExTap x:
                AddPositive(x, new UmgrModel.ExTap
                {
                    Effect = x.Effect ?? ExEffect.UP,
                    Role = UmgrModel.ExTapRole.Explicit
                });
                break;
            case C2sModel.Hold x:
                ConvertHold(x);
                break;
            case C2sModel.Air x:
                ConvertAir(x);
                break;
        }
    }

    private void AddPositive(C2sModel.Note source, UmgrModel.PositiveNote target)
    {
        Copy(source, target);
        _target.Notes.AppendChild(target);
        _positiveNotes[source] = target;
    }

    private void ConvertHold(C2sModel.Hold source)
    {
        var hold = new UmgrModel.Hold { Effect = source.Effect };
        Copy(source, hold);
        _target.Notes.AppendChild(hold);
        var tail = new UmgrModel.HoldJoint { Tick = source.EndTick, Timeline = Timeline(source) };
        hold.AppendChild(tail);
        _positiveNotes[source] = tail;
    }

    private void ConvertSlides(IEnumerable<C2sModel.Slide> source)
    {
        var segments = source.OrderBy(n => n.Tick.Original).ThenBy(n => n.Lane).ThenBy(n => n.Width).ToArray();
        var used = new HashSet<C2sModel.Slide>();
        foreach (var root in segments)
        {
            if (!used.Add(root))
            {
                continue;
            }

            var slide = new UmgrModel.Slide { Effect = root.Effect, NoLine = root.NoLine };
            Copy(root, slide);
            _target.Notes.AppendChild(slide);
            var current = root;
            while (true)
            {
                var joint = CreateSlideJoint(current);
                slide.AppendChild(joint);
                _positiveNotes[current] = joint;
                var next = segments.FirstOrDefault(n => !used.Contains(n) &&
                    n.Tick.Original == current.EndTick.Original && n.Lane == current.EndLane && n.Width == current.EndWidth);
                if (next is null)
                {
                    break;
                }

                used.Add(next);
                joint.HasEffectOverride = true;
                joint.SegmentEffect = next.Effect;
                joint.NoLine = next.NoLine;
                current = next;
            }
        }
    }

    private static UmgrModel.SlideJoint CreateSlideJoint(C2sModel.Slide source) => new()
    {
        Tick = source.EndTick,
        Lane = source.EndLane,
        Width = source.EndWidth,
        Timeline = Timeline(source),
        Joint = source.Joint
    };

    private bool TryTakeAirAction(
        C2sModel.Air source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UmgrModel.NegativeNote? action)
    {
        action = null;

        if (source.Parent is null)
        {
            return false;
        }

        if (_airActionsByParent.TryGetValue(source.Parent, out var actions) &&
            actions.TryDequeue(out var candidate))
        {
            action = candidate;
            return true;
        }

        return false;
    }

    private void ConvertAir(C2sModel.Air source)
    {
        if (source.Parent is null)
        {
            return;
        }

        if (TryTakeAirAction(source, out var action))
        {
            switch (action)
            {
                case UmgrModel.AirHold hold:
                    hold.Direction = source.Direction;
                    hold.Color = source.Color;
                    break;

                case UmgrModel.AirSlide slide:
                    slide.Direction = source.Direction;
                    slide.Color = source.Color;
                    break;
            }

            return;
        }

        if (!_positiveNotes.TryGetValue(source.Parent, out var parent))
        {
            return;
        }

        // AirHold/AirSlide convert before Air and already MakePair the parent.
        switch (parent.PairNote)
        {
            case UmgrModel.AirHold hold:
                hold.Direction = source.Direction;
                hold.Color = source.Color;
                return;

            case UmgrModel.AirSlide slide:
                slide.Direction = source.Direction;
                slide.Color = source.Color;
                return;
        }

        var air = new UmgrModel.Air
        {
            Direction = source.Direction,
            Color = source.Color
        };

        Copy(source, air);
        _target.Notes.AppendChild(air);
        PairAirAction(source.Parent, air);
    }

    private void PairAirAction(
        C2sModel.Note sourceParent,
        UmgrModel.NegativeNote action)
    {
        if (!_positiveNotes.TryGetValue(sourceParent, out var parent))
        {
            return;
        }

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
            C2sModel.Tap =>
                UmgrModel.AirActionCarrierParent.Tap,

            C2sModel.ExTap =>
                UmgrModel.AirActionCarrierParent.ExTap,

            C2sModel.Flick =>
                UmgrModel.AirActionCarrierParent.Flick,

            C2sModel.Damage =>
                UmgrModel.AirActionCarrierParent.Damage,

            C2sModel.Hold =>
                UmgrModel.AirActionCarrierParent.Hold,

            C2sModel.Slide =>
                UmgrModel.AirActionCarrierParent.Slide,

            _ =>
                UmgrModel.AirActionCarrierParent.None
        };

        if (carrierParent == UmgrModel.AirActionCarrierParent.None)
        {
            parent.MakePair(action);
            return;
        }

        var carrierEffect = sourceParent switch
        {
            C2sModel.ExTap exTap =>
                exTap.Effect ?? ExEffect.UP,

            C2sModel.Hold hold =>
                hold.Effect ?? ExEffect.UP,

            C2sModel.Slide slide =>
                slide.Effect ?? ExEffect.UP,

            _ =>
                ExEffect.UP
        };

        var carrier = new UmgrModel.ExTap
        {
            Tick = parent.Tick,
            Lane = parent.Lane,
            Width = parent.Width,
            Timeline = parent.Timeline,
            Effect = carrierEffect,
            Role = UmgrModel.ExTapRole.AirActionCarrier,
            AirActionParent = carrierParent,

            AirActionParentJoint =
                sourceParent is C2sModel.Slide slideParent
                    ? slideParent.Joint
                    : Joint.D,

            AirActionParentIsEx =
                sourceParent switch
                {
                    C2sModel.Hold holdParent =>
                        holdParent.Effect is not null,

                    C2sModel.Slide slideEffectParent =>
                        slideEffectParent.Effect is not null,

                    _ =>
                        false
                }
        };

        _target.Notes.AppendChild(carrier);
        carrier.MakePair(action);
    }

    private void RegisterAirAction(
        C2sModel.Note parent,
        UmgrModel.NegativeNote action)
    {
        if (!_airActionsByParent.TryGetValue(parent, out var actions))
        {
            actions = [];
            _airActionsByParent[parent] = actions;
        }

        actions.Enqueue(action);
    }

    private void ConvertAirSlideChain(
        C2sModel.AirSlide source,
        IReadOnlyList<C2sModel.AirSlide> allSegments)
    {
        var air = new UmgrModel.AirSlide
        {
            Height = source.Height.Original,
            Color = source.Color
        };

        Copy(source, air);

        var segment = source;

        while (true)
        {
            _usedAirSegments.Add(segment);
            var next = allSegments.FirstOrDefault(
                x => !_usedAirSegments.Contains(x) && x.Parent is C2sModel.AirSlide && x.Tick == segment.EndTick && x.Lane == segment.EndLane && x.Width == segment.EndWidth && x.Height.Original == segment.EndHeight.Original && x.Color == segment.Color);

            air.AppendChild(new UmgrModel.AirSlideJoint
            {
                Tick = segment.EndTick,
                Lane = segment.EndLane,
                Width = segment.EndWidth,
                Timeline = Timeline(segment),
                Height = segment.EndHeight.Original,
                Joint = segment.Joint
            });

            if (next is null)
            {
                break;
            }

            segment = next;
        }

        _target.Notes.AppendChild(air);

        EnsureAirActionPaired(source, source.Parent, air);
    }

    private void ConvertAirHoldChain(
        C2sModel.AirHold source,
        IReadOnlyList<C2sModel.AirHold> allSegments)
    {
        var air = new UmgrModel.AirHold
        {
            Color = source.Color
        };
        Copy(source, air);

        var segment = source;

        while (true)
        {
            _usedAirSegments.Add(segment);
            var next = allSegments.FirstOrDefault(
                x => !_usedAirSegments.Contains(x) && x.Parent is C2sModel.AirHold && x.Tick == segment.EndTick && x.Lane == segment.EndLane && x.Width == segment.EndWidth);

            air.AppendChild(new UmgrModel.AirHoldJoint
            {
                Tick = segment.EndTick,
                Timeline = Timeline(segment),
                Joint = segment.Joint
            });

            if (next is null)
            {
                break;
            }

            segment = next;
        }

        _target.Notes.AppendChild(air);

        EnsureAirActionPaired(source, source.Parent, air);
    }

    private C2sModel.Note? ResolveAirActionPairParent(C2sModel.Note? parent)
    {
        if (parent is not C2sModel.Slide slide)
        {
            return parent;
        }

        if (!_positiveNotes.TryGetValue(slide, out var mappedParent) ||
            mappedParent is not UmgrModel.SlideJoint mappedJoint ||
            mappedJoint.Parent is not UmgrModel.Slide mappedSlide ||
            ReferenceEquals(mappedSlide.LastChild, mappedJoint))
        {
            return parent;
        }

        var terminalMatches = _source.Notes
            .OfType<C2sModel.Slide>()
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
                mappedCandidate is UmgrModel.SlideJoint candidateJoint &&
                candidateJoint.Parent is UmgrModel.Slide candidateSlide &&
                ReferenceEquals(candidateSlide.LastChild, candidateJoint))
            .ToArray();

        return terminalMatches.Length == 1
            ? terminalMatches[0]
            : parent;
    }

    private void EnsureAirActionPaired(
        C2sModel.Note source,
        C2sModel.Note? parent,
        UmgrModel.NegativeNote action)
    {
        var pairParent = ResolveAirActionPairParent(parent);

        var canUseMappedParent =
            pairParent is not null &&
            _positiveNotes.TryGetValue((C2sModel.Note)pairParent, out var positiveParent) &&
            (positiveParent is not UmgrModel.SlideJoint slideJoint ||
             slideJoint.Parent is UmgrModel.Slide mappedSlide &&
             ReferenceEquals(mappedSlide.LastChild, slideJoint));

        if (pairParent is not null && canUseMappedParent)
        {
            PairAirAction((C2sModel.Note)pairParent, action);

            if (action.PairNote is not null)
            {
                if (parent is not null)
                {
                    RegisterAirAction((C2sModel.Note)parent, action);
                }

                return;
            }
        }

        // A Slide action attached to an intermediate joint cannot be written
        // directly after that joint because MGXC long-note pairing is
        // sequential. Keep a dedicated carrier for that case, as well as for
        // unresolved parents.
        var carrier = new UmgrModel.ExTap
        {
            Tick = source.Tick,
            Lane = source.Lane,
            Width = source.Width,
            Timeline = Timeline(source),
            Effect = parent switch
            {
                C2sModel.ExTap exTap => exTap.Effect ?? ExEffect.UP,
                C2sModel.Hold hold => hold.Effect ?? ExEffect.UP,
                C2sModel.Slide slide => slide.Effect ?? ExEffect.UP,
                _ => ExEffect.UP
            },
            Role = UmgrModel.ExTapRole.AirActionCarrier,
            AirActionParent = parent switch
            {
                C2sModel.Tap => UmgrModel.AirActionCarrierParent.Tap,
                C2sModel.ExTap => UmgrModel.AirActionCarrierParent.ExTap,
                C2sModel.Flick => UmgrModel.AirActionCarrierParent.Flick,
                C2sModel.Damage => UmgrModel.AirActionCarrierParent.Damage,
                C2sModel.Hold => UmgrModel.AirActionCarrierParent.Hold,
                C2sModel.Slide => UmgrModel.AirActionCarrierParent.Slide,
                _ => UmgrModel.AirActionCarrierParent.Tap
            },
            AirActionParentJoint = parent is C2sModel.Slide slideParent
                ? slideParent.Joint
                : Joint.D,
            AirActionParentIsEx = parent switch
            {
                C2sModel.Hold holdParent => holdParent.Effect is not null,
                C2sModel.Slide slideEffectParent => slideEffectParent.Effect is not null,
                _ => false
            }
        };

        _target.Notes.AppendChild(carrier);
        carrier.MakePair(action);

        if (parent is null)
        {
            return;
        }

        RegisterAirAction((C2sModel.Note)parent, action);
    }

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

    private static AirCrashPathKey AirCrashStartKey(C2sModel.AirCrash note) => new(
        note.Tick.Original,
        note.Lane,
        note.Width,
        note.Height.Original,
        note.Color,
        note.Density.Original,
        AirCrashPathAttr(note.Attr));

    private static AirCrashPathKey AirCrashEndKey(C2sModel.AirCrash note) => new(
        note.EndTick.Original,
        note.EndLane,
        note.EndWidth,
        note.EndHeight.Original,
        note.Color,
        note.Density.Original,
        AirCrashPathAttr(note.Attr));

    private void ConvertAirCrashes(IEnumerable<C2sModel.AirCrash> source)
    {
        var active = new Dictionary<AirCrashPathKey, Queue<UmgrModel.AirCrash>>();

        foreach (var segment in source
                     .Select((segment, index) => (Segment: segment, SourceOrder: index))
                     .OrderBy(x => x.Segment.Tick.Original)
                     .ThenBy(x => x.SourceOrder)
                     .Select(x => x.Segment))
        {
            var startKey = AirCrashStartKey(segment);
            UmgrModel.AirCrash crash;

            if (active.TryGetValue(startKey, out var startQueue) && startQueue.Count > 0)
            {
                crash = startQueue.Dequeue();
                if (startQueue.Count == 0)
                {
                    active.Remove(startKey);
                }
            }
            else
            {
                crash = new UmgrModel.AirCrash
                {
                    Height = segment.Height.Original,
                    Color = segment.Color,
                    Density = segment.Density,
                    Attr = segment.Attr
                };
                Copy(segment, crash);
                _target.Notes.AppendChild(crash);
            }

            crash.AppendChild(new UmgrModel.AirCrashJoint
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
                endQueue = new Queue<UmgrModel.AirCrash>();
                active[endKey] = endQueue;
            }

            endQueue.Enqueue(crash);
        }
    }

    private static void Copy(C2sModel.Note source, UmgrModel.Note target)
    {
        target.Tick = source.Tick;
        target.Lane = source.Lane;
        target.Width = source.Width;
        target.Timeline = Timeline(source);
    }

    private static int Timeline(C2sModel.Note note) => Math.Max(0, note.Timeline);

    private void ApplySlaTimelines()
    {
        var regions = _source.Notes.OfType<C2sModel.Sla>().ToArray();
        foreach (var note in Flatten(_target.Notes.Children))
        {
            var timeline = regions.Where(x => Contains(x, note)).Select(x => x.Timeline).DefaultIfEmpty(note.Timeline).Max();
            note.Timeline = Math.Max(0, timeline);
        }
    }

    private static IEnumerable<UmgrModel.Note> Flatten(IEnumerable<UmgrModel.Note> notes)
    {
        foreach (var note in notes)
        {
            yield return note;
            foreach (var child in Flatten(note.Children))
            {
                yield return child;
            }
        }
    }

    private static bool Contains(C2sModel.Sla sla, UmgrModel.Note note)
    {
        var end = sla.Tick.Original + sla.Length.Original;
        return note.Tick.Original >= sla.Tick.Original && note.Tick.Original < end &&
               note.Lane >= sla.Lane && note.Lane + note.Width <= sla.Lane + sla.Width;
    }

    private void EmitDebugTilMarkers()
    {
        foreach (var sla in _source.Notes.OfType<C2sModel.Sla>())
        {
            var crash = new UmgrModel.AirCrash
            {
                Tick = sla.Tick,
                Lane = sla.Lane,
                Width = sla.Width,
                Timeline = 0,
                Height = 0,
                Color = Color.NON,
                Density = 0
            };
            crash.AppendChild(new UmgrModel.AirCrashJoint
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
