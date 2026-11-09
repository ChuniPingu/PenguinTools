namespace PenguinTools.Chart.Writer;

internal static class ChartMediaPaths
{
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
