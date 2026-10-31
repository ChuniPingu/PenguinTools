
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser.mgxc;

public partial class MgxcParser
{
    private bool _lastEventWasExtras;
    private readonly List<byte[]> _imageEvents = [];
    private void ParseEvent(BinaryReader br)
    {
        var recordStart = br.BaseStream.Position;
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
            var end = br.BaseStream.Position;
            br.BaseStream.Position = recordStart;
            _imageEvents.Add(br.ReadBytes(checked((int)(end - recordStart))));
            return;
        }

        if (e == null)
        {
            // Stop before reading later records at the wrong offset.
            MessageDescriptor msg = Msg.Create(MsgKeys.MgCrit_Unrecognized_event, name);
            ThrowAtPosition(msg, br.BaseStream.Position, Mgxc);
        }

        if (e is UmgrModel.BookmarkEvent origin && origin.Tag.StartsWith("MGR_ORIGIN=", StringComparison.Ordinal))
        {
            _lastEventWasExtras = true;
            _hasFixedZeroOriginMarker |= ChartExtras.HasFixedZeroOrigin(origin.Tag);
        }
        else if (e is UmgrModel.BookmarkEvent preroll && preroll.Tag.StartsWith("MGR_PREROLL=", StringComparison.Ordinal))
        {
            _lastEventWasExtras = true;
            _hasPrerollBar1Marker |= ChartExtras.HasPrerollBar1(preroll.Tag);
        }
        else if (e is UmgrModel.BookmarkEvent pending && pending.Tag.StartsWith(ChartExtras.PendingSnapshotShiftPrefix, StringComparison.Ordinal))
        {
            ChartExtras.RejectPendingSnapshotShift(pending.Tag);
        }
        else if (e is UmgrModel.BookmarkEvent bookmark && ChartExtras.IsExtrasBookmark(bookmark.Tag))
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
