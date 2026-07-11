namespace PenguinTools.Core.Diagnostic;

public static class SeverityCodes
{
    public static string ToCode(Severity severity)
    {
        return severity switch
        {
            Severity.Information => "information",
            Severity.Warning => "warning",
            Severity.Error => "error",
            _ => "diagnostic"
        };
    }
}