using PenguinTools.Chart.Models;

namespace PenguinTools.Chart.Writer.c2s;

using c2s = Models.c2s;

public partial class C2SChartWriter
{
#pragma warning disable CS0612
    private static string Format(c2s.Event e)
    {
        return e switch
        {
            c2s.Bpm bpm => $"{FormatNode(bpm)}\t{bpm.Value:F3}",
            c2s.Met met => $"{FormatNode(met)}\t{met.Denominator}\t{met.Numerator}",
            c2s.Slp slp => $"{FormatNode(slp)}\t{slp.Length.Scaled}\t{slp.Speed:F6}\t{slp.Timeline}",
            c2s.Sfl sfl => $"{FormatNode(sfl)}\t{sfl.Length.Scaled}\t{sfl.Speed:F6}",
            c2s.Dcm dcm => $"{FormatNode(dcm)}\t{dcm.Length.Scaled}\t{dcm.Speed:F6}",
            _ => throw new InvalidOperationException($"Unsupported c2s event type '{e.GetType().FullName}'.")
        };
    }
#pragma warning restore CS0612

    private static bool TryFormat(c2s.Note note, out string line, out MessageDescriptor? error)
    {
        switch (note)
        {
            case c2s.Tap tap:
                line = FormatNote(tap);
                error = null;
                return true;
            case c2s.Damage damage:
                line = FormatNote(damage);
                error = null;
                return true;
            case c2s.Flick flick:
                line = FormatNote(flick);
                error = null;
                return true;
            case c2s.ExTap exTap:
                line = $"{FormatNote(exTap)}{FormatEffect(exTap.Effect)}";
                error = null;
                return true;
            case c2s.Hold hold:
                line = $"{FormatNote(hold)}\t{hold.Length.Scaled}{FormatEffect(hold.Effect)}";
                error = null;
                return true;
            case c2s.Sla sla:
                line = $"{FormatNote(sla)}\t{sla.Length.Scaled}\t{sla.Timeline}";
                error = null;
                return true;
            case c2s.Slide slide:
                line =
                    $"{FormatNote(slide)}\t{slide.Length.Scaled}\t{slide.EndLane}\t{slide.EndWidth}\tSLD{FormatEffect(slide.Effect)}";
                error = null;
                return true;
            case c2s.Air { Parent: null }:
                line = string.Empty;
                error = Msg.Key(MsgKeys.MgCrit_Air_parent_null);
                return false;
            case c2s.Air { Parent: { } parent } air:
                line = $"{FormatNote(air)}\t{parent.Id}\t{air.Color}";
                error = null;
                return true;
            case c2s.AirSlide { Parent: null }:
                line = string.Empty;
                error = Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null);
                return false;
            case c2s.AirSlide { Parent: { } parent } airSlide:
                line =
                    $"{FormatNote(airSlide)}\t{parent.Id}\t{airSlide.Height.Result:F1}\t{airSlide.Length.Scaled}\t{airSlide.EndLane}\t{airSlide.EndWidth}\t{airSlide.EndHeight.Result:F1}\t{airSlide.Color}";
                error = null;
                return true;
            case c2s.AirHold { Parent: null }:
                line = string.Empty;
                error = Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null);
                return false;
            case c2s.AirHold { Parent: { } parent } airHold:
                line = $"{FormatNote(airHold)}\t{parent.Id}\t{airHold.Length.Scaled}\t{airHold.Color}";
                error = null;
                return true;
            case c2s.AirCrash airCrash:
                line =
                    $"{FormatNote(airCrash)}\t{airCrash.Density.Scaled}\t{airCrash.Height.Result:F1}\t{airCrash.Length.Scaled}\t{airCrash.EndLane}\t{airCrash.EndWidth}\t{airCrash.EndHeight.Result:F1}\t{airCrash.Color}";
                error = null;
                return true;
            default:
                throw new InvalidOperationException($"Unsupported c2s note type '{note.GetType().FullName}'.");
        }
    }

    private static string FormatNode(c2s.Node node)
    {
        var pos = node.Tick.Position;
        return $"{node.Id}\t{pos.Measure}\t{pos.Offset}";
    }

    private static string FormatNote(c2s.Note note)
    {
        return $"{FormatNode(note)}\t{note.Lane}\t{note.Width}";
    }

    private static string FormatEffect(ExEffect? effect)
    {
        return effect is null ? string.Empty : $"\t{effect}";
    }
}