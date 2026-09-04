using System.Text.RegularExpressions;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Parser;

using umgr = Models.umgr;

internal static partial class ChartMetaCommands
{
    public static bool IsIgnored(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return false;

        var ignored = false;
        foreach (var parts in EnumerateCommands(comment))
        {
            if (parts is not ["meta", "ignore", .. var args]) continue;
            ignored = args.Length == 0 || ParseBool(args[0]);
        }

        return ignored;
    }

    public static OperationResult<umgr.Chart> SkipParse(
        DiagnosticCollector diagnostics,
        string path,
        int? line = null)
    {
        diagnostics.Clear();
        diagnostics.Report(CreateSkippedDiagnostic(path, line));
        return OperationResult<umgr.Chart>.Failure().WithDiagnostics(diagnostics);
    }

    public static Diagnostic CreateSkippedDiagnostic(string path, int? line = null)
    {
        var message = Msg.Key(MsgKeys.Mg_Meta_Ignored);
        return line is { } lineNumber
            ? new LocationDiagnostic(Severity.Information, message, lineNumber, path)
            : new PathDiagnostic(Severity.Information, message, path);
    }

    public static string[] Tokenize(string command)
    {
        return CommandTokenRegex().Matches(command)
            .Select(match => match.Value.Length >= 2 && match.Value[0] == '"' && match.Value[^1] == '"'
                ? match.Value[1..^1]
                : match.Value)
            .ToArray();
    }

    private static IEnumerable<string[]> EnumerateCommands(string comment)
    {
        var normalized = comment
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);

        foreach (var line in normalized.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmedLine = line.Trim();
            if (C2sRoundTripComment.IsRoundTripLine(trimmedLine)) continue;
            if (!trimmedLine.StartsWith('#')) continue;

            var parts = Tokenize(trimmedLine[1..]);
            if (parts.Length == 0) continue;
            yield return parts;
        }
    }

    private static bool ParseBool(string str)
    {
        var value = str.ToLowerInvariant();
        if (value is "true" or "1" or "yes") return true;
        if (value is "false" or "0" or "no") return false;
        return string.IsNullOrWhiteSpace(str);
    }

    [GeneratedRegex("[^\\s\"]+|\"[^\"]*\"")]
    private static partial Regex CommandTokenRegex();
}
