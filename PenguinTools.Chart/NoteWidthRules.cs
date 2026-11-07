using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class NoteWidthRules
{
    // Native curve clipping can collapse a nonjudging sample. It does not
    // permit a zero-width playable head, judgement or AIR attachment.
    internal static bool IsClippedControl(U.Note note) => note is
        U.SlideJoint { Joint: Joint.C, IsCurveControl: false } or
        U.AirSlideJoint { Joint: Joint.C, IsCurveControl: false };

    internal static void Validate(U.Chart chart)
    {
        foreach (var note in chart.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children)))
        {
            if (note.Width <= 0 && !(note.Width == 0 && IsClippedControl(note)))
            {
                Fail(note.Width, note.Tick.Original, note);
            }
            if (note.Width == 0 && note is U.PositiveNote { PairNote: not null })
            {
                Fail(note.Width, note.Tick.Original, note);
            }
        }
    }

    internal static void Validate(C.Chart chart)
    {
        foreach (var note in chart.Notes)
        {
            if (note.Width <= 0 && !(note.Width == 0 && note is C.Slide or C.AirSlide))
            {
                Fail(note.Width, note.Tick.Original, note);
            }
            if (note is C.Slide or C.AirSlide or C.AirCrash)
            {
                var path = (C.LongNote)note;
                var control = note is C.Slide { Joint: Joint.C } or C.AirSlide { Joint: Joint.C };
                if (path.EndWidth <= 0 && !(path.EndWidth == 0 && control))
                {
                    Fail(path.EndWidth, path.EndTick.Original, note);
                }
            }
            if (note is C.AirSlide { Width: 0 } air &&
                (air.Parent is not C.AirSlide { Joint: Joint.C, EndWidth: 0 } prior ||
                 prior.EndTick.Round != air.Tick.Round || prior.EndLane != air.Lane ||
                 C2sHeight.Key(prior.EndHeight) != C2sHeight.Key(air.Height)))
            {
                Fail(air.Width, air.Tick.Original, air);
            }
        }
        foreach (var path in C2sSlidePaths.Build(chart.Notes.OfType<C.Slide>()))
        {
            if (path[0].Width == 0)
            {
                Fail(0, path[0].Tick.Original, path[0]);
            }
        }
    }

    private static void Fail(int width, int tick, object note) =>
        throw new TimedDiagnosticException(Msg.Create(MsgKeys.MgCrit_Invalid_note_width, width), tick, note);
}
