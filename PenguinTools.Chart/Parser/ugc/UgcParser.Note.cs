using System.Globalization;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser.ugc;

public partial class UgcParser
{
    private void DispatchBodyLine(string line)
    {
        if (!line.StartsWith('#'))
        {
            return;
        }

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

        var (payloadStr, suffixStr) = SplitParentPayload(rest[(parentIdx + 1)..].ToString());
        // Older extended v8 exports appended the interval after color/axis.
        // An explicit comma remains authoritative, including an empty suffix.
        var colorEnd = 4 + HeightDigits;
        if (_version == 8 && suffixStr.Length == 0 && !rest.Contains(',') &&
            payloadStr.Length > colorEnd && payloadStr[0] == 'C')
        {
            var intervalIndex = payloadStr[colorEnd] is 'Y' or 'Z' ? colorEnd + 1 : colorEnd;
            var interval = payloadStr.AsSpan(intervalIndex);
            if (interval.SequenceEqual("$") || int.TryParse(interval, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                suffixStr = interval.ToString();
                payloadStr = payloadStr[..intervalIndex];
            }
        }
        var absTick = BarTickToAbsTick(bar, tick);
        HandleParentPayload(absTick, payloadStr, suffixStr);
    }

    private static (string Payload, string Suffix) SplitParentPayload(string rhs)
    {
        var commaIdx = rhs.IndexOf(',');
        var payloadStr = commaIdx >= 0 ? rhs[..commaIdx] : rhs;
        var suffixStr = commaIdx >= 0 ? rhs[(commaIdx + 1)..] : string.Empty;

        return (payloadStr, suffixStr);
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
        var x = Lane(payload[1]);
        var w = Width(payload[2]);
        if (w <= 0)
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.MgCrit_Invalid_note_width, w));
        }

        var extras = payload.Length > 3 ? payload[3..] : string.Empty;

        if (typeChar == 'a')
        {
            HandleAirParent(absTick, x, w, extras, payload);
            return;
        }

        if (typeChar == 'H')
        {
            HandleAirHoldParent(absTick, x, w, extras);
            return;
        }

        if (typeChar == 'S')
        {
            HandleAirSlideParent(absTick, x, w, extras, payload);
            return;
        }

        if (typeChar == 'C')
        {
            HandleAirCrashParent(absTick, x, w, extras, payload, suffix);
            return;
        }

        var note = CreateGroundParent(typeChar, extras);

        if (note is null)
        {
            WarnUnknownType(typeChar);
            return;
        }

        note.Tick = absTick;
        note.Lane = x;
        note.Width = w;
        note.Timeline = _currentTimeline;
        if (_version >= 9 && note is UmgrModel.Slide slide)
        {
            slide.NoLine = extras.StartsWith('Z');
        }

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

        var air = new UmgrModel.Air
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
        {
            pairPositive.MakePair(air);
        }
        else
        {
            ReportAtCurrentLine(Severity.Warning, Msg.Key(MsgKeys.MgCrit_Pairing_notes_incompatible));
        }

        _lastNote = air;
    }

    private void HandleAirHoldParent(int absTick, int x, int w, string extras)
    {
        var color = ParseAirHoldColor(extras);
        var airHold = new UmgrModel.AirHold
        {
            Color = color,
            Timeline = _currentTimeline,
            Tick = absTick,
            Lane = x,
            Width = w
        };
        Ugc.Notes.AppendChild(airHold);

        if (_lastNote is UmgrModel.Air oldAir && oldAir.Tick.Original == absTick &&
            oldAir.Lane == x && oldAir.Width == w)
        {
            airHold.Direction = oldAir.Direction;
            airHold.Color = oldAir.Color;
            oldAir.Parent?.RemoveChild(oldAir);
            _lastNote = oldAir.PairNote;
        }

        var pairPositive = FindPairPositive(absTick, x, w);
        if (pairPositive != null)
        {
            pairPositive.MakePair(airHold);
        }

        _lastParentNote = airHold;
        _lastNote = airHold;
    }

    private void HandleAirSlideParent(int absTick, int x, int w, string extras, string payload)
    {
        if (extras.Length < HeightDigits + 1)
        {
            WarnMalformed(payload);
            return;
        }

        var height = Height(extras.AsSpan(0, HeightDigits));

        var airSlide = new UmgrModel.AirSlide
        {
            Height = height,
            Color = UgcPayload.AirColorChar(extras[HeightDigits]),
            Timeline = _currentTimeline,
            Tick = absTick,
            Lane = x,
            Width = w
        };
        Ugc.Notes.AppendChild(airSlide);

        if (_lastNote is UmgrModel.Air oldAir && oldAir.Tick.Original == absTick &&
            oldAir.Lane == x && oldAir.Width == w)
        {
            airSlide.Direction = oldAir.Direction;
            airSlide.Color = oldAir.Color;
            oldAir.Parent?.RemoveChild(oldAir);
            _lastNote = oldAir.PairNote;
        }

        var pairPositive = FindPairPositive(absTick, x, w);
        if (pairPositive != null)
        {
            pairPositive.MakePair(airSlide);
        }

        _lastParentNote = airSlide;
        _lastNote = airSlide;
    }

    private void HandleAirCrashParent(int absTick, int x, int w, string extras, string payload, string suffix)
    {
        if (extras.Length < HeightDigits + 1)
        {
            WarnMalformed(payload);
            return;
        }

        var height = Height(extras.AsSpan(0, HeightDigits));

        var crash = new UmgrModel.AirCrash
        {
            Color = UgcPayload.CrushColorChar(extras[HeightDigits]),
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
    private UmgrModel.PositiveNote? FindPairPositive(int absTick, int lane, int width)
    {
        if (_lastNote is UmgrModel.PositiveNote lastP && lastP.Tick.Original == absTick && lastP.Lane == lane && lastP.Width == width)
        {
            return lastP;
        }

        return Ugc.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children)).OfType<UmgrModel.PositiveNote>().LastOrDefault(p => p.Tick.Original == absTick && p.Lane == lane && p.Width == width);
    }

    private AirLadderAttr ParseAirLadderAttribute(string extras)
    {
        var axisIndex = HeightDigits + 1;
        if (extras.Length <= axisIndex)
        {
            return AirLadderAttr.DEF;
        }

        return extras[axisIndex] switch
        {
            'Y' => AirLadderAttr.AxisY,
            'Z' => AirLadderAttr.AxisZ,
            _ => AirLadderAttr.DEF
        };
    }

    private static UmgrModel.ExTap MakeExTap(string extras)
    {
        var exNote = new UmgrModel.ExTap
        {
            Effect = extras.Length >= 1 ? UgcPayload.ExEffectChar(extras[0]) : ExEffect.UP
        };
        return exNote;
    }

    private static UmgrModel.Note? CreateGroundParent(char typeChar, string extras)
    {
        return typeChar switch
        {
            't' => new UmgrModel.Tap(),
            'x' => MakeExTap(extras),
            'f' => new UmgrModel.Flick
            {
                AutoplayDirection = extras.Length > 0 && extras[0] is 'L' or 'R' ? extras[0] : 'A'
            },
            'd' => new UmgrModel.Damage(),
            'h' => new UmgrModel.Hold(),
            's' => new UmgrModel.Slide(),
            _ => null
        };
    }

    private Color ParseAirHoldColor(string extras)
    {
        // Keep the documented short form used by native exporters. Older
        // high-precision v4-v8 readers also accept an explicit zero padding
        // character; importing that compatibility form must retain its color.
        var index = _version < 9 && _highPrecision && extras is "0I" or "0N" ? 1 : 0;
        return extras.Length > index ? UgcPayload.AirColorChar(extras[index]) : Color.DEF;
    }

    private bool HandleCompactChild(int offsetTick, string payload)
    {
        if (payload.Trim() == "s" && _lastParentNote is UmgrModel.Hold hold)
        {
            var hj = new UmgrModel.HoldJoint
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

        if (payload.Length == 1 && _lastParentNote is UmgrModel.AirHold airHold)
        {
            AppendCompactAirHold(offsetTick, payload, airHold);
            return true;
        }

        if (payload.Length == 1 && _lastParentNote is UmgrModel.AirSlide airSlide)
        {
            AppendCompactAirSlide(offsetTick, payload, airSlide);
            return true;
        }

        return false;
    }

    private void AppendCompactAirHold(int offsetTick, string payload, UmgrModel.AirHold airHold)
    {
        if (payload[0] is not ('s' or 'c'))
        {
            WarnMalformed(payload);
            return;
        }

        var joint = new UmgrModel.AirHoldJoint
        {
            Tick = airHold.Tick.Original + offsetTick,
            Timeline = _currentTimeline,
            Joint = payload[0] == 'c' ? Joint.C : Joint.D
        };
        airHold.AppendChild(joint);
        _lastNote = joint;
    }

    private void AppendCompactAirSlide(int offsetTick, string payload, UmgrModel.AirSlide airSlide)
    {
        if (payload[0] is not ('s' or 'c'))
        {
            WarnMalformed(payload);
            return;
        }

        var joint = new UmgrModel.AirSlideJoint
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
    }

    private void HandleChildPayload(int offsetTick, string payload)
    {
        offsetTick = ScaleTick(offsetTick);
        if (_lastParentNote is null)
        {
            if (_version >= 9)
            {
                ThrowAtCurrentLine(Msg.Key(MsgKeys.MgCrit_Pairing_notes_incompatible));
            }

            WarnMalformed(payload);
            return;
        }

        if (HandleCompactChild(offsetTick, payload))
        {
            return;
        }

        if (payload.Length < 3)
        {
            WarnMalformed(payload);
            return;
        }

        var typeChar = payload[0];
        var x = Lane(payload[1]);
        var w = Width(payload[2]);
        var clippedControl = _version >= 9 && typeChar == 'c' && _lastParentNote is UmgrModel.Slide or UmgrModel.AirSlide;
        var hiddenSegment = _version == 8 && typeChar is 'n' or 'N' or 'V' && _lastParentNote is UmgrModel.Slide;
        if (hiddenSegment)
        {
            typeChar = typeChar == 'N' ? 'c' : typeChar == 'V' ? 'v' : 's';
            Ugc.Extras.SegmentSlideLines = true;
        }
        if (w < 0 || (w == 0 && !clippedControl))
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.MgCrit_Invalid_note_width, w));
        }

        var absTick = _lastParentNote.Tick.Original + offsetTick;

        var child = CreateChild(typeChar, payload);

        if (child is null)
        {
            if (_version >= 9)
            {
                ThrowAtCurrentLine(Msg.Key(MsgKeys.MgCrit_Pairing_notes_incompatible));
            }

            return;
        }

        child.Tick = absTick;
        child.Lane = x;
        child.Width = w;
        child.Timeline = _currentTimeline;
        if (_version == 8 && _lastParentNote is UmgrModel.Slide legacySlide)
        {
            if (legacySlide.LastChild is UmgrModel.SlideJoint previous)
            {
                previous.NoLine = hiddenSegment;
            }
            else
            {
                legacySlide.NoLine = hiddenSegment;
            }
        }
        if (_version >= 9 && child is UmgrModel.SlideJoint joint && _lastParentNote is UmgrModel.Slide slide)
        {
            joint.NoLine = slide.NoLine;
        }
        _lastParentNote.AppendChild(child);
        _lastNote = child;
    }

    private UmgrModel.Note? CreateChild(char typeChar, string payload)
    {
        UmgrModel.Note? child = null;
        switch (typeChar)
        {
            case 's' when _lastParentNote is UmgrModel.Hold:
                child = new UmgrModel.HoldJoint();
                break;
            case 's' when _lastParentNote is UmgrModel.Slide:
                child = new UmgrModel.SlideJoint { Joint = Joint.D };
                break;
            case 'c' when _lastParentNote is UmgrModel.Slide:
                child = new UmgrModel.SlideJoint { Joint = Joint.C };
                break;
            case 'v' when _lastParentNote is UmgrModel.Slide:
                child = new UmgrModel.SlideJoint { Joint = Joint.C, IsCurveControl = true, CurveInterpretation = UmgrModel.CurveInterpretation.Legacy };
                break;
            case 's':
            case 'c':
            case 'v':
                child = CreateAirChild(typeChar, payload);
                break;
        }

        return child;
    }

    private UmgrModel.Note? CreateAirChild(char typeChar, string payload)
    {
        UmgrModel.Note? child = null;
        if (_lastParentNote is UmgrModel.AirHold && typeChar != 'v')
        {
            child = new UmgrModel.AirHoldJoint { Joint = typeChar == 'c' ? Joint.C : Joint.D };
        }
        else if (_lastParentNote is UmgrModel.AirSlide airSlide)
        {
            if (payload.Length != 3 && payload.Length < 3 + HeightDigits)
            {
                WarnMalformed(payload);
                return null;
            }

            var height = payload.Length == 3 ? airSlide.Height : Height(payload.AsSpan(3, HeightDigits));

            child = new UmgrModel.AirSlideJoint
            {
                Joint = typeChar is 'c' or 'v' ? Joint.C : Joint.D,
                IsCurveControl = typeChar == 'v',
                CurveInterpretation = typeChar == 'v' ? UmgrModel.CurveInterpretation.Legacy : UmgrModel.CurveInterpretation.FormatDefault,
                Height = height
            };
        }
        else if (_lastParentNote is UmgrModel.AirCrash)
        {
            if (typeChar != 'c' || payload.Length < 3 + HeightDigits)
            {
                WarnMalformed(payload);
                return null;
            }

            var height = Height(payload.AsSpan(3, HeightDigits));

            child = new UmgrModel.AirCrashJoint { Height = height };
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
