using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser.mgxc;

internal enum NoteType : sbyte
{
    Unknown = 0x00,
    Tap = 0x01,
    ExTap = 0x02,
    Flick = 0x03,
    Damage = 0x04,
    Hold = 0x05,
    Slide = 0x06,
    Air = 0x07,
    AirHold = 0x08,
    AirSlide = 0x09,
    AirCrush = 0x0A,
    Click = 0x0B,
    Last = 0x0D
}

internal enum LongAttr : sbyte
{
    None = 0x00,
    Begin = 0x01,
    Step = 0x02,
    Control = 0x03,
    CurveControl = 0x04,
    End = 0x05,
    EndNoAct = 0x06
}

internal enum Direction : sbyte
{
    None = 0x00,
    Auto = 0x01,
    Up = 0x02,
    Down = 0x03,
    Center = 0x04,
    Left = 0x05,
    Right = 0x06,
    UpLeft = 0x07,
    UpRight = 0x08,
    DownLeft = 0x09,
    DownRight = 0x0A,
    RotateLeft = 0x0B,
    RotateRight = 0x0C,
    InOut = 0x0D,
    OutIn = 0x0E
}

internal enum ExAttr : sbyte
{
    None = 0x00,
    Invert = 0x01,
    HasNote = 0x02,
    ExJdg = 0x03
}

public partial class MgxcParser
{
    private UmgrModel.Note? _lastNote;
    private UmgrModel.Note? _lastParentNote;
    private bool _wholeSlideNoLine;

    private void ParseNote(BinaryReader br)
    {
        var recordStart = br.BaseStream.Position;
        var type = (NoteType)br.ReadSByte();
        var longAttr = (LongAttr)br.ReadSByte();
        var direction = (Direction)br.ReadSByte();
        var exAttr = (ExAttr)br.ReadSByte();
        var variationId = br.ReadSByte();
        var (x, width, height) = ReadNoteGeometry(br);
        var tick = br.ReadInt32();
        var timelineId = br.ReadInt32();
        var optionValue = type == NoteType.AirCrush && longAttr == LongAttr.Begin ? br.ReadInt32() : 0;

        UmgrModel.Note? note = null;
        var isChildNote = false;
        var isPairNote = false;
        switch (type)
        {
            case NoteType.Tap:
                note = new UmgrModel.Tap();
                break;
            case NoteType.ExTap:
                note = CreateExTap(direction);
                break;
            case NoteType.Flick:
                note = new UmgrModel.Flick
                {
                    AutoplayDirection = direction switch { Direction.Left => 'L', Direction.Right => 'R', _ => 'A' }
                };
                break;
            case NoteType.Damage:
                note = new UmgrModel.Damage();
                break;
            case NoteType.Hold:
                (note, isChildNote) = CreateHold(longAttr, tick);
                break;
            case NoteType.Slide:
                (note, isChildNote) = CreateSlide(longAttr, variationId, tick);
                break;
            case NoteType.Air:
                note = CreateAir(direction, exAttr);
                isPairNote = true;
                break;
            case NoteType.AirHold:
            case NoteType.AirSlide:
                (note, isChildNote, isPairNote) = CreateLongAir(type, longAttr, height, tick);
                break;
            case NoteType.AirCrush:
                (note, isChildNote) = CreateAirCrash(longAttr, variationId, height, optionValue, exAttr, direction, tick);
                break;
            case NoteType.Click:
                Mgxc.Extras.ClickTicks.Add(tick);
                return;
            case NoteType.Last:
                return;
            default:
                break;
        }

        if (note == null)
        {
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Unrecognized_note, (int)type);
            ReportAtPosition(Severity.Warning, msg, tick, br.BaseStream.Position, type);
            return;
        }

        note.Tick = tick;
        note.Lane = x;
        note.Width = width;
        note.Timeline = timelineId;

        if (isChildNote)
        {
            var validParent = (note, _lastParentNote) switch
            {
                (UmgrModel.HoldJoint, UmgrModel.Hold) => true,
                (UmgrModel.SlideJoint, UmgrModel.Slide) => true,
                (UmgrModel.AirHoldJoint, UmgrModel.AirHold) => true,
                (UmgrModel.AirSlideJoint, UmgrModel.AirSlide) => true,
                (UmgrModel.AirCrashJoint, UmgrModel.AirCrash) => true,
                _ => false
            };
            if (!validParent)
            {
                ThrowAtPosition(Msg.Key(MsgKeys.MgCrit_Pairing_notes_incompatible), br.BaseStream.Position, note, tick);
            }
            _lastParentNote!.AppendChild(note);
        }
        else
        {
            Mgxc.Notes.AppendChild(note);
        }

        if (isPairNote)
        {
            PairNote(note);
        }

