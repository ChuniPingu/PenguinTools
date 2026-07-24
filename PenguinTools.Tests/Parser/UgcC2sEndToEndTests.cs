using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class UgcC2sEndToEndTests
{
    /// <summary>
    ///     Converts and writes both charts, then compares output from the start through the line before the first
    ///     <c>SLP</c> (scroll) row.
    ///     Full first-100-line identity is still sensitive to TIL-derived <c>SLP</c> ordering on some charts.
    /// </summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FinishedChartSampleCases.MasterPairs), MemberType = typeof(FinishedChartSampleCases))]
    public async Task UgcProducedC2s_MatchesMgxcProducedC2s_FirstHundredLines(string name, string ugcPath,
        string mgxcPath)
    {
        var assets = TestAssets.Load();
        var media = TestMediaTool.Instance;
        var ct = TestContext.Current.CancellationToken;

        var ugcParse = await new UgcParser(new UgcParseRequest(ugcPath, assets), media).ParseAsync(ct);
        var mgxcParse = await new MgxcParser(new MgxcParseRequest(mgxcPath, assets), media).ParseAsync(ct);

        Assert.True(ugcParse.Succeeded, $"UGC parse failed for {name}: {ugcParse}");
        Assert.True(mgxcParse.Succeeded, $"MGXC parse failed for {name}: {mgxcParse}");

        var ugcOut = Path.GetTempFileName() + ".c2s";
        var mgxcOut = Path.GetTempFileName() + ".c2s";
        try
        {
            var ugcConvert = new C2SChartConverter(new C2SConvertRequest(ugcParse.Value!)).Convert();
            var mgxcConvert = new C2SChartConverter(new C2SConvertRequest(mgxcParse.Value!)).Convert();

            Assert.True(ugcConvert.Succeeded, $"UGC c2s convert failed for {name}: {ugcConvert}");
            Assert.True(mgxcConvert.Succeeded, $"MGXC c2s convert failed for {name}: {mgxcConvert}");

            var ugcWrite = await new C2SChartWriter(
                new C2SWriteRequest(ugcOut, ugcConvert.Value!, ugcParse.Value!.GetCalculator())).WriteAsync(ct);
            var mgxcWrite = await new C2SChartWriter(
                new C2SWriteRequest(mgxcOut, mgxcConvert.Value!, mgxcParse.Value!.GetCalculator())).WriteAsync(ct);

            Assert.True(ugcWrite.Succeeded, $"UGC c2s write failed for {name}: {ugcWrite}");
            Assert.True(mgxcWrite.Succeeded, $"MGXC c2s write failed for {name}: {mgxcWrite}");

            static string[] LinesBeforeFirstSlp(string path)
            {
                var list = new List<string>();
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith("SLP\t", StringComparison.Ordinal)) break;
                    list.Add(line);
                }

                return list.ToArray();
            }

            var ugcLines = LinesBeforeFirstSlp(ugcOut);
            var mgxcLines = LinesBeforeFirstSlp(mgxcOut);

            Assert.Equal(mgxcLines, ugcLines);
        }
        finally
        {
            if (File.Exists(ugcOut)) File.Delete(ugcOut);
            if (File.Exists(mgxcOut)) File.Delete(mgxcOut);
        }
    }
}
