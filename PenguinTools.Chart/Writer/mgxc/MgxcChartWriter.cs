using System.Globalization;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core;
using PenguinTools.Core.IO;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Writer.mgxc;

using umgr = Models.umgr;

/// <summary>Writes Magrete binary MGXC (version 2).</summary>
public sealed class MgxcChartWriter(MgxcWriteRequest request)
{
    private const int Version = 2;
    private const int DefaultHeight = 80;
    private const sbyte NoLineVariation = 0x7F;

    private readonly umgr.Chart _chart = request.Chart ?? throw new ArgumentNullException(nameof(request));

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        await AtomicFile.WriteAsync(request.Path, async (stream, token) =>
        {
            await using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                WriteFile(writer);
                writer.Flush();
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(stream, token);
            await stream.FlushAsync(token);
        }, ct);
        return OperationResult.Success();
    }

    private void WriteFile(BinaryWriter bw)
    {
        bw.Write(Encoding.ASCII.GetBytes("MGXC"));
        var sizePos = bw.BaseStream.Position;
        bw.Write(0);
        bw.Write(Version);

        WriteBlock(bw, "meta", WriteMeta);
        WriteBlock(bw, "evnt", WriteEvents);
        WriteBlock(bw, "dat2", WriteNotes);

        var end = bw.BaseStream.Position;
        bw.BaseStream.Position = sizePos;
        bw.Write(checked((int)(end - 8)));
        bw.BaseStream.Position = end;
    }

    private static void WriteBlock(BinaryWriter bw, string name, Action<BinaryWriter> write)
    {
        bw.Write(Encoding.ASCII.GetBytes(name));
        var sizePos = bw.BaseStream.Position;
        bw.Write(0);
        var start = bw.BaseStream.Position;
        write(bw);
        var end = bw.BaseStream.Position;
        bw.BaseStream.Position = sizePos;
        bw.Write(checked((int)(end - start)));
        bw.BaseStream.Position = end;
    }

    private void WriteMeta(BinaryWriter bw)
    {
        var m = _chart.Meta;
        WriteStringField(bw, "titl", m.Title);
        WriteStringField(bw, "sort", m.SortName);
        WriteStringField(bw, "arts", m.Artist);
        WriteStringField(bw, "genr", m.Genre?.Str ?? "");
        WriteStringField(bw, "dsgn", m.Designer);
        WriteIntField(bw, "diff", DifficultyValue(m.Difficulty));
        WriteStringField(bw, "plvl", m.Difficulty == Difficulty.WorldsEnd
            ? WeLevel(m.WeDifficulty)
            : FormatPlayLevel(m.Level));
        WriteStringField(bw, "weat", m.WeTag?.Str ?? "");
        WriteDoubleField(bw, "cnst", m.Difficulty == Difficulty.WorldsEnd ? 0 : (double)m.Level);
        WriteStringField(bw, "sgid", string.IsNullOrWhiteSpace(m.MgxcId)
            ? m.Id?.ToString() ?? ""
            : m.MgxcId);
        WriteStringField(bw, "wvfn", m.BgmFilePath);
        WriteDoubleField(bw, "wvof", (double)m.BgmManualOffset);
        WriteDoubleField(bw, "wvp0", (double)m.BgmPreviewStart);
        WriteDoubleField(bw, "wvp1", (double)m.BgmPreviewStop);
        WriteStringField(bw, "jack", m.JacketFilePath);
        WriteStringField(bw, "bgfn", m.IsCustomStage ? m.BgiFilePath : "");
        WriteStringField(bw, "bgsc", "");
        WriteIntField(bw, "bgsy", 1);
        WriteStringField(bw, "flcl", "");
        WriteIntField(bw, "flcx", -1);
        WriteStringField(bw, "flbg", "");
        WriteStringField(bw, "flsc", "");
        WriteIntField(bw, "mtil", m.MainTil);
        WriteDoubleField(bw, "mbpm", (double)m.MainBpm);
        WriteIntField(bw, "ttrl", 0);
        WriteIntField(bw, "sofs", m.BgmEnableBarOffset ? 1 : 0);
        WriteIntField(bw, "uclk", 1);
        WriteIntField(bw, "xlng", 1);
        WriteIntField(bw, "bgmw", 0);
        WriteStringField(bw, "atls", "");
        WriteStringField(bw, "atst", "");
        WriteStringField(bw, "durl", "");
        WriteStringField(bw, "lcpy", "");
        WriteStringField(bw, "ltyp", "");
        WriteStringField(bw, "lurl", "");
        WriteIntField(bw, "xver", 1);
        WriteStringField(bw, "cmmt", m.Comment);
        WriteIntField(bw, "CTCK", 0);
        WriteStringField(bw, "LXFN", "");
        WriteDoubleField(bw, "HSCL", 10);
        bw.Write(0);
        bw.Write((short)0);
        bw.Write((short)0);
    }

    private void WriteEvents(BinaryWriter bw)
    {
        var beats = _chart.Events.Children.OfType<umgr.BeatEvent>()
            .Where(x => x.Numerator > 0 && x.Denominator > 0)
            .OrderBy(x => x.Bar).ToArray();
        if (beats.Length == 0)
            beats = [new umgr.BeatEvent { Bar = 0, Tick = 0, Numerator = 4, Denominator = 4 }];

        foreach (var beat in beats)
        {
            bw.Write(Encoding.ASCII.GetBytes("beat"));
            WriteIntFieldValue(bw, beat.Bar);
            WriteIntFieldValue(bw, beat.Numerator);
            WriteIntFieldValue(bw, beat.Denominator);
            bw.Write(0);
        }

        foreach (var bpm in _chart.Events.Children.OfType<umgr.BpmEvent>().OrderBy(x => x.Tick).DefaultIfEmpty(
                     new umgr.BpmEvent { Tick = 0, Bpm = _chart.Meta.MainBpm > 0 ? _chart.Meta.MainBpm : 120m }))
        {
            bw.Write(Encoding.ASCII.GetBytes("bpm "));
            WriteIntFieldValue(bw, bpm.Tick.Original);
            WriteDoubleFieldValue(bw, (double)bpm.Bpm);
            bw.Write(0);
        }

        foreach (var speed in _chart.Events.Children.OfType<umgr.NoteSpeedEvent>().OrderBy(x => x.Tick))
        {
            bw.Write(Encoding.ASCII.GetBytes("smod"));
            WriteIntFieldValue(bw, speed.Tick.Original);
            WriteDoubleFieldValue(bw, (double)speed.Speed);
            bw.Write(0);
        }

        foreach (var til in _chart.Events.Children.OfType<umgr.ScrollSpeedEvent>()
                     .OrderBy(x => x.Timeline).ThenBy(x => x.Tick))
        {
            bw.Write(Encoding.ASCII.GetBytes("til "));
            WriteIntFieldValue(bw, til.Timeline);
            WriteIntFieldValue(bw, til.Tick.Original);
            WriteDoubleFieldValue(bw, (double)til.Speed);
            bw.Write(0);
        }
    }

    private void WriteNotes(BinaryWriter bw)
    {
        foreach (var note in _chart.Notes.Children)
        {
            switch (note)
            {
                case umgr.Tap:
                    WriteNote(bw, NoteType.Tap, LongAttr.None, Direction.None, ExAttr.None, 0,
                        note.Lane, note.Width, DefaultHeight, note.Tick.Original, note.Timeline);
                    break;
                case umgr.ExTap ex:
                    WriteNote(bw, NoteType.ExTap, LongAttr.None, EffectDirection(ex.Effect), ExAttr.None, 0,
                        note.Lane, note.Width, DefaultHeight, note.Tick.Original, note.Timeline);
                    break;
                case umgr.Flick:
                    WriteNote(bw, NoteType.Flick, LongAttr.None, Direction.None, ExAttr.None, 0,
                        note.Lane, note.Width, DefaultHeight, note.Tick.Original, note.Timeline);
                    break;
                case umgr.Damage:
                    WriteNote(bw, NoteType.Damage, LongAttr.None, Direction.None, ExAttr.None, 0,
                        note.Lane, note.Width, DefaultHeight, note.Tick.Original, note.Timeline);
                    break;
                case umgr.Hold hold:
                    WriteExCarrierIfNeeded(bw, hold);
                    WriteNote(bw, NoteType.Hold, LongAttr.Begin, Direction.None, ExAttr.None, 0,
                        hold.Lane, hold.Width, DefaultHeight, hold.Tick.Original, hold.Timeline);
                    foreach (var joint in hold.Children.OfType<umgr.HoldJoint>())
                        WriteNote(bw, NoteType.Hold, LongAttr.End, Direction.None, ExAttr.None, 0,
                            hold.Lane, hold.Width, DefaultHeight, joint.Tick.Original, joint.Timeline);
                    break;
                case umgr.Slide slide:
                    WriteExCarrierIfNeeded(bw, slide);
                    WriteNote(bw, NoteType.Slide, LongAttr.Begin, Direction.None, ExAttr.None,
                        slide.NoLine ? NoLineVariation : (sbyte)0,
                        slide.Lane, slide.Width, DefaultHeight, slide.Tick.Original, slide.Timeline);
                    var joints = slide.Children.OfType<umgr.SlideJoint>().ToArray();
                    for (var i = 0; i < joints.Length; i++)
                    {
                        var joint = joints[i];
                        var isLast = i == joints.Length - 1;
                        WriteNote(bw, NoteType.Slide, SlideAttr(joint.Joint, isLast), Direction.None, ExAttr.None,
                            joint.NoLine ? NoLineVariation : (sbyte)0,
                            joint.Lane, joint.Width, DefaultHeight, joint.Tick.Original, joint.Timeline);
                    }
                    break;
                case umgr.Air air:
                    if (HasAirActionAt(air)) break;
                    WriteNote(bw, NoteType.Air, LongAttr.None, AirDir(air.Direction), AirEx(air.Color), 0,
                        air.Lane, air.Width, DefaultHeight, air.Tick.Original, air.Timeline);
                    break;
                case umgr.AirHold airHold:
                    WriteAirBase(bw, airHold.Direction, airHold.Color, airHold);
                    WriteNote(bw, NoteType.AirHold, LongAttr.Begin, Direction.None, ExAttr.None, 0,
                        airHold.Lane, airHold.Width, DefaultHeight, airHold.Tick.Original, airHold.Timeline);
                    var airHoldJoints = airHold.Children.OfType<umgr.AirHoldJoint>().ToArray();
                    for (var i = 0; i < airHoldJoints.Length; i++)
                    {
                        var joint = airHoldJoints[i];
                        WriteNote(bw, NoteType.AirHold, SlideAttr(joint.Joint, i == airHoldJoints.Length - 1),
                            Direction.None, ExAttr.None, 0,
                            airHold.Lane, airHold.Width, DefaultHeight, joint.Tick.Original, joint.Timeline);
                    }
                    break;
                case umgr.AirSlide airSlide:
                    WriteAirBase(bw, airSlide.Direction, airSlide.Color, airSlide);
                    WriteNote(bw, NoteType.AirSlide, LongAttr.Begin, Direction.None, ExAttr.None, 0,
                        airSlide.Lane, airSlide.Width, Height(airSlide.Height), airSlide.Tick.Original,
                        airSlide.Timeline);
                    var airSlideJoints = airSlide.Children.OfType<umgr.AirSlideJoint>().ToArray();
                    for (var i = 0; i < airSlideJoints.Length; i++)
                    {
                        var joint = airSlideJoints[i];
                        WriteNote(bw, NoteType.AirSlide, SlideAttr(joint.Joint, i == airSlideJoints.Length - 1),
                            Direction.None, ExAttr.None, 0,
                            joint.Lane, joint.Width, Height(joint.Height), joint.Tick.Original, joint.Timeline);
                    }
                    break;
                case umgr.AirCrash crash:
                    WriteNote(bw, NoteType.AirCrush, LongAttr.Begin, Direction.None, ExAttr.None,
                        CrushVariation(crash.Color),
                        crash.Lane, crash.Width, Height(crash.Height), crash.Tick.Original, crash.Timeline,
                        crash.Density.Original);
                    foreach (var joint in crash.Children.OfType<umgr.AirCrashJoint>())
                        WriteNote(bw, NoteType.AirCrush, LongAttr.End, Direction.None, ExAttr.None, 0,
                            joint.Lane, joint.Width, Height(joint.Height), joint.Tick.Original, joint.Timeline);
                    break;
            }
        }
    }

    private void WriteExCarrierIfNeeded(BinaryWriter bw, umgr.ExTapableNote note)
    {
        if (note.Effect is not { } effect) return;
        if (HasEffectCarrier(note)) return;
        WriteNote(bw, NoteType.ExTap, LongAttr.None, EffectDirection(effect), ExAttr.None, 0,
            note.Lane, note.Width, DefaultHeight, note.Tick.Original, note.Timeline);
    }

    private void WriteAirBase(BinaryWriter bw, AirDirection direction, Color color, umgr.NegativeNote action)
    {
        WriteNote(bw, NoteType.Air, LongAttr.None, AirDir(direction), AirEx(color), 0,
            action.Lane, action.Width, DefaultHeight, action.Tick.Original, action.Timeline);
    }

    private static bool HasAirActionAt(umgr.Air air) =>
        air.PairNote?.PairNote is umgr.AirHold or umgr.AirSlide;

    private bool HasEffectCarrier(umgr.Note note) =>
        _chart.Notes.Children.OfType<umgr.ExTap>().Any(x =>
            x.Tick == note.Tick && x.Lane <= note.Lane &&
            x.Lane + x.Width >= note.Lane + note.Width);

    private static void WriteNote(
        BinaryWriter bw,
        NoteType type,
        LongAttr longAttr,
        Direction direction,
        ExAttr exAttr,
        sbyte variationId,
        int lane,
        int width,
        int height,
        int tick,
        int timeline,
        int? optionValue = null)
    {
        bw.Write((sbyte)type);
        bw.Write((sbyte)longAttr);
        bw.Write((sbyte)direction);
        bw.Write((sbyte)exAttr);
        bw.Write(variationId);
        bw.Write(checked((sbyte)lane));
        bw.Write(checked((short)width));
        bw.Write(height);
        bw.Write(tick);
        bw.Write(timeline);
        if (optionValue is { } option) bw.Write(option);
    }

    private static LongAttr SlideAttr(Joint joint, bool isLast) => (joint, isLast) switch
    {
        (Joint.D, true) => LongAttr.End,
        (Joint.C, true) => LongAttr.EndNoAct,
        (Joint.D, false) => LongAttr.Step,
        _ => LongAttr.Control
    };

    private static Direction EffectDirection(ExEffect effect) => effect switch
    {
        ExEffect.UP => Direction.Up,
        ExEffect.DW => Direction.Down,
        ExEffect.CE => Direction.Center,
        ExEffect.LS => Direction.Left,
        ExEffect.RS => Direction.Right,
        ExEffect.LC => Direction.RotateLeft,
        ExEffect.RC => Direction.RotateRight,
        ExEffect.BS => Direction.InOut,
        _ => Direction.Up
    };

    private static Direction AirDir(AirDirection direction) => direction switch
    {
        AirDirection.IR => Direction.Up,
        AirDirection.DW => Direction.Down,
        AirDirection.UL => Direction.UpLeft,
        AirDirection.UR => Direction.UpRight,
        AirDirection.DL => Direction.DownLeft,
        AirDirection.DR => Direction.DownRight,
        _ => Direction.Up
    };

    private static ExAttr AirEx(Color color) => color is Color.PNK ? ExAttr.Invert : ExAttr.None;

    private static sbyte CrushVariation(Color color) => color switch
    {
        Color.DEF => 0,
        Color.RED => 1,
        Color.ORN => 2,
        Color.YEL => 3,
        Color.GRN => 4,
        Color.AQA => 5,
        Color.BLU => 6,
        Color.PPL => 7,
        Color.VLT => 8,
        Color.GRY => 10,
        Color.BLK => 11,
        Color.LIM => 12,
        Color.CYN => 13,
        Color.DGR => 14,
        Color.PNK => 15,
        Color.NON => 35,
        _ => 0
    };

    private static int Height(decimal value) => (int)Math.Round(value);

    private static int DifficultyValue(Difficulty d) => d switch
    {
        Difficulty.WorldsEnd => 4,
        Difficulty.Ultima => 5,
        _ => (int)d
    };

    private static string WeLevel(StarDifficulty star) => star switch
    {
        StarDifficulty.S1 => "1",
        StarDifficulty.S2 => "2",
        StarDifficulty.S3 => "3",
        StarDifficulty.S4 => "4",
        StarDifficulty.S5 => "5",
        _ => ""
    };

    internal static string FormatPlayLevel(decimal level)
    {
        if (level <= 0) return "";
        var whole = (int)decimal.Truncate(level);
        return level - whole >= 0.5m ? $"{whole}+" : whole.ToString(CultureInfo.InvariantCulture);
    }

    private static void WriteStringField(BinaryWriter bw, string name, string value)
    {
        bw.Write(Encoding.ASCII.GetBytes(name));
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        bw.Write((short)4);
        bw.Write(checked((short)bytes.Length));
        bw.Write(bytes);
    }

    private static void WriteIntField(BinaryWriter bw, string name, int value)
    {
        bw.Write(Encoding.ASCII.GetBytes(name));
        WriteIntFieldValue(bw, value);
    }

    private static void WriteIntFieldValue(BinaryWriter bw, int value)
    {
        if (value is >= short.MinValue and <= short.MaxValue)
        {
            bw.Write((short)1);
            bw.Write((short)value);
            return;
        }

        bw.Write((short)2);
        bw.Write((short)0);
        bw.Write(value);
    }

    private static void WriteDoubleField(BinaryWriter bw, string name, double value)
    {
        bw.Write(Encoding.ASCII.GetBytes(name));
        WriteDoubleFieldValue(bw, value);
    }

    private static void WriteDoubleFieldValue(BinaryWriter bw, double value)
    {
        bw.Write((short)3);
        bw.Write((short)0);
        bw.Write(value);
    }

    private enum NoteType : sbyte
    {
        Tap = 0x01,
        ExTap = 0x02,
        Flick = 0x03,
        Damage = 0x04,
        Hold = 0x05,
        Slide = 0x06,
        Air = 0x07,
        AirHold = 0x08,
        AirSlide = 0x09,
        AirCrush = 0x0A
    }

    private enum LongAttr : sbyte
    {
        None = 0x00,
        Begin = 0x01,
        Step = 0x02,
        Control = 0x03,
        End = 0x05,
        EndNoAct = 0x06
    }

    private enum Direction : sbyte
    {
        None = 0x00,
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
        InOut = 0x0D
    }

    private enum ExAttr : sbyte
    {
        None = 0x00,
        Invert = 0x01
    }
}
