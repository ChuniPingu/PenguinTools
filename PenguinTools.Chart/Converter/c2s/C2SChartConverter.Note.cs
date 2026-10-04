using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

using C2sModel = PenguinTools.Chart.Models.c2s;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Converter.c2s;

public partial class C2SChartConverter
{
    private readonly Dictionary<UmgrModel.NegativeNote, C2sModel.IPairable> _negativePairRoots = [];
    private readonly Dictionary<UmgrModel.PositiveNote, C2sModel.Note> _positivePairTargets = [];
    private readonly Dictionary<UmgrModel.PositiveNote, C2sModel.Note> _positivePairRealTargets = [];
    private readonly Dictionary<C2sModel.Slide, C2sSlideSegmentSource> _slideSegmentSources = [];

    private T CreateNote<TSource, T>(TSource source, Action<T>? action = null)
        where TSource : UmgrModel.Note where T : C2sModel.Note, new()
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

    private void CreatePositiveNote<TSource, T>(TSource source, Action<T>? action = null)
        where TSource : UmgrModel.PositiveNote where T : C2sModel.Note, new()
    {
        var note = CreateNote(source, action);
        RegisterPositivePairTarget(source, note);
    }

    private void RegisterPositivePairTarget(UmgrModel.PositiveNote source, C2sModel.Note target)
    {
        _positivePairRealTargets[source] = target;
        _positivePairTargets[source] = CreateGenericAirParent(target);
    }

    private void RegisterNegativePairRoot(
        UmgrModel.NegativeNote source,
        C2sModel.IPairable target)
    {
        _negativePairRoots[source] = target;
    }

    // C2S AIR parent tokens are generic HLD/SLD even when the attach point is
    // an EX or control segment. Keep one dummy shape so pairing and writing agree.
    private static C2sModel.Note CreateGenericAirParent(C2sModel.Note target) => target switch
    {
        C2sModel.Hold => new C2sModel.Hold(),
        C2sModel.Slide => new C2sModel.Slide { Joint = Joint.D },
        _ => target
    };

    private void RegisterAirActionCarrier(
        UmgrModel.ExTap carrier)
    {
        C2sModel.Note? parent = carrier.AirActionParent switch
        {
            UmgrModel.AirActionCarrierParent.Tap =>
                new C2sModel.Tap(),

            UmgrModel.AirActionCarrierParent.ExTap =>
                new C2sModel.ExTap
                {
                    Effect = carrier.Effect
                },

            UmgrModel.AirActionCarrierParent.Flick =>
                new C2sModel.Flick(),

            UmgrModel.AirActionCarrierParent.Damage =>
                new C2sModel.Damage(),

            UmgrModel.AirActionCarrierParent.Hold =>
                CreateGenericAirParent(new C2sModel.Hold()),

            UmgrModel.AirActionCarrierParent.Slide =>
                CreateGenericAirParent(new C2sModel.Slide()),

            _ => null
        };

        if (parent is not null)
        {
            _positivePairTargets[carrier] = parent;
        }
    }

