using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Converter.c2s;

using umgr = Models.umgr;
using c2s = Models.c2s;

public partial class C2SChartConverter
{
    private readonly Dictionary<umgr.NegativeNote, c2s.IPairable> _negativePairRoots = [];
    private readonly Dictionary<umgr.PositiveNote, c2s.Note> _positivePairTargets = [];
    private readonly Dictionary<c2s.Slide, C2sSlideSegmentSource> _slideSegmentSources = [];

    private T CreateNote<TSource, T>(TSource source, Action<T>? action = null)
        where TSource : umgr.Note where T : c2s.Note, new()
    {
        var note = new T
        {
            Timeline = source.Timeline,
            Tick = source.Tick,
            Lane = source.Lane,
            Width = source.Width
        };

        action?.Invoke(note);
        Notes.Add(note);

        return note;
    }

    private T CreatePositiveNote<TSource, T>(TSource source, Action<T>? action = null)
        where TSource : umgr.PositiveNote where T : c2s.Note, new()
    {
        var note = CreateNote(source, action);
        RegisterPositivePairTarget(source, note);
        return note;
    }

    private void RegisterPositivePairTarget(umgr.PositiveNote source, c2s.Note target)
    {
        _positivePairTargets[source] = CreateGenericAirParent(target);
    }

    private void RegisterNegativePairRoot(
        umgr.NegativeNote source,
        c2s.IPairable target)
    {
        _negativePairRoots[source] = target;
    }

    // C2S AIR parent tokens are generic HLD/SLD even when the attach point is
    // an EX or control segment. Keep one dummy shape so pairing and writing agree.
    private static c2s.Note CreateGenericAirParent(c2s.Note target) => target switch
    {
        c2s.Hold => new c2s.Hold(),
        c2s.Slide => new c2s.Slide { Joint = Joint.D },
        _ => target
    };

    private void RegisterAirActionCarrier(
        umgr.ExTap carrier)
    {
        c2s.Note? parent = carrier.AirActionParent switch
        {
            umgr.AirActionCarrierParent.Tap =>
                new c2s.Tap(),

            umgr.AirActionCarrierParent.ExTap =>
                new c2s.ExTap
                {
                    Effect = carrier.Effect
                },

            umgr.AirActionCarrierParent.Flick =>
                new c2s.Flick(),

            umgr.AirActionCarrierParent.Damage =>
                new c2s.Damage(),

            umgr.AirActionCarrierParent.Hold =>
                CreateGenericAirParent(new c2s.Hold()),

            umgr.AirActionCarrierParent.Slide =>
                CreateGenericAirParent(new c2s.Slide()),

            _ => null
        };

        if (parent is not null)
            _positivePairTargets[carrier] = parent;
    }

    private void ResolvePairings()
    {
        foreach (var (source, root) in _negativePairRoots)
        {
            if (source.PairNote is null)
                continue;

            if (!_positivePairTargets.ContainsKey(source.PairNote) &&
                source.PairNote is umgr.ExTap
                {
                    Role: umgr.ExTapRole.AirActionCarrier
                } carrier)
            {
                RegisterAirActionCarrier(carrier);
            }

            if (_positivePairTargets.TryGetValue(
                    source.PairNote,
                    out var parent))
            {
                root.Parent = parent;
            }
        }
    }

    private bool ShouldConsumeExLongCarrier(umgr.ExTap exTap)
    {
        if (exTap.PairNote is not null)
            return false;

        return exTap.Role switch
        {
            umgr.ExTapRole.HoldOnlyCarrier =>
                HasExactLongHead(exTap, note => note is umgr.Hold),
            umgr.ExTapRole.SharedLongCarrier =>
                HasExactLongHead(exTap, _ => true),
            _ => false
        };
    }

