using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class AirSlideColors
{
    internal static void WarnIfCombined(U.Chart chart, string format, DiagnosticCollector diagnostics)
    {
        foreach (var slide in chart.Notes.Children.OfType<U.AirSlide>())
        {
            if (slide.ArrowColor is not { } arrow || arrow == slide.Color)
            {
                continue;
            }
            diagnostics.Report(new TimedDiagnostic(Severity.Warning,
                Msg.Create(MsgKeys.Warn_Air_arrow_color_not_reproduced, format), slide.Tick.Original)
            { Target = slide });
        }
    }
}
