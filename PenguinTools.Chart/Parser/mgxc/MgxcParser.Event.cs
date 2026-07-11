namespace PenguinTools.Chart.Parser.mgxc;

using umgr = Models.umgr;

public partial class MgxcParser
{
    private void ParseEvent(BinaryReader br)
    {
        var name = br.ReadUtf8String(4);
        umgr.Event? e = null;

        if (name == "beat")
        {
            e = new umgr.BeatEvent
            {
                Bar = (int)br.ReadField(),
                Numerator = (int)br.ReadField(),
                Denominator = (int)br.ReadField()
            };
        }
        else if (name == "bpm ")
        {
            e = new umgr.BpmEvent
            {
                Tick = (int)br.ReadField(),
                Bpm = br.ReadField().Round()
            };
        }
        else if (name == "smod")
        {
            e = new umgr.NoteSpeedEvent
            {
                Tick = (int)br.ReadField(),
                Speed = br.ReadField().Round()
            };
        }
        else if (name == "til ")
        {
            e = new umgr.ScrollSpeedEvent
            {
                Timeline = (int)br.ReadField(),
                Tick = (int)br.ReadField(),
                Speed = br.ReadField().Round()
            };
        }
        else if (name == "bmrk")
        {
            br.ReadWideField(); // hash
            e = new umgr.BookmarkEvent
            {
                Tick = (int)br.ReadField(),
                Tag = (string)br.ReadWideField()
            };
            br.ReadWideField(); // rgb
        }
        else if (name == "mbkm")
        {
            e = new umgr.BreakingMarker
            {
                Tick = (int)br.ReadField()
            };
        }
        else if (name == "rimg")
        {
            br.ReadField();
            br.ReadField();
            br.ReadWideField();
            br.ReadInt32();
            return;
        }

        if (e == null)
        {
            // avoid misalignment
            MessageDescriptor msg = Msg.Create(MsgKeys.MgCrit_Unrecognized_event, name);
            ThrowAtPosition(msg, br.BaseStream.Position, Mgxc);
        }

        Mgxc.Events.AppendChild(e);

        br.ReadInt32(); // 00 00 00 00
    }
}