using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser.mgxc;

using umgr = Models.umgr;

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
    private umgr.Note? _lastNote;
    private umgr.Note? _lastParentNote;

    private void ParseNote(BinaryReader br)
    {
        var type = (NoteType)br.ReadSByte();
        var longAttr = (LongAttr)br.ReadSByte();
        var direction = (Direction)br.ReadSByte();
        var exAttr = (ExAttr)br.ReadSByte();
        var variationId = br.ReadSByte();
        var x = br.ReadSByte();
        var width = br.ReadInt16();
        var height = br.ReadInt32();
        var tick = br.ReadInt32();
        var timelineId = br.ReadInt32();
        var optionValue = type == NoteType.AirCrush && longAttr == LongAttr.Begin ? br.ReadInt32() : 0;

        umgr.Note? note = null;
        var isChildNote = false;
        var isPairNote = false;

        if (type == NoteType.Tap)
        {
            note = new umgr.Tap();
        }
        else if (type == NoteType.ExTap)
        {
            var exNote = new umgr.ExTap
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
            note = exNote;
        }
        else if (type == NoteType.Flick)
        {
            note = new umgr.Flick();
        }
        else if (type == NoteType.Damage)
        {
            note = new umgr.Damage();
        }
        else if (type == NoteType.Hold)
        {
            if (longAttr == LongAttr.Begin)
            {
                note = new umgr.Hold();
            }
            else if (longAttr == LongAttr.End)
            {
                note = new umgr.HoldJoint();
                isChildNote = true;
            }
            else
            {
                MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Invalid_joint_type_note, typeof(umgr.HoldJoint));
                Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick)
                {
                    Target = longAttr
                });
            }
        }
        else if (type == NoteType.Slide)
        {
            if (longAttr == LongAttr.Begin)
            {
                note = new umgr.Slide();
            }
            else
            {
                var exNote = new umgr.SlideJoint();
                if (longAttr is LongAttr.Step or LongAttr.End)
                {
                    exNote.Joint = Joint.D;
                }
                else if (longAttr is LongAttr.Control or LongAttr.EndNoAct or LongAttr.CurveControl)
                {
                    exNote.Joint = Joint.C;
                }
                else
                {
                    MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Invalid_joint_type_note, typeof(umgr.SlideJoint));
                    Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick)
                    {
                        Target = longAttr
                    });
                }

                note = exNote;
                isChildNote = true;
            }
        }
        else if (type == NoteType.Air)
        {
            var exNote = new umgr.Air();
            switch (direction)
            {
                case Direction.Up: exNote.Direction = AirDirection.IR; break;
                case Direction.Down: exNote.Direction = AirDirection.DW; break;
                case Direction.UpLeft: exNote.Direction = AirDirection.UL; break;
                case Direction.UpRight: exNote.Direction = AirDirection.UR; break;
                case Direction.DownLeft: exNote.Direction = AirDirection.DL; break;
                case Direction.DownRight: exNote.Direction = AirDirection.DR; break;
                default: exNote.Direction = AirDirection.IR; break;
            }

            exNote.Color = exAttr == ExAttr.Invert ? Color.PNK : Color.DEF;
            note = exNote;
            isPairNote = true;
        }
        else if (type is NoteType.AirHold or NoteType.AirSlide)
        {
            if (longAttr == LongAttr.Begin)
            {
                var exNote = new umgr.AirSlide();
                if (_lastNote is umgr.Air oldLastNote)
                {
                    _lastNote.Parent?.RemoveChild(_lastNote);
                    _lastNote = oldLastNote.PairNote;
                    exNote.Color = oldLastNote.Color;
                }

                exNote.Height = height;
                note = exNote;
                isPairNote = true;
            }
            else
            {
                var exNote = new umgr.AirSlideJoint();
                if (longAttr is LongAttr.Step or LongAttr.End)
                {
                    exNote.Joint = Joint.D;
                }
                else if (longAttr is LongAttr.Control or LongAttr.EndNoAct or LongAttr.CurveControl)
                {
                    exNote.Joint = Joint.C;
                }
                else
                {
                    MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Invalid_joint_type_note, typeof(umgr.AirSlideJoint));
                    Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick)
                    {
                        Target = longAttr
                    });
                }

                exNote.Height = height;
                note = exNote;
                isChildNote = true;
            }
        }
        else if (type == NoteType.AirCrush)
        {
            var color = variationId switch
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

            if (longAttr == LongAttr.Begin)
            {
                var exNote = new umgr.AirCrash
                {
                    Color = color,
                    Height = height,
                    Density = optionValue
                };
                note = exNote;
            }
            else
            {
                var exNote = new umgr.AirCrashJoint();
                if (longAttr is LongAttr.Step)
                {
                    MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Invalid_joint_type_note, typeof(umgr.AirCrashJoint));
                    Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick)
                    {
                        Target = longAttr
                    });
                }

                exNote.Height = height;
                note = exNote;
                isChildNote = true;
            }
        }
        else if (type == NoteType.Click)
        {
            return;
        }
        else if (type == NoteType.Last)
        {
            return;
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

        if (isChildNote) _lastParentNote?.AppendChild(note);
        else Mgxc.Notes.AppendChild(note);

        if (isPairNote)
            switch (_lastNote)
            {
                case umgr.PositiveNote lastP when note is umgr.NegativeNote newN:
                    lastP.MakePair(newN);
                    break;
                case umgr.NegativeNote lastN when note is umgr.PositiveNote newP:
                    lastN.MakePair(newP);
                    break;
                default:
                    throw new TimedDiagnosticException(MsgKeys.MgCrit_Pairing_notes_incompatible, note.Tick.Original,
                        new[]
                        {
                            note,
                            _lastNote
                        });
            }

        if (!isChildNote) _lastParentNote = note;

        _lastNote = note;
    }
}