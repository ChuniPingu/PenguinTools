using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser.ugc;

using umgr = Models.umgr;

public partial class UgcParser
{
    private void DispatchBodyLine(string line)
    {
        if (!line.StartsWith('#'))
            return;
        var rest = line.AsSpan(1);
        var parentIdx = rest.IndexOf(':');
        if (parentIdx < 0)
        {
            var childIdx = rest.IndexOf('>');
            if (childIdx <= 0 || !int.TryParse(rest[..childIdx], out var offsetTick))
            {
                WarnMalformed(line);
                return;
            }

            HandleChildPayload(offsetTick, rest[(childIdx + 1)..].ToString());
            return;
        }

        var lhs = rest[..parentIdx];
        var tickSepIdx = lhs.IndexOf('\'');
        if (tickSepIdx < 0)
        {
            if (!int.TryParse(lhs, out var offset))
            {
                WarnMalformed(line);
                return;
            }

            var payload = rest[(parentIdx + 1)..].ToString();
            HandleChildPayload(offset, payload);
            return;
        }

        if (tickSepIdx == 0
            || !int.TryParse(lhs[..tickSepIdx], out var bar)
            || !int.TryParse(lhs[(tickSepIdx + 1)..], out var tick))
        {
            WarnMalformed(line);
            return;
        }

        var rhs = rest[(parentIdx + 1)..].ToString();
        var commaIdx = rhs.IndexOf(',');
        var payloadStr = commaIdx >= 0 ? rhs[..commaIdx] : rhs;
        var suffixStr = commaIdx >= 0 ? rhs[(commaIdx + 1)..] : string.Empty;

        var absTick = BarTickToAbsTick(bar, tick);
        HandleParentPayload(absTick, payloadStr, suffixStr);
    }

    private void HandleParentPayload(int absTick, string payload, string suffix)
    {
        if (payload is ['c'])
        {
            Ugc.Extras.ClickTicks.Add(absTick);
            _lastNote = null;
            return;
        }

        if (payload.Length < 3)
        {
            WarnMalformed(payload);
            return;
        }

        var typeChar = payload[0];
        var x = UgcPayload.Lane(payload[1]);
        var w = UgcPayload.Lane(payload[2]);
        if (w <= 0)
        {
            WarnMalformed(payload);
            return;
        }

        var extras = payload.Length > 3 ? payload[3..] : string.Empty;

        if (typeChar == 'a')
        {
            HandleAirParent(absTick, x, w, extras, payload);
            return;
        }

        if (typeChar == 'H' && extras.Length < 3)
        {
            HandleAirHoldParent(absTick, x, w, extras);
            return;
        }

        if (typeChar is 'S' or 'H')
        {
            HandleAirSlideParent(absTick, x, w, extras, payload);
            return;
        }

        if (typeChar == 'C')
        {
            HandleAirCrashParent(absTick, x, w, extras, payload, suffix);
            return;
        }

        var note = typeChar switch
        {
            't' => new umgr.Tap(),
            'x' => MakeExTap(extras),
            'f' => new umgr.Flick(),
            'd' => new umgr.Damage(),
            _ => HandleLongNoteParent(typeChar)
        };

        if (note is null)
        {
            WarnUnknownType(typeChar);
            return;
        }

        note.Tick = absTick;
        note.Lane = x;
        note.Width = w;
        note.Timeline = _currentTimeline;

        Ugc.Notes.AppendChild(note);
        _lastParentNote = note;
        _lastNote = note;
    }

    private void HandleAirParent(int absTick, int x, int w, string extras, string payload)
    {
        if (extras.Length < 3)
        {
            WarnMalformed(payload);
            return;
        }

        var air = new umgr.Air
        {
            Direction = UgcPayload.AirDirectionCode(extras.AsSpan(0, 2)),
            Color = UgcPayload.AirColorChar(extras[2]),
            Timeline = _currentTimeline,
            Tick = absTick,
            Lane = x,
            Width = w
        };

        Ugc.Notes.AppendChild(air);

        var pairPositive = FindPairPositive(absTick, x, w);
        if (pairPositive != null)
            pairPositive.MakePair(air);
        else
            ReportAtCurrentLine(Severity.Warning, Msg.Key(MsgKeys.MgCrit_Pairing_notes_incompatible));

        _lastNote = air;
    }

    private void HandleAirHoldParent(int absTick, int x, int w, string extras)
    {
        var color = ParseAirHoldColor(extras);
        var airHold = new umgr.AirHold
        {
            Color = color,
            Timeline = _currentTimeline,
            Tick = absTick,
            Lane = x,
            Width = w
        };
        Ugc.Notes.AppendChild(airHold);

        if (_lastNote is umgr.Air oldAir && oldAir.Tick.Original == absTick)
        {
            airHold.Direction = oldAir.Direction;
            airHold.Color = oldAir.Color;
            oldAir.Parent?.RemoveChild(oldAir);
            _lastNote = oldAir.PairNote;
        }

        var pairPositive = FindPairPositive(absTick, x, w);
        if (pairPositive != null)
            pairPositive.MakePair(airHold);

        _lastParentNote = airHold;
        _lastNote = airHold;
    }