    private bool HasExactLongHead(umgr.ExTap exTap, Func<umgr.ExTapableNote, bool> eligible) =>
        Mgxc.Notes.Children
            .OfType<umgr.ExTapableNote>()
            .Any(note =>
                eligible(note) &&
                note.Tick == exTap.Tick &&
                note.Lane == exTap.Lane &&
                note.Width == exTap.Width);

    private void ConvertNote(umgr.Note e)
    {
        switch (e)
        {
            case umgr.SoflanArea sla:
                ProcessSoflanArea(sla);
                break;
            case umgr.Tap tap:
                CreatePositiveNote<umgr.Tap, c2s.Tap>(tap);
                break;
            case umgr.ExTap { Role: umgr.ExTapRole.AirActionCarrier }:
                break;
            // UMIGURI paints EX longs with a covering ExTap. Consume that ExTap
            // only when it sits on the same tick/lane/width as a long-note head
            // and does not also own an AIR action. A strictly larger covering
            // ExTap stays as CHR while still converting the covered heads.
            case umgr.ExTap exTap when ShouldConsumeExLongCarrier(exTap):
                break;
            case umgr.ExTap exTap:
                CreatePositiveNote<umgr.ExTap, c2s.ExTap>(
                    exTap,
                    x => x.Effect = exTap.Effect);
                break;
            case umgr.Flick flick:
                CreatePositiveNote<umgr.Flick, c2s.Flick>(flick);
                break;
            case umgr.Damage damage:
                CreatePositiveNote<umgr.Damage, c2s.Damage>(damage);
                break;
            case umgr.Hold hold:
                ProcessHold(hold);
                break;
            case umgr.Slide slide:
                ProcessSlide(slide);
                break;
            case umgr.Air airNote:
                ProcessAir(airNote);
                break;
            case umgr.AirSlide airSlide:
                ProcessAirSlide(airSlide);
                break;
            case umgr.AirHold airHold:
                ProcessAirHold(airHold);
                break;
            case umgr.AirCrash airCrash:
                ProcessAirCrash(airCrash);
                break;
        }
    }

    private void ProcessAirCrash(umgr.AirCrash airCrash)
    {
        var joints = airCrash.Children.OfType<umgr.AirCrashJoint>().Prepend(airCrash.AsChild()).ToArray();

        var density = airCrash.Density;
        if (density.Original >= 0x7FFFFFFF) density = (airCrash.GetLastTick() - airCrash.Tick.Original) * 2;

        for (var i = 0; i < joints.Length - 1; i++)
        {
            var curr = joints[i];
            var next = joints[i + 1];
            CreateNote<umgr.AirCrashJoint, c2s.AirCrash>(curr, x =>
            {
                x.EndTick = next.Tick;
                x.EndLane = next.Lane;
                x.EndWidth = next.Width;
                x.Height = curr.Height;
                x.EndHeight = next.Height;
                x.Color = airCrash.Color;
                x.Attr = airCrash.Attr;
                x.Density = density;
            });
        }
    }

    // C2S AirSlide already includes its arrow. A sibling AIR is only emitted
    // from a real umgr.Air (including an overlapping note that owns AIR).
    private void ProcessAirSlide(umgr.AirSlide airSlide)
    {
        if (airSlide.PairNote?.PairNote != airSlide)
            throw new TimedDiagnosticException(MsgKeys.MgCrit_Invalid_AirSlide_parent, airSlide.Tick.Original,
                airSlide);

        var joints = airSlide.Children.OfType<umgr.AirSlideJoint>().Prepend(airSlide.AsChild()).ToArray();
        c2s.AirSlide? firstSegment = null;
        c2s.Note? previousSegment = null;
        for (var i = 0; i < joints.Length - 1; i++)
        {
            var curr = joints[i];
            var next = joints[i + 1];
            var prevSeg = previousSegment;
            var segment = CreateNote<umgr.AirSlideJoint, c2s.AirSlide>(curr, x =>
            {
                x.Parent = prevSeg;
                x.Color = airSlide.Color;
                x.Height = curr.Height;
                x.Joint = next.Joint;
                x.EndTick = next.Tick;
                x.EndLane = next.Lane;
                x.EndWidth = next.Width;
                x.EndHeight = next.Height;
            });
            firstSegment ??= segment;
            previousSegment = segment;
        }

        if (firstSegment != null) RegisterNegativePairRoot(airSlide, firstSegment);
    }

