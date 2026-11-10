using System.Text.RegularExpressions;
using PenguinTools.Chart.Parser;

namespace PenguinTools.Chart.Writer;

internal static partial class ChartMediaPaths
{
    internal static string CommentForOutput(string comment, string sourceChart, string outputChart)
    {
        return MediaCommandRegex().Replace(ChartMetaCommands.NormalizeComment(comment), match =>
        {
            var token = match.Groups[2].Value;
            var quoted = token.StartsWith('"');
            var reference = quoted ? token[1..^1] : token;
            string relocated;
            try
            {
                relocated = ForOutput(reference, sourceChart, outputChart);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return match.Value;
            }
            var value = quoted || relocated.Any(char.IsWhiteSpace) ? $"\"{relocated}\"" : relocated;
            return match.Groups[1].Value + value;
        });
    }

    [GeneratedRegex("""(?m)^([ \t]*#meta[ \t]+(?:bg|wall)[ \t]+)("[^"\r\n]*"|[^\s"]+)""")]
    private static partial Regex MediaCommandRegex();

    internal static string ForOutput(string reference, string sourceChart, string outputChart)
    {
        if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference) ||
            string.IsNullOrWhiteSpace(sourceChart))
        {
            return reference;
        }

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceChart))!;
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputChart))!;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(sourceDirectory, outputDirectory, comparison))
        {
            return reference;
        }

        var mediaPath = Path.GetFullPath(reference, sourceDirectory);
        return Path.GetRelativePath(outputDirectory, mediaPath);
    }
}
