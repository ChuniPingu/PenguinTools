using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Diagnostics;

public sealed record NoteSegmentDiagnosticTarget(
    NoteDiagnosticTarget Start,
    NoteDiagnosticTarget End,
    int SourceLengthTicks,
    int ConvertedLengthTicks)
{
    public static NoteSegmentDiagnosticTarget From(Note note, int originOffset, ITickFormatter? calculator)
    {
        ArgumentNullException.ThrowIfNull(note);
        var (endTick, endLane, endWidth) = note switch
        {
            LongNote length => (length.EndTick.Original, length.EndLane, length.EndWidth),
            Sla sla => (checked(sla.Tick.Original + sla.Length.Original), sla.Lane, sla.Width),
            _ => throw new ArgumentException("A segment requires a long note or SLA.", nameof(note))
        };
        var start = NoteDiagnosticTarget.FromC2s(note, note.Tick.Original - originOffset).WithTime(calculator);
        var end = (start with
        {
            Tick = endTick - originOffset,
            Lane = endLane,
            Width = endWidth,
            TimePosition = null
        }).WithTime(calculator);
        return new(start, end, endTick - note.Tick.Original, new Time(endTick).Scaled - note.Tick.Scaled);
    }
}
