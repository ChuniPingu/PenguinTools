using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Diagnostics;

public sealed record NoteDiagnosticTarget(
    string Type,
    int Tick,
    int Lane,
    int Width,
    int Timeline,
    ExEffect? Effect = null,
    Joint? Joint = null,
    AirDirection? Direction = null,
    Color? Color = null,
    decimal? Height = null,
    int? Density = null)
{
    public static NoteDiagnosticTarget From(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var target = new NoteDiagnosticTarget(
            note.GetType().Name,
            note.Tick.Original,
            note.Lane,
            note.Width,
            note.Timeline);

        return note switch
        {
            ExTap exTap => target with { Effect = exTap.Effect },
            ExTapableNote exTapable => target with { Effect = exTapable.Effect },
            SlideJoint slideJoint => target with { Joint = slideJoint.Joint },
            Air air => target with { Direction = air.Direction, Color = air.Color },
            AirSlide airSlide => target with { Color = airSlide.Color, Height = airSlide.Height },
            AirSlideJoint airSlideJoint => target with
            {
                Joint = airSlideJoint.Joint,
                Height = airSlideJoint.Height
            },
            AirHold airHold => target with { Color = airHold.Color },
            AirHoldJoint airHoldJoint => target with { Joint = airHoldJoint.Joint },
            AirCrash airCrash => target with
            {
                Color = airCrash.Color,
                Height = airCrash.Height,
                Density = airCrash.Density.Original
            },
            AirCrashJoint airCrashJoint => target with
            {
                Height = airCrashJoint.Height,
                Density = airCrashJoint.Density.Original
            },
            _ => target
        };
    }
}