    private void HandleAirSlideParent(int absTick, int x, int w, string extras, string payload)
    {
        if (extras.Length < 3)
        {
            WarnMalformed(payload);
            return;
        }

        var height = UgcPayload.Height36(extras.AsSpan(0, 2));

        var airSlide = new umgr.AirSlide
        {
            Height = height,
            Color = UgcPayload.AirColorChar(extras[2]),
            Timeline = _currentTimeline,
            Tick = absTick,
            Lane = x,
            Width = w
        };
        Ugc.Notes.AppendChild(airSlide);

        if (_lastNote is umgr.Air oldAir && oldAir.Tick.Original == absTick)
        {
            airSlide.Direction = oldAir.Direction;
            airSlide.Color = oldAir.Color;
            oldAir.Parent?.RemoveChild(oldAir);
            _lastNote = oldAir.PairNote;
        }

        var pairPositive = FindPairPositive(absTick, x, w);
        if (pairPositive != null)
            pairPositive.MakePair(airSlide);

        _lastParentNote = airSlide;
        _lastNote = airSlide;
    }

    private void HandleAirCrashParent(int absTick, int x, int w, string extras, string payload, string suffix)
    {
        if (extras.Length < 3)
        {
            WarnMalformed(payload);
            return;
        }

        var height = UgcPayload.Height36(extras.AsSpan(0, 2));

        var crash = new umgr.AirCrash
        {
            Color = UgcPayload.CrushColorChar(extras[2]),
            Height = height,
            Density = suffix == "$" ? int.MaxValue : ScaleTick(UgcPayload.AirCrashInterval(suffix)),
            Attr = ParseAirLadderAttribute(extras),
            Tick = absTick,
            Lane = x,
            Width = w,
            Timeline = _currentTimeline
        };
        Ugc.Notes.AppendChild(crash);
        _lastParentNote = crash;
        _lastNote = crash;
    }