    private void ProcessAirHold(umgr.AirHold airHold)
    {
        if (airHold.PairNote?.PairNote != airHold)
            throw new TimedDiagnosticException(MsgKeys.MgCrit_Invalid_AirSlide_parent, airHold.Tick.Original,
                airHold);

        var joints = airHold.Children.OfType<umgr.AirHoldJoint>().Prepend(airHold.AsChild()).ToArray();
        c2s.AirHold? firstSegment = null;
        c2s.Note? previousSegment = null;
        for (var i = 0; i < joints.Length - 1; i++)
        {
            var curr = joints[i];
            var next = joints[i + 1];
            var prevSeg = previousSegment;
            var segment = CreateNote<umgr.AirHoldJoint, c2s.AirHold>(curr, x =>
            {
                x.Parent = prevSeg;
                x.Color = airHold.Color;
                x.Joint = next.Joint;
                x.EndTick = next.Tick;
                x.EndLane = next.Lane;
                x.EndWidth = next.Width;
            });
            firstSegment ??= segment;
            previousSegment = segment;
        }

        if (firstSegment != null) RegisterNegativePairRoot(airHold, firstSegment);
    }

    private void ProcessAir(umgr.Air airNote)
    {
        if (airNote.PairNote?.PairNote != airNote)
            throw new TimedDiagnosticException(MsgKeys.MgCrit_Invalid_Air_parent, airNote.Tick.Original, airNote);

        var note = CreateNote<umgr.Air, c2s.Air>(airNote, x =>
        {
            x.Direction = airNote.Direction;
            x.Color = airNote.Color;
        });
        RegisterNegativePairRoot(airNote, note);
    }

    private void ProcessSlide(umgr.Slide slide)
    {
        var joints = slide.Children.OfType<umgr.SlideJoint>().Prepend(slide.AsChild()).ToArray();
        for (var i = 0; i < joints.Length - 1; i++)
        {
            var curr = joints[i];
            var next = joints[i + 1];
            var index = i;
            var note = CreateNote<umgr.SlideJoint, c2s.Slide>(curr, x =>
            {
                x.Joint = next.Joint;
                x.EndTick = next.Tick;
                x.EndLane = next.Lane;
                x.EndWidth = next.Width;
                x.NoLine = curr.NoLine;
                x.Effect = slide.Effect;
            });
            _slideSegmentSources[note] = new C2sSlideSegmentSource(
                slide,
                next,
                i == 0);
            // pair the last joint with air
            if (i == joints.Length - 2)
                RegisterPositivePairTarget(next, note);
        }
    }

    private void ProcessSoflanArea(umgr.SoflanArea sla)
    {
        if (sla.LastChild is not umgr.SoflanAreaJoint tail)
            throw new TimedDiagnosticException(MsgKeys.MgCrit_SoflanArea_has_no_tail, sla.Tick.Original, sla);

        CreateNote<umgr.SoflanArea, c2s.Sla>(sla, x => { x.Length = tail.Tick.Round - sla.Tick.Round; });
    }

    private void ProcessHold(umgr.Hold hold)
    {
        if (hold.LastChild is not umgr.HoldJoint tail)
            throw new TimedDiagnosticException(MsgKeys.MgCrit_Hold_has_no_tail, hold.Tick.Original, hold);

        var note = CreateNote<umgr.Hold, c2s.Hold>(hold, x =>
        {
            x.EndTick = tail.Tick;
            x.Effect = hold.Effect;
        });
        RegisterPositivePairTarget(tail, note);
    }
}