        if (!isChildNote)
        {
            _lastParentNote = note;
        }

        if (type is NoteType.Slide or NoteType.AirSlide)
        {
            var end = br.BaseStream.Position;
            br.BaseStream.Position = recordStart + 8;
            var rawHeight = br.ReadInt32();
            br.BaseStream.Position = end;
            _curves.Observe(note, (int)type, (int)longAttr, rawHeight, _version, Mgxc.Meta.ExtendedField);
        }

        _lastNote = note;
    }

    private static UmgrModel.ExTap CreateExTap(Direction direction) => new()
    {
        Effect = direction switch
        {
            Direction.Up => ExEffect.UP,
            Direction.Down => ExEffect.DW,
            Direction.Center => ExEffect.CE,
            Direction.Left => ExEffect.LS,
            Direction.Right => ExEffect.RS,
            Direction.RotateLeft => ExEffect.LC,
            Direction.RotateRight => ExEffect.RC,
            Direction.InOut => ExEffect.BS,
            Direction.OutIn => ExEffect.CE,
            _ => ExEffect.UP
        }
    };

    private (UmgrModel.Note? Note, bool IsChild) CreateHold(LongAttr longAttr, int tick)
    {
        if (longAttr == LongAttr.Begin)
        {
            return (new UmgrModel.Hold(), false);
        }

        if (longAttr == LongAttr.End)
        {
            return (new UmgrModel.HoldJoint(), true);
        }

        ReportInvalidJoint(nameof(UmgrModel.HoldJoint), longAttr, tick);
        return (null, false);
    }

    private (UmgrModel.Note Note, bool IsChild) CreateSlide(LongAttr longAttr, sbyte variationId, int tick)
    {
        var noLine = variationId == 127 || (_version >= 3 && variationId == 35);
        Mgxc.Extras.SegmentSlideLines |= variationId == 127;
        if (longAttr == LongAttr.Begin)
        {
            _wholeSlideNoLine = _version >= 3 && variationId == 35;
            return (new UmgrModel.Slide { NoLine = noLine }, false);
        }

        return (new UmgrModel.SlideJoint
        {
            NoLine = _wholeSlideNoLine || variationId == 127,
            IsCurveControl = longAttr == LongAttr.CurveControl,
            Joint = ParseJoint(longAttr, nameof(UmgrModel.SlideJoint), tick)
        }, true);
    }

    private static UmgrModel.Air CreateAir(Direction direction, ExAttr exAttr) => new()
    {
        Direction = direction switch
        {
            Direction.Up => AirDirection.IR,
            Direction.Down => AirDirection.DW,
            Direction.UpLeft => AirDirection.UL,
            Direction.UpRight => AirDirection.UR,
            Direction.DownLeft => AirDirection.DL,
            Direction.DownRight => AirDirection.DR,
            _ => AirDirection.IR
        },
        Color = exAttr == ExAttr.Invert ? Color.PNK : Color.DEF
    };

    private (int X, int Width, decimal Height) ReadNoteGeometry(BinaryReader reader)
    {
        int x, width, height;
        if (_version >= 4)
        {
            reader.ReadByte(); // reserved
            width = reader.ReadByte();
            x = reader.ReadSByte();
            var native = reader.ReadInt32();
            height = UsesSignedHeight ? native : (ushort)native;
        }
        else if (_version == 3)
        {
            x = reader.ReadSByte();
            width = reader.ReadByte();
            reader.ReadByte(); // reserved
            height = reader.ReadUInt16();
            reader.ReadUInt16(); // reserved
        }
        else
        {
            x = reader.ReadSByte();
            width = reader.ReadInt16();
            height = reader.ReadInt32();
        }
        if (_version >= 4 && _heightScale != 10 && height / 36m >= _heightScale)
        {
            // Native v4 limits heights when rescaling, but leaves its usual
            // HSCL=10 representation untouched (including heights above 360).
            // Check the limit first so small scales cannot overflow division.
            return (x, width, 360);
        }
        try
        {
            return (x, width, height * 10m / _heightScale);
        }
        catch (OverflowException)
        {
            throw new LocationDiagnosticException(Msg.Create(MsgKeys.MgCrit_Invalid_height_scale, _heightScale),
                checked((int)reader.BaseStream.Position), Path);
        }
    }

    private (UmgrModel.Note Note, bool IsChild, bool IsPair) CreateLongAir(
        NoteType type, LongAttr longAttr, decimal height, int tick)
    {
        if (longAttr == LongAttr.Begin)
        {
            // Invert is set on the AIR arrow and applies to the whole long note.
            return (type == NoteType.AirHold ? CreateAirHold() : CreateAirSlide(height), false, true);
        }
        if (type == NoteType.AirHold)
        {
            return (new UmgrModel.AirHoldJoint
            {
                Joint = ParseJoint(longAttr, nameof(UmgrModel.AirHoldJoint), tick)
            }, true, false);
        }

        return (new UmgrModel.AirSlideJoint
        {
            IsCurveControl = longAttr == LongAttr.CurveControl,
            Joint = ParseJoint(longAttr, nameof(UmgrModel.AirSlideJoint), tick),
            Height = height
        }, true, false);
    }

    private UmgrModel.Air? TakeLastAir()
    {
        if (_lastNote is not UmgrModel.Air air)
        {
            return null;
        }

        air.Parent?.RemoveChild(air);
        _lastNote = air.PairNote;
        return air;
    }

    private UmgrModel.AirHold CreateAirHold()
    {
        var note = new UmgrModel.AirHold();
        if (TakeLastAir() is { } air)
        {
            note.Color = air.Color;
            note.Direction = air.Direction;
        }
        return note;
    }

    private UmgrModel.AirSlide CreateAirSlide(decimal height)
    {
        var note = new UmgrModel.AirSlide { Height = height };
        if (TakeLastAir() is { } air)
        {
            note.Color = air.Color;
            note.Direction = air.Direction;
        }
        return note;
    }

    private Joint ParseJoint(LongAttr longAttr, string noteName, int tick)
    {
        if (longAttr is LongAttr.Step or LongAttr.End)
        {
            return Joint.D;
        }

        if (longAttr is LongAttr.Control or LongAttr.EndNoAct or LongAttr.CurveControl)
        {
            return Joint.C;
        }

        ReportInvalidJoint(noteName, longAttr, tick);
        return Joint.D;
    }

    private void ReportInvalidJoint(string noteName, LongAttr longAttr, int tick)
    {
        MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Invalid_joint_type_note, noteName);
        Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick) { Target = longAttr });
    }

    private (UmgrModel.Note Note, bool IsChild) CreateAirCrash(
        LongAttr longAttr, sbyte variationId, decimal height, int optionValue, ExAttr exAttr, Direction direction, int tick)
    {
        if (longAttr == LongAttr.Begin)
        {
            return (new UmgrModel.AirCrash
            {
                Color = AirCrashColor(variationId),
                Height = height,
                Density = optionValue,
                HasExplicitJudgements = optionValue == 0 && (exAttr & ExAttr.HasNote) != 0,
                Attr = _version >= 3 ? (int)exAttr switch
                {
                    4 => AirLadderAttr.AxisY,
                    5 => AirLadderAttr.AxisZ,
                    _ => AirLadderAttr.DEF
                } : direction switch
                {
                    Direction.RotateLeft => AirLadderAttr.AxisY,
                    Direction.RotateRight => AirLadderAttr.AxisZ,
                    _ => AirLadderAttr.DEF
                }
            }, false);
        }

        if (longAttr == LongAttr.Step && _lastParentNote is not UmgrModel.AirCrash { HasExplicitJudgements: true })
        {
            ReportInvalidJoint(nameof(UmgrModel.AirCrashJoint), longAttr, tick);
        }

        return (new UmgrModel.AirCrashJoint
        {
            Height = height,
            IsJudgement = longAttr == LongAttr.Step || (longAttr == LongAttr.End && (exAttr & ExAttr.HasNote) != 0)
        }, true);
    }

    private static Color AirCrashColor(sbyte variationId) => variationId switch
    {
        0 => Color.DEF,
        1 => Color.RED, // Red
        2 => Color.ORN, // Orange
        3 => Color.YEL, // Yellow
        4 => Color.GRN, // Green
        5 => Color.AQA, // Sky
        6 => Color.BLU, // Blue
        7 => Color.PPL, // Violet
        8 => Color.VLT, // Pink
        9 => Color.PPL, // Violet
        10 => Color.GRY, // White
        11 => Color.BLK, // Black
        12 => Color.LIM, // Grass
        13 => Color.CYN, // Sky Blue
        14 => Color.DGR, // Cobalt Blue
        15 => Color.PNK, // Purple
        35 => Color.NON, // Transparent
        _ => Color.DEF
    };

    private void PairNote(UmgrModel.Note note)
    {
        switch (_lastNote)
        {
            case UmgrModel.PositiveNote lastP when note is UmgrModel.NegativeNote newN:
                lastP.MakePair(newN);
                break;
            case UmgrModel.NegativeNote lastN when note is UmgrModel.PositiveNote newP:
                lastN.MakePair(newP);
                break;
            default:
                throw new TimedDiagnosticException(MsgKeys.MgCrit_Pairing_notes_incompatible, note.Tick.Original,
                    new[] { note, _lastNote });
        }
    }
}