    // Last PositiveNote at absTick when _lastNote is a non-positive long parent (Hold/Slide).
    private umgr.PositiveNote? FindPairPositive(int absTick, int lane, int width)
    {
        if (_lastNote is umgr.PositiveNote lastP && lastP.Tick.Original == absTick && lastP.Lane == lane && lastP.Width == width)
            return lastP;

        return Ugc.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children)).OfType<umgr.PositiveNote>().LastOrDefault(p => p.Tick.Original == absTick && p.Lane == lane && p.Width == width);
    }

    private static AirLadderAttr ParseAirLadderAttribute(string extras)
    {
        if (extras.Length <= 3) return AirLadderAttr.DEF;
        return extras[3] switch
        {
            'Y' => AirLadderAttr.AxisY,
            'Z' => AirLadderAttr.AxisZ,
            _ => AirLadderAttr.DEF
        };
    }

    private static umgr.ExTapRole ParseExTapRole(string extras)
    {
        if (extras.Contains('!')) return umgr.ExTapRole.Explicit;
        return extras.Contains('~') ? umgr.ExTapRole.SharedLongCarrier : umgr.ExTapRole.Auto;
    }

    private static umgr.ExTap MakeExTap(string extras)
    {
        var exNote = new umgr.ExTap
        {
            Role = ParseExTapRole(extras),
            Effect = extras.Length >= 1 ? UgcPayload.ExEffectChar(extras[0]) : ExEffect.UP
        };
        return exNote;
    }

    private static umgr.Note? HandleLongNoteParent(char typeChar)
    {
        return typeChar switch
        {
            'h' => new umgr.Hold(),
            's' => new umgr.Slide(),
            _ => null
        };
    }

    private static Color ParseAirHoldColor(string extras)
    {
        if (extras.Length >= 3)
            return UgcPayload.AirColorChar(extras[2]);
        return extras.Length >= 1 ? UgcPayload.AirColorChar(extras[0]) : Color.DEF;
    }

    private string NormalizeSlidePayload(string payload)
    {
        if (_lastParentNote is umgr.Slide slide && payload.Length > 0)
        {
            var noLine = payload[0] is 'n' or 'N' or 'V';
            var previous = slide.Children.OfType<umgr.SlideJoint>().LastOrDefault();
            if (previous is null)
                slide.NoLine = noLine;
            else
                previous.NoLine = noLine;
            if (noLine)
                payload = (payload[0] == 'n' ? "s" : "c") + payload[1..];
        }
        return payload;
    }

    private bool HandleCompactChild(int offsetTick, string payload)
    {
        if (payload.Trim() == "s" && _lastParentNote is umgr.Hold hold)
        {
            var hj = new umgr.HoldJoint
            {
                Tick = hold.Tick.Original + offsetTick,
                Lane = hold.Lane,
                Width = hold.Width,
                Timeline = _currentTimeline
            };
            hold.AppendChild(hj);
            _lastNote = hj;
            return true;
        }

        if (payload.Length == 1 && _lastParentNote is umgr.AirHold airHold)
        {
            if (payload[0] is not ('s' or 'c'))
            {
                WarnMalformed(payload);
                return true;
            }

            var joint = new umgr.AirHoldJoint
            {
                Tick = airHold.Tick.Original + offsetTick,
                Timeline = _currentTimeline,
                Joint = payload[0] == 'c' ? Joint.C : Joint.D
            };
            airHold.AppendChild(joint);
            _lastNote = joint;
            return true;
        }

        if (payload.Length == 1 && _lastParentNote is umgr.AirSlide airSlide)
        {
            if (payload[0] is not ('s' or 'c'))
            {
                WarnMalformed(payload);
                return true;
            }

            var joint = new umgr.AirSlideJoint
            {
                Tick = airSlide.Tick.Original + offsetTick,
                Lane = airSlide.Lane,
                Width = airSlide.Width,
                Timeline = _currentTimeline,
                Height = airSlide.Height,
                Joint = payload[0] == 'c' ? Joint.C : Joint.D
            };
            airSlide.AppendChild(joint);
            _lastNote = joint;
            return true;
        }

        return false;
    }

    private void HandleChildPayload(int offsetTick, string payload)
    {
        offsetTick = ScaleTick(offsetTick);
        payload = NormalizeSlidePayload(payload);
        if (_lastParentNote is null)
        {
            WarnMalformed(payload);
            return;
        }

        if (HandleCompactChild(offsetTick, payload)) return;

        if (payload.Length < 3)
        {
            WarnMalformed(payload);
            return;
        }

        var typeChar = payload[0];
        var x = UgcPayload.Lane(payload[1]);
        var w = UgcPayload.Lane(payload[2]);
        if (w <= 0)
        {
            WarnMalformed(payload);
            return;
        }

        var absTick = _lastParentNote.Tick.Original + offsetTick;

        var child = CreateChild(typeChar, payload);

        if (child is null)
            return;

        child.Tick = absTick;
        child.Lane = x;
        child.Width = w;
        child.Timeline = _currentTimeline;
        _lastParentNote.AppendChild(child);
        _lastNote = child;
    }

    private umgr.Note? CreateChild(char typeChar, string payload)
    {
        umgr.Note? child = null;
        switch (typeChar)
        {
            case 's' when _lastParentNote is umgr.Hold:
                child = new umgr.HoldJoint();
                break;
            case 's' when _lastParentNote is umgr.Slide:
                child = new umgr.SlideJoint { Joint = Joint.D };
                break;
            case 'c' when _lastParentNote is umgr.Slide:
                child = new umgr.SlideJoint { Joint = Joint.C };
                break;
            case 's':
            case 'c':
                child = CreateAirChild(typeChar, payload);
                break;
        }

        return child;
    }

    private umgr.Note? CreateAirChild(char typeChar, string payload)
    {
        umgr.Note? child = null;
        if (_lastParentNote is umgr.AirHold)
        {
            child = new umgr.AirHoldJoint { Joint = typeChar == 'c' ? Joint.C : Joint.D };
        }
        else if (_lastParentNote is umgr.AirSlide airSlide)
        {
            if (payload.Length is not (3 or >= 5))
            {
                WarnMalformed(payload);
                return null;
            }

            var height = payload.Length == 3 ? airSlide.Height : UgcPayload.Height36(payload.AsSpan(3, 2));

            child = new umgr.AirSlideJoint
            {
                Joint = typeChar == 'c' ? Joint.C : Joint.D,
                Height = height
            };
        }
        else if (_lastParentNote is umgr.AirCrash)
        {
            if (typeChar != 'c' || payload.Length < 5)
            {
                WarnMalformed(payload);
                return null;
            }

            var height = UgcPayload.Height36(payload.AsSpan(3, 2));

            child = new umgr.AirCrashJoint { Height = height };
        }
        return child;
    }

    private void WarnMalformed(string what)
    {
        ReportAtCurrentLine(Severity.Warning, Msg.Create(MsgKeys.Mg_Unrecognized_note, what));
    }

    private void WarnUnknownType(char c)
    {
        ReportAtCurrentLine(Severity.Warning, Msg.Create(MsgKeys.Mg_Unrecognized_note, c.ToString()));
    }
}
