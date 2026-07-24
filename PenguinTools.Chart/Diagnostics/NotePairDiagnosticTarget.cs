using PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Diagnostics;

public sealed record NotePairDiagnosticTarget(
    NoteDiagnosticTarget Left,
    NoteDiagnosticTarget Right)
{
    public static NotePairDiagnosticTarget From(Note left, Note right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new NotePairDiagnosticTarget(
            NoteDiagnosticTarget.From(left),
            NoteDiagnosticTarget.From(right));
    }
}
