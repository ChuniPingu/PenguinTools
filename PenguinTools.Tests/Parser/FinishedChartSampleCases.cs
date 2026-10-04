using Xunit;

namespace PenguinTools.Tests.Parser;

/// <summary>
///     Paired chart samples: for each <c>*.ugc</c> in <see cref="ChartTestPaths.AssetsDirectory" />, the matching
///     <c>*.mgxc</c> with the same file name (without extension).
/// </summary>
public static class FinishedChartSampleCases
{
    public static IEnumerable<TheoryDataRow<string, string, string>> MasterPairs()
    {
        var root = ChartTestPaths.AssetsDirectory;
        if (!Directory.Exists(root)) yield break;

        foreach (var ugcPath in Directory.EnumerateFiles(root, "*.ugc", SearchOption.TopDirectoryOnly))
        {
            var stem = Path.GetFileNameWithoutExtension(ugcPath);
            var mgxcPath = Path.Combine(root, stem + ".mgxc");
            if (!File.Exists(mgxcPath)) continue;

            yield return new TheoryDataRow<string, string, string>(stem, ugcPath, mgxcPath);
        }
    }
}
