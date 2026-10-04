using PenguinTools.Chart.Models;

using C2sModel = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart.Writer.c2s;

public partial class C2SChartWriter
{
#pragma warning disable CS0612
    private string Format(C2sModel.Event e)
    {
        return e switch
        {
            C2sModel.Stop stop => $"{FormatNode(stop)}\t{Scale(stop.Length)}",
            C2sModel.Bpm bpm => $"{FormatNode(bpm)}\t{bpm.Value:F3}",
            C2sModel.Met met => $"{FormatNode(met)}\t{met.Denominator}\t{met.Numerator}",
            C2sModel.Slp slp => $"{FormatNode(slp)}\t{Scale(slp.Length)}\t{slp.Speed:F6}\t{slp.Timeline}",
            C2sModel.Sfl sfl => $"{FormatNode(sfl)}\t{Scale(sfl.Length)}\t{sfl.Speed:F6}",
            C2sModel.Dcm dcm => $"{FormatNode(dcm)}\t{Scale(dcm.Length)}\t{dcm.Speed:F6}",
            _ => throw new InvalidOperationException($"Unsupported c2s event type '{e.GetType().FullName}'.")
        };
    }
#pragma warning restore CS0612

    private bool TryFormat(C2sModel.Note note, out string line, out MessageDescriptor? error)
    {
        switch (note)
        {
            case C2sModel.Tap tap:
                line = FormatNote(tap);
                error = null;
                return true;
            case C2sModel.Damage damage:
                line = FormatNote(damage);
                error = null;
                return true;
            case C2sModel.Flick flick:
                line = $"{FormatNote(flick)}\tL";
                error = null;
                return true;
            case C2sModel.ExTap exTap:
                line = $"{FormatNote(exTap)}{FormatEffect(exTap.Effect)}";
                error = null;
                return true;
            case C2sModel.Hold hold:
                line = $"{FormatNote(hold)}\t{ScaleLength(hold)}{FormatEffect(hold.Effect)}";
                error = null;
                return true;
            case C2sModel.Sla sla:
                line = $"{FormatNote(sla)}\t{Scale(sla.Length)}\t{sla.Timeline}";
                error = null;
                return true;
            case C2sModel.Slide slide:
                line = $"{FormatNote(slide)}\t{ScaleLength(slide)}\t{slide.EndLane}\t{slide.EndWidth}";
                line += slide.NoLine ? "\tNCL" : "\tSLD";
                line += FormatEffect(slide.Effect);
                error = null;
                return true;
            case C2sModel.Air { Parent: null }:
                line = string.Empty;
                error = Msg.Key(MsgKeys.MgCrit_Air_parent_null);
                return false;
            case C2sModel.Air { Parent: { } parent } air:
                line = $"{FormatNote(air)}\t{ParentId(parent)}\t{air.Color}";
                error = null;
                return true;
            case C2sModel.AirSlide { Parent: { } parent } airSlide:
                line =
                    $"{FormatNote(airSlide)}\t{ParentId(parent)}\t{airSlide.Height.Result}\t{ScaleLength(airSlide)}\t{airSlide.EndLane}\t{airSlide.EndWidth}\t{airSlide.EndHeight.Result}\t{airSlide.Color}";
                error = null;
                return true;
            case C2sModel.AirSlide { Parent: null }:
            case C2sModel.AirHold { Parent: null }:
                line = string.Empty;
                error = Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null);
                return false;
            case C2sModel.AirHold { Parent: { } parent } airHold:
                line = $"{FormatNote(airHold)}\t{ParentId(parent)}\t{ScaleLength(airHold)}\t{airHold.Color}";
                error = null;
                return true;
            case C2sModel.AirCrash airCrash:
                line =
                    $"{FormatNote(airCrash)}\t{Scale(airCrash.Density)}\t{airCrash.Height.Result}\t{ScaleLength(airCrash)}\t{airCrash.EndLane}\t{airCrash.EndWidth}\t{airCrash.EndHeight.Result}\t{airCrash.Color}";
                line += $"\t{airCrash.Attr}";
                error = null;
                return true;
            default:
                throw new InvalidOperationException($"Unsupported c2s note type '{note.GetType().FullName}'.");
        }
    }

    private static string ParentId(C2sModel.Note parent) => parent switch
    {
        C2sModel.Hold => "HLD",
        C2sModel.Slide => "SLD",
        _ => parent.Id
    };

    // Keep exact endpoints until final C2S quantization and statistics invalidation.
    private int ScaleLength(C2sModel.LongNote note) => Scale(checked(note.EndTick.Original - note.Tick.Original));

    private int Scale(Time time)
    {
        var scaled = (long)time.Original * Resolution;
        if (scaled % 1920 != 0)
        {
            throw new FormatException("Tick cannot be represented at the output resolution.");
        }

        return checked((int)(scaled / 1920));
    }

    private string FormatNode(C2sModel.Node node)
    {
        var pos = new Position(node.Tick.Original / 1920, Scale(node.Tick.Original % 1920));
        return $"{node.Id}\t{pos.Measure}\t{pos.Offset}";
    }

    private string FormatNote(C2sModel.Note note)
    {
        return $"{FormatNode(note)}\t{note.Lane}\t{note.Width}";
    }

    private static string FormatEffect(ExEffect? effect)
    {
        return effect is null ? string.Empty : $"\t{effect}";
    }
}
