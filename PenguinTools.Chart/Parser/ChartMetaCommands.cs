using System.Text;
using System.Text.RegularExpressions;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser;

internal static partial class ChartMetaCommands
{
    public static bool IsIgnored(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return false;
        }

        var ignored = false;
        foreach (var parts in EnumerateCommands(comment))
        {
            if (parts is not ["meta", "ignore", .. var args])
            {
                continue;
            }

            ignored = args.Length == 0 || ParseBool(args[0]);
        }

        return ignored;
    }

    public static OperationResult<UmgrModel.Chart> SkipParse(
        DiagnosticCollector diagnostics,
        string path,
        int? line = null)
    {
        diagnostics.Clear();
        diagnostics.Report(CreateSkippedDiagnostic(path, line));
        return OperationResult<UmgrModel.Chart>.Failure().WithDiagnostics(diagnostics);
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
        foreach (var line in CommentLines(comment))
        {
            var trimmedLine = line.Trim();
            if (C2sRoundTripComment.IsRoundTripLine(trimmedLine))
            {
                continue;
            }

            if (!trimmedLine.StartsWith('#'))
            {
                continue;
            }

            var parts = Tokenize(trimmedLine[1..]);
            if (parts.Length == 0)
            {
                continue;
            }

            yield return parts;
        }
    }

    internal static string[] CommentLines(string comment) =>
        NormalizeComment(comment).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    internal static string NormalizeComment(string comment)
    {
        var normalized = new StringBuilder(comment.Length);
        var quoted = false;
        var index = 0;
        while (index < comment.Length)
        {
            var ch = comment[index];
            if (ch == '"')
            {
                quoted = !quoted;
            }

            var length = !quoted && ch == '\\' ? EscapedMetaLineBreakLength(comment.AsSpan(index)) : 0;
            if (length > 0)
            {
                normalized.Append('\n');
                index += length;
                continue;
            }

            normalized.Append(ch);
            index++;
        }

        return normalized.ToString();
    }

    private static int EscapedMetaLineBreakLength(ReadOnlySpan<char> text)
    {
        var length = 0;
        if (text.StartsWith("\\r\\n", StringComparison.Ordinal))
        {
            length = 4;
        }
        else if (text.StartsWith("\\r", StringComparison.Ordinal) || text.StartsWith("\\n", StringComparison.Ordinal))
        {
            length = 2;
        }

        return length > 0 && text[length..].TrimStart().StartsWith("#meta", StringComparison.Ordinal) ? length : 0;
    }

    private static bool ParseBool(string str)
    {
        var value = str.ToLowerInvariant();
        if (value is "true" or "1" or "yes")
        {
            return true;
        }

        if (value is "false" or "0" or "no")
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(str);
    }

    [GeneratedRegex("[^\\s\"]+|\"[^\"]*\"")]
    private static partial Regex CommandTokenRegex();
}
