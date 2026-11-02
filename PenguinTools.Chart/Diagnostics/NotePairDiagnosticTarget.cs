using PenguinTools.Chart.Models.umgr;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Diagnostics;

public sealed record NotePairDiagnosticTarget(
    NoteDiagnosticTarget Left,
    NoteDiagnosticTarget Right,
    TickPosition? TimePosition = null)
{
    public static NotePairDiagnosticTarget From(Note left, Note right, ITickFormatter? timeCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        TickPosition? timePosition = null;
        if (timeCalculator is not null)
        {
            timePosition = timeCalculator.GetPosition(left.Tick.Original);
        }

        return new NotePairDiagnosticTarget(
            NoteDiagnosticTarget.From(left),
            NoteDiagnosticTarget.From(right),
            timePosition);
    }

    public NotePairDiagnosticTarget WithTime(ITickFormatter timeCalculator, int tick)
    {
        ArgumentNullException.ThrowIfNull(timeCalculator);

        return this with
        {
            Left = Left.WithTime(timeCalculator),
            Right = Right.WithTime(timeCalculator),
            TimePosition = TimePosition ?? timeCalculator.GetPosition(tick)
        };
    }
}
