
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser.mgxc;

public partial class MgxcParser
{
    private bool _lastEventWasExtras;
    private void ParseEvent(BinaryReader br)
    {
        _lastEventWasExtras = false;
        var name = br.ReadUtf8String(4);
        UmgrModel.Event? e = null;

        if (name == "beat")
        {
            e = new UmgrModel.BeatEvent
            {
                Bar = (int)br.ReadField(),
                Numerator = (int)br.ReadField(),
                Denominator = (int)br.ReadField()
            };
        }
        else if (name == "bpm ")
        {
            e = new UmgrModel.BpmEvent
            {
                Tick = (int)br.ReadField(),
                Bpm = br.ReadField().Round()
            };
        }
        else if (name == "smod")
        {
            e = new UmgrModel.NoteSpeedEvent
            {
                Tick = (int)br.ReadField(),
                Speed = br.ReadField().Round()
            };
        }
        else if (name == "til ")
        {
            e = new UmgrModel.ScrollSpeedEvent
            {
                Timeline = (int)br.ReadField(),
                Tick = (int)br.ReadField(),
                Speed = br.ReadField().Round()
            };
        }
        else if (name == "bmrk")
        {
            e = new UmgrModel.BookmarkEvent
            {
                Id = (string)br.ReadWideField(),
                Tick = (int)br.ReadField(),
                Tag = (string)br.ReadWideField(),
                Rgb = (string)br.ReadWideField()
            };
        }
        else if (name == "mbkm")
        {
            e = new UmgrModel.BreakingMarker
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

        if (e is UmgrModel.BookmarkEvent bookmark && ChartExtras.IsExtrasBookmark(bookmark.Tag))
        {
            _lastEventWasExtras = true;
            var clickEnabled = Mgxc.Extras.ClickEnabled;
            var tutorial = Mgxc.Extras.Tutorial;
            var sourceSnapshot = Mgxc.Extras.SourceSnapshot;
            Mgxc.Extras = ChartExtras.FromBookmark(bookmark.Tag);
            Mgxc.Extras.ClickEnabled = clickEnabled;
            Mgxc.Extras.Tutorial = tutorial;
            if (Mgxc.Extras.SourceSnapshot.Length == 0)
            {
                Mgxc.Extras.SourceSnapshot = sourceSnapshot;
            }
        }
        else
        {
            Mgxc.Events.AppendChild(e);
        }

        br.ReadInt32(); // 00 00 00 00
    }
}
