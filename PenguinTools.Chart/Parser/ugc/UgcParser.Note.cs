using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser.ugc;

using umgr = Models.umgr;

public partial class UgcParser
{
    private void DispatchBodyLine(string line)
    {
        if (!line.StartsWith('#')) return;
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
            _lastNote = null;
            return;
        }

        if (payload.Length < 3)
        {
            WarnMalformed(payload);
            return;
        }

        var typeChar = payload[0];
        var x = UgcPayload.Base36(payload[1]);
        var w = UgcPayload.Base36(payload[2]);
        if (x < 0 || w < 0)
        {
            WarnMalformed(payload);
            return;
        }

        var extras = payload.Length > 3 ? payload[3..] : string.Empty;

        if (typeChar == 'a')
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
                Timeline = _currentTimeline
            };

            Ugc.Notes.AppendChild(air);

            var pairPositive = FindPairPositive(absTick);
            if (pairPositive != null)
                pairPositive.MakePair(air);
            else
                ReportAtCurrentLine(Severity.Warning, Msg.Key(MsgKeys.MgCrit_Pairing_notes_incompatible));

            _lastNote = air;
            return;
        }

        if (typeChar == 'H')
        {
            var color = ParseAirHoldColor(extras);
            var airHold = new umgr.AirHold
            {
                Color = color,
                Timeline = _currentTimeline
            };
            Ugc.Notes.AppendChild(airHold);

            if (_lastNote is umgr.Air oldAir && oldAir.Tick.Original == absTick)
            {
                oldAir.Parent?.RemoveChild(oldAir);
                _lastNote = oldAir.PairNote;
            }

            var pairPositive = FindPairPositive(absTick);
            if (pairPositive != null)
                pairPositive.MakePair(airHold);

            _lastParentNote = airHold;
            _lastNote = airHold;
            return;
        }

        if (typeChar == 'S')
        {
            if (extras.Length < 3)
            {
                WarnMalformed(payload);
                return;
            }

            var height = UgcPayload.Height36(extras.AsSpan(0, 2));
            if (height < 0)
            {
                WarnMalformed(payload);
                return;
            }

            var airSlide = new umgr.AirSlide
            {
                Height = height,
                Color = UgcPayload.AirColorChar(extras[2]),
                Timeline = _currentTimeline
            };
            Ugc.Notes.AppendChild(airSlide);

            if (_lastNote is umgr.Air oldAir && oldAir.Tick.Original == absTick)
            {
                oldAir.Parent?.RemoveChild(oldAir);
                _lastNote = oldAir.PairNote;
            }

            var pairPositive = FindPairPositive(absTick);
            if (pairPositive != null)
                pairPositive.MakePair(airSlide);

            _lastParentNote = airSlide;
            _lastNote = airSlide;
            return;
        }

        if (typeChar == 'C')
        {
            if (extras.Length < 3)
            {
                WarnMalformed(payload);
                return;
            }

            var height = UgcPayload.Height36(extras.AsSpan(0, 2));
            if (height < 0)
            {
                WarnMalformed(payload);
                return;
            }

            var crash = new umgr.AirCrash
            {
                Color = UgcPayload.CrushColorChar(extras[2]),
                Height = height,
                Density = UgcPayload.AirCrashInterval(suffix),
                Tick = absTick,
                Lane = x,
                Width = w,
                Timeline = _currentTimeline
            };
            Ugc.Notes.AppendChild(crash);
            _lastParentNote = crash;
            _lastNote = crash;
            return;
        }

        var note = typeChar switch
        {
            't' => new umgr.Tap(),
            'x' => MakeExTap(extras),
            'f' => new umgr.Flick(),
            'd' => new umgr.Damage(),
            _ => HandleLongNoteParent(typeChar, extras, suffix)
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

    // Last PositiveNote at absTick when _lastNote is a non-positive long parent (Hold/Slide).
    private umgr.PositiveNote? FindPairPositive(int absTick)
    {
        if (_lastNote is umgr.PositiveNote lastP && lastP.Tick.Original == absTick)
            return lastP;

        return Ugc.Notes.Children.OfType<umgr.PositiveNote>().LastOrDefault(p => p.Tick.Original == absTick);
    }

    private static umgr.ExTap MakeExTap(string extras)
    {
        var exNote = new umgr.ExTap
        {
            Effect = extras.Length >= 1 ? UgcPayload.ExEffectChar(extras[0]) : ExEffect.UP
        };
        return exNote;
    }

    private umgr.Note? HandleLongNoteParent(char typeChar, string extras, string suffix)
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

    private void HandleChildPayload(int offsetTick, string payload)
    {
        if (_lastParentNote is null)
        {
            WarnMalformed(payload);
            return;
        }

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
            return;
        }

        if (payload.Length == 1 && _lastParentNote is umgr.AirHold airHold)
        {
            if (payload[0] is not ('s' or 'c'))
            {
                WarnMalformed(payload);
                return;
            }

            var joint = new umgr.AirHoldJoint
            {
                Tick = airHold.Tick.Original + offsetTick,
                Timeline = _currentTimeline,
                Joint = payload[0] == 'c' ? Joint.C : Joint.D
            };
            airHold.AppendChild(joint);
            _lastNote = joint;
            return;
        }

        if (payload.Length == 1 && _lastParentNote is umgr.AirSlide airSlide)
        {
            if (payload[0] is not ('s' or 'c'))
            {
                WarnMalformed(payload);
                return;
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
            return;
        }

        if (payload.Length < 3)
        {
            WarnMalformed(payload);
            return;
        }

        var typeChar = payload[0];
        var x = UgcPayload.Base36(payload[1]);
        var w = UgcPayload.Base36(payload[2]);
        if (x < 0 || w < 0)
        {
            WarnMalformed(payload);
            return;
        }

        var absTick = _lastParentNote.Tick.Original + offsetTick;

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
                if (_lastParentNote is umgr.AirHold)
                {
                    child = new umgr.AirHoldJoint { Joint = typeChar == 'c' ? Joint.C : Joint.D };
                }
                else if (_lastParentNote is umgr.AirSlide)
                {
                    if (payload.Length < 5)
                    {
                        WarnMalformed(payload);
                        return;
                    }

                    var height = UgcPayload.Height36(payload.AsSpan(3, 2));
                    if (height < 0)
                    {
                        WarnMalformed(payload);
                        return;
                    }

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
                        return;
                    }

                    var height = UgcPayload.Height36(payload.AsSpan(3, 2));
                    if (height < 0)
                    {
                        WarnMalformed(payload);
                        return;
                    }

                    child = new umgr.AirCrashJoint { Height = height };
                }

                break;
        }

        if (child is null) return;

        child.Tick = absTick;
        child.Lane = x;
        child.Width = w;
        child.Timeline = _currentTimeline;
        _lastParentNote.AppendChild(child);
        _lastNote = child;
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