    private void ResolvePairings()
    {
        foreach (var (source, root) in _negativePairRoots)
        {
            if (source.PairNote is null)
            {
                continue;
            }

            if (!_positivePairTargets.ContainsKey(source.PairNote) &&
                source.PairNote is UmgrModel.ExTap
                {
                    Role: UmgrModel.ExTapRole.AirActionCarrier
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

    // Exact bare carriers paint the long head and are omitted from C2S.
    // Margrete only consumes the first such ExTap per cell; later duplicates
    // stay as CHR (one extra TAP). Track cells already consumed this convert.
    private readonly HashSet<(int Tick, int Lane, int Width)> _consumedExLongCarrierCells = [];

    private bool ShouldConsumeExLongCarrier(UmgrModel.ExTap exTap)
    {
        if (exTap.PairNote is not null)
        {
            return false;
        }

        var eligible = exTap.Role switch
        {
            UmgrModel.ExTapRole.HoldOnlyCarrier =>
                (Func<UmgrModel.ExTapableNote, bool>)(note => note is UmgrModel.Hold),
            UmgrModel.ExTapRole.SharedLongCarrier =>
                _ => true,
            _ => null
        };

        if (eligible is null || !HasExactLongHead(exTap, eligible))
        {
            return false;
        }

        return _consumedExLongCarrierCells.Add(
            (exTap.Tick.Original, exTap.Lane, exTap.Width));
    }

    private bool HasExactLongHead(UmgrModel.ExTap exTap, Func<UmgrModel.ExTapableNote, bool> eligible) =>
        Mgxc.Notes.Children
            .OfType<UmgrModel.ExTapableNote>()
            .Any(note =>
                eligible(note) &&
                note.Tick == exTap.Tick &&
                note.Lane == exTap.Lane &&
                note.Width == exTap.Width);

    private void ConvertNote(UmgrModel.Note e)
    {
        switch (e)
        {
            case UmgrModel.SoflanArea sla:
                ProcessSoflanArea(sla);
                break;
            case UmgrModel.Tap tap:
                CreatePositiveNote<UmgrModel.Tap, C2sModel.Tap>(tap);
                break;
            case UmgrModel.ExTap { Role: UmgrModel.ExTapRole.AirActionCarrier }:
                break;
            // UMIGURI paints EX longs with a covering ExTap. Consume only the
            // first exact bare carrier per (tick, lane, width); later duplicates
            // stay as CHR. A strictly larger covering ExTap also stays as CHR
            // while still converting the covered heads.
            case UmgrModel.ExTap exTap when ShouldConsumeExLongCarrier(exTap):
                break;
            case UmgrModel.ExTap exTap:
                CreatePositiveNote<UmgrModel.ExTap, C2sModel.ExTap>(
                    exTap,
                    x => x.Effect = exTap.Effect);
                break;
            case UmgrModel.Flick flick:
                CreatePositiveNote<UmgrModel.Flick, C2sModel.Flick>(flick);
                break;
            case UmgrModel.Damage damage:
                CreatePositiveNote<UmgrModel.Damage, C2sModel.Damage>(damage);
                break;
            case UmgrModel.Hold hold:
                ProcessHold(hold);
                break;
            case UmgrModel.Slide slide:
                ProcessSlide(slide);
                break;
            case UmgrModel.Air airNote:
                ProcessAir(airNote);
                break;
            case UmgrModel.AirSlide airSlide:
                ProcessAirSlide(airSlide);
                break;
            case UmgrModel.AirHold airHold:
                ProcessAirHold(airHold);
                break;
            case UmgrModel.AirCrash airCrash:
                ProcessAirCrash(airCrash);
                break;
        }
    }

    private void ProcessAirCrash(UmgrModel.AirCrash airCrash)
    {
        var joints = airCrash.Children.OfType<UmgrModel.AirCrashJoint>().Prepend(airCrash.AsChild()).ToArray();

        var density = airCrash.Density;
        if (density.Original >= 0x7FFFFFFF)
        {
            density = (airCrash.GetLastTick() - airCrash.Tick.Original) * 2;
        }

        for (var i = 0; i < joints.Length - 1; i++)
        {
            var curr = joints[i];
            var next = joints[i + 1];
            CreateNote<UmgrModel.AirCrashJoint, C2sModel.AirCrash>(curr, x =>
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
    // from a real UmgrModel.Air (including an overlapping note that owns AIR).
    private void ProcessAirSlide(UmgrModel.AirSlide airSlide)
    {
        if (airSlide.PairNote?.PairNote != airSlide)
        {
            throw new TimedDiagnosticException(MsgKeys.MgCrit_Invalid_AirSlide_parent, airSlide.Tick.Original,
                airSlide);
        }

        var joints = airSlide.Children.OfType<UmgrModel.AirSlideJoint>().Prepend(airSlide.AsChild()).ToArray();
        C2sModel.AirSlide? firstSegment = null;
        C2sModel.Note? previousSegment = null;
        for (var i = 0; i < joints.Length - 1; i++)
        {
            var curr = joints[i];
            var next = joints[i + 1];
            var prevSeg = previousSegment;
            var segment = CreateNote<UmgrModel.AirSlideJoint, C2sModel.AirSlide>(curr, x =>
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

        if (firstSegment != null)
        {
            RegisterNegativePairRoot(airSlide, firstSegment);
        }
    }

    private void ProcessAirHold(UmgrModel.AirHold airHold)
    {
        if (airHold.PairNote?.PairNote != airHold)
        {
            throw new TimedDiagnosticException(MsgKeys.MgCrit_Invalid_AirSlide_parent, airHold.Tick.Original,
                airHold);
        }

        var joints = airHold.Children.OfType<UmgrModel.AirHoldJoint>().Prepend(airHold.AsChild()).ToArray();
        C2sModel.AirHold? firstSegment = null;
        C2sModel.Note? previousSegment = null;
        for (var i = 0; i < joints.Length - 1; i++)
        {
            var curr = joints[i];
            var next = joints[i + 1];
            var prevSeg = previousSegment;
            var segment = CreateNote<UmgrModel.AirHoldJoint, C2sModel.AirHold>(curr, x =>
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

        if (firstSegment != null)
        {
            RegisterNegativePairRoot(airHold, firstSegment);
        }
    }

    private void ProcessAir(UmgrModel.Air airNote)
    {
        if (airNote.PairNote?.PairNote != airNote)
        {
            throw new TimedDiagnosticException(MsgKeys.MgCrit_Invalid_Air_parent, airNote.Tick.Original, airNote);
        }

        var note = CreateNote<UmgrModel.Air, C2sModel.Air>(airNote, x =>
        {
            x.Direction = airNote.Direction;
            x.Color = airNote.Color;
        });
        RegisterNegativePairRoot(airNote, note);
    }

    private void ProcessSlide(UmgrModel.Slide slide)
    {
        var joints = slide.Children.OfType<UmgrModel.SlideJoint>().Prepend(slide.AsChild()).ToArray();
        for (var i = 0; i < joints.Length - 1; i++)
        {
            var curr = joints[i];
            var next = joints[i + 1];
            var note = CreateNote<UmgrModel.SlideJoint, C2sModel.Slide>(curr, x =>
            {
                x.Joint = next.Joint;
                x.EndTick = next.Tick;
                x.EndLane = next.Lane;
                x.EndWidth = next.Width;
                x.NoLine = curr.NoLine;
                x.Effect = curr.HasEffectOverride ? curr.SegmentEffect : slide.Effect;
            });
            _slideSegmentSources[note] = new C2sSlideSegmentSource(
                slide,
                next,
                i == 0);
            // pair the last joint with air
            if (i == joints.Length - 2)
            {
                RegisterPositivePairTarget(next, note);
            }
        }
    }

    private void ProcessSoflanArea(UmgrModel.SoflanArea sla)
    {
        if (sla.LastChild is not UmgrModel.SoflanAreaJoint tail)
        {
            throw new TimedDiagnosticException(MsgKeys.MgCrit_SoflanArea_has_no_tail, sla.Tick.Original, sla);
        }

        CreateNote<UmgrModel.SoflanArea, C2sModel.Sla>(sla, x => { x.Length = tail.Tick.Round - sla.Tick.Round; });
    }

    private void ProcessHold(UmgrModel.Hold hold)
    {
        if (hold.LastChild is not UmgrModel.HoldJoint tail)
        {
            throw new TimedDiagnosticException(MsgKeys.MgCrit_Hold_has_no_tail, hold.Tick.Original, hold);
        }

        var note = CreateNote<UmgrModel.Hold, C2sModel.Hold>(hold, x =>
        {
            x.EndTick = tail.Tick;
            x.Effect = hold.Effect;
        });
        RegisterPositivePairTarget(tail, note);
    }
}
