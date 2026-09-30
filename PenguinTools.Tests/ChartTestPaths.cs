namespace PenguinTools.Tests;

/// <summary>
///     Paths under the test project (no machine-specific roots).
/// </summary>
internal static class ChartTestPaths
{
    /// <summary>
    ///     <c>PenguinTools.Tests/Assets</c> — paired <c>.ugc</c> / <c>.mgxc</c> samples live here.
    /// </summary>
    public static string AssetsDirectory =>
        Environment.GetEnvironmentVariable("PENGUINTOOLS_TEST_CHARTS") is { Length: > 0 } directory
            ? Path.GetFullPath(directory)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets"));
}
