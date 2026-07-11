namespace PenguinTools.Core;

public sealed record ProgressReport(
    MessageDescriptor Phase,
    string? Unit = null,
    MessageDescriptor? Step = null,
    string? Item = null,
    string? Label = null,
    int? Completed = null,
    int? Total = null)
{
    public double? Percent =>
        Completed is { } completed && Total is { } total && total > 0
            ? Math.Clamp(completed / (double)total * 100.0, 0.0, 100.0)
            : null;
}