using System.Text.Json;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models.umgr;
using PenguinTools.Workflow;

namespace PenguinTools.Application;

public static class DiagnosticTargetSerializer
{
    public static JsonElement? ToJsonElement(object? target)
    {
        return target switch
        {
            null => null,
            NotePairDiagnosticTarget pair => JsonSerializer.SerializeToElement(pair,
                DiagnosticTargetJsonContext.Default.NotePairDiagnosticTarget),
            NoteDiagnosticTarget noteTarget => JsonSerializer.SerializeToElement(noteTarget,
                DiagnosticTargetJsonContext.Default.NoteDiagnosticTarget),
            Note note => JsonSerializer.SerializeToElement(NoteDiagnosticTarget.From(note),
                DiagnosticTargetJsonContext.Default.NoteDiagnosticTarget),
            ChartDiagnosticTarget chart => JsonSerializer.SerializeToElement(chart,
                DiagnosticTargetJsonContext.Default.ChartDiagnosticTarget),
            ChartDiagnosticTarget[] charts => JsonSerializer.SerializeToElement(charts,
                DiagnosticTargetJsonContext.Default.ChartDiagnosticTargetArray),
            string text => JsonSerializer.SerializeToElement(text, DiagnosticTargetJsonContext.Default.String),
            _ => JsonSerializer.SerializeToElement(target.ToString() ?? string.Empty,
                DiagnosticTargetJsonContext.Default.String)
        };
    }
}
