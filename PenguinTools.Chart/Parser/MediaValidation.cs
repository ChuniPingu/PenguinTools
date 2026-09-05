using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;

namespace PenguinTools.Chart.Parser;

internal static class MediaValidation
{
    public static async Task ReportAsync(Task<ProcessCommandResult> validation, string path, string messageKey,
        Action onFailure, IDiagnosticSink diagnostics)
    {
        object failure;
        try
        {
            var result = await validation;
            if (result.IsSuccess) return;
            failure = result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex;
        }

        onFailure();
        diagnostics.Report(new PathDiagnostic(Severity.Warning, Msg.Key(messageKey), path)
        {
            Target = failure
        });
    }
}
