using System.Globalization;
using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class C2sStatisticsTests
{
    public static TheoryData<string, int[]> AcceptedRecords => new()
    {
        { "AIR 0 0 0 4 TAP", [0, 0, 0, 0, 0] },
        { "AIR 0 0 0 4 TAP\nTAP 0 0 0 4", [1, 0, 0, 0, 0] },
        { "TAP 0 0 0 4\nAIR 0 0 0 4 TAP\nAUL 0 0 0 4 TAP", [1, 0, 0, 1, 0] },
        { "TAP 0 0 0 4\nTAP 0 0 0 4\nAIR 0 0 0 4 TAP\nAUL 0 0 0 4 TAP", [2, 0, 0, 2, 0] },
        { "TAP 0 0 0 4\nAIR 0 0 0 4 TAP\nAHD 0 0 0 4 TAP 96 DEF", [1, 0, 0, 1, 0] },
        { "AHD 0 0 0 4 TAP 96 DEF\nTAP 0 0 0 4", [1, 0, 0, 0, 0] },
        { "SLD 0 0 0 4 96 4 4 SLD\nSLD 0 96 4 4 96 8 4 SLD\nAHX 0 96 4 4 SLD 96 DEF", [1, 0, 4, 0, 0] },
        { "TAP 0 0 0 4\nAHD 0 0 0 4 TAP 96 DEF\nAHX 0 96 0 4 AHD 96 DEF", [1, 0, 0, 2, 0] },
        { "TAP 0 0 0 4\nAHX 0 0 0 4 TAP 96 DEF\nAHD 0 96 0 4 AHX 96 DEF", [1, 0, 0, 4, 0] },
        { "TAP 0 0 0 4\nASC 0 0 0 4 TAP 1 96 4 4 2 DEF\nASD 0 96 4 4 ASC 3 96 8 4 4 DEF", [1, 0, 0, 1, 0] },
        { "TAP 0 0 0 4\nASC 0 0 0 4 TAP 1 96 4 4 2 DEF\nASD 0 96 4 4 ASD 2 96 8 4 4 DEF", [1, 0, 0, 1, 0] },
        { "TAP 0 0 0 4\nASC 0 0 0 4 TAP 1 96 4 4 2 DEF\nASC 0 96 4 4 ASC 2 96 8 4 3 DEF\nASD 0 192 8 4 ASC 3 48 12 4 4 DEF", [1, 0, 0, 5, 0] },
        { "ALD 0 96 0 4 48 1 96 4 4 1 DEF CEN\nALD 0 0 0 4 48 1 96 0 4 1 DEF CEN", [0, 0, 0, 6, 0] },
        { "TUTORIAL 1\nSTP 0 0 200\nHLD 0 0 0 4 100", [1, 0, 0, 0, 0] },
        { "TUTORIAL 1\nSTP 0 0 100\nHLD 0 0 0 4 100", [1, 1, 0, 0, 0] },
        { "TUTORIAL 1\nSLP 0 0 200 0 1\nHLD 0 0 0 4 100", [1, 3, 0, 0, 0] },
        { "TUTORIAL 1\nSTP 0 0 200\nTAP 0 0 0 4\nAHD 0 0 0 4 TAP 100 DEF", [1, 0, 0, 2, 0] }
    };

    [Theory]
    [MemberData(nameof(AcceptedRecords))]
    public void CountsOnlyAcceptedRelationshipsAndJudgements(string records, int[] expected) =>
        AssertCounts(ChartStatistics.Calculate(Header + records.Replace(' ', '\t')), expected);

    [Theory]
    [InlineData("0", 100, 2)]
    [InlineData("0.5", 100, 1)]
    [InlineData("0.999", 100, 1)]
    [InlineData("1", 96, 0)]
    [InlineData("-1", 100, 3)]
    public void AirExclusion_UsesConfiguredMargin(string aer, int end, int holds)
    {
        var text = Header + $"PROGJUDGE_AER\t{aer}\nHLD\t0\t0\t0\t4\t{end}\nAIR\t0\t{end}\t0\t4\tHLD";
        AssertCounts(ChartStatistics.Calculate(text), [1, holds, 0, 1, 0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Writer_ReplacesUnchangedSourceAndSnapshotStatistics(bool throughEditorModel)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, Header + "HLD\t0\t0\t0\t4\t96\n" +
                "T_JUDGE_TAP\t1\nT_JUDGE_HLD\t500\nT_JUDGE_SLD\t0\nT_JUDGE_AIR\t0\nT_JUDGE_FLK\t0\nT_JUDGE_ALL\t501", ct);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var chart = parsed.Value!;
            if (throughEditorModel)
            {
                var editor = new UgcChartConverter(new UgcConvertRequest(chart)).Convert();
                Assert.True(editor.Succeeded, editor.ToString());
                var converted = new C2SChartConverter(new C2SConvertRequest(editor.Value!)).Convert();
                Assert.True(converted.Succeeded, converted.ToString());
                chart = converted.Value!;
            }
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            AssertCounts(await File.ReadAllTextAsync(path, ct), [1, 2, 0, 0, 0]);
        }
        finally { File.Delete(path); }
    }

    private const string Header = "VERSION\t1.15.00\t1.15.00\nRESOLUTION\t384\nBPM_DEF\t120\t120\t120\t120\nBPM\t0\t0\t120\n";

    private static void AssertCounts(string text, int[] expected)
    {
        var counts = text.Split('\n').Select(line => line.Trim().Split('\t'))
            .Where(p => p[0].StartsWith("T_JUDGE_", StringComparison.Ordinal))
            .ToDictionary(p => p[0], p => int.Parse(p[1], CultureInfo.InvariantCulture));
        string[] labels = ["TAP", "HLD", "SLD", "AIR", "FLK"];
        Assert.Equal(expected, labels.Select(label => counts["T_JUDGE_" + label]));
        Assert.Equal(expected.Sum(), counts["T_JUDGE_ALL"]);
    }
}
