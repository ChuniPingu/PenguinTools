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
        { "SLC 0 0 0 4 96 4 0 SLD\nSLD 0 96 4 1 96 8 4 SLD", [2, 0, 4, 0, 0] },
        { "SLC 0 0 0 4 96 4 0 SLD\nSLD 0 96 4 0 96 8 4 SLD", [1, 0, 4, 0, 0] },
        { "TAP 0 0 0 4\nASC 0 0 0 4 TAP 5 96 4 0 5 DEF\nASD 0 96 4 1 ASC 5 96 8 4 5 DEF", [1, 0, 0, 1, 0] },
        { "TAP 0 0 0 4\nASC 0 0 0 4 TAP 5 96 4 0 5 DEF\nASD 0 96 4 0 ASC 5 96 8 4 5 DEF", [1, 0, 0, 4, 0] },
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
        { "ALD 0 0 0 4 48 1 0 4 4 1 DEF DEF", [0, 0, 0, 0, 0] },
        { "ALD 0 96 0 4 48 1 -96 4 4 1 DEF DEF", [0, 0, 0, 0, 0] },
        { "ALD 0 0 0 4 48 1 96 4 4 2 UNKNOWN DEF\nALD 0 96 4 4 48 2 96 8 4 3 DEF DEF", [0, 0, 0, 5, 0] },
        { "SLA 0 0 0 4 96 0\nAIR 0 96 0 4 SLA DEF", [0, 0, 0, 1, 0] },
        { "SLA 0 0 0 4 96 0\nAIR 0 0 0 4 SLA DEF", [0, 0, 0, 0, 0] },
        { "SLA 0 0 0 4 96 0\nAHD 0 96 0 4 SLA 96 DEF\nAIR 0 96 0 4 SLA DEF", [0, 0, 0, 2, 0] },
        { "SLA 0 0 0 4 96 0\nSLA 0 0 0 4 96 0\nAIR 0 96 0 4 SLA DEF\nAIR 0 96 0 4 SLA DEF", [0, 0, 0, 2, 0] },
        { "SLA 0 0 0 4 0 0\nAIR 0 0 0 4 SLA DEF", [0, 0, 0, 1, 0] },
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
    [InlineData("ASX", "ASX", 0)]
    [InlineData("ASX", "AHD", 0)]
    [InlineData("AHX", "ASX", 1)]
    [InlineData("AHX", "AHX", 4)]
    public void LegacyAirHoldAliases_DoNotCreateNativeJudgements(string firstTag, string parentTag, int airCount)
    {
        var records = $"TAP 0 0 0 4\n{firstTag} 0 0 0 4 TAP 96 DEF\nAHD 0 96 0 4 {parentTag} 96 DEF";
        AssertCounts(ChartStatistics.Calculate(Header + records.Replace(' ', '\t')), [1, 0, 0, airCount, 0]);
    }

    [Theory]
    [InlineData("5.06", "5.10", 3)]
    [InlineData("5.04", "5.09", 2)]
    public void AirSlideContinuation_UsesNativeHeightKey(string endHeight, string nextHeight, int airCount)
    {
        var records = $"TAP 0 0 0 4\nASD 0 0 0 4 TAP 5 96 4 4 {endHeight} DEF\n" +
            $"ASD 0 96 4 4 ASD {nextHeight} 96 8 4 5 DEF";
        AssertCounts(ChartStatistics.Calculate(Header + records.Replace(' ', '\t')), [1, 0, 0, airCount, 0]);
    }

    [Theory]
    [InlineData("5.06", "5.10", 5)]
    [InlineData("5.04", "5.09", 6)]
    public void AirCrashContinuation_UsesNativeHeightKey(string endHeight, string nextHeight, int airCount)
    {
        var records = $"ALD 0 0 0 4 48 5 96 4 4 {endHeight} DEF DEF\n" +
            $"ALD 0 96 4 4 48 {nextHeight} 96 8 4 5 DEF AxisY";
        AssertCounts(ChartStatistics.Calculate(Header + records.Replace(' ', '\t')), [0, 0, 0, airCount, 0]);
    }

    [Theory]
    [InlineData(8, 4, 8, 4, 3)]
    [InlineData(8, 4, 4, 4, 2)]
    [InlineData(4, 2, 4, 2, 3)]
    [InlineData(4, 2, 4, 4, 2)]
    public void AirHoldContinuation_UsesLiveGroundEndpoint(int endLane, int endWidth,
        int nextLane, int nextWidth, int airCount)
    {
        var records = "SLD 0 0 0 4 96 4 4 SLD\nAHD 0 96 4 4 SLD 96 DEF\n" +
            $"SLD 0 96 4 4 96 {endLane} {endWidth} SLD\nAHD 0 192 {nextLane} {nextWidth} AHD 96 DEF";
        AssertCounts(ChartStatistics.Calculate(Header + records.Replace(' ', '\t')), [1, 0, 3, airCount, 0]);
    }

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
    [InlineData("119.999999", "240", 2)]
    [InlineData("119.99999", "240", 4)]
    [InlineData("239.999999", "240", 1)]
    [InlineData("239.99999", "240", 2)]
    [InlineData("120", "240.000001", 2)]
    [InlineData("120", "240.00002", 4)]
    public void JudgementInterval_UsesSinglePrecisionThresholds(string bpm, string judgeBpm, int holds)
    {
        var text = $"VERSION\t1.15.00\t1.15.00\nBPM\t0\t0\t{bpm}\nPROGJUDGE_BPM\t{judgeBpm}\nHLD\t0\t0\t0\t4\t96";
        AssertCounts(ChartStatistics.Calculate(text), [1, holds, 0, 0, 0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Writer_RecalculatesAllCountsDespiteSavedStatistics(bool throughEditorModel)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            const string records = """
                HLD 0 0 0 4 96
                SLD 0 0 8 4 96 12 4 SLD
                TAP 1 0 0 4
                AIR 1 0 0 4 TAP
                FLK 1 0 8 4
                T_JUDGE_TAP 500
                T_JUDGE_HLD 500
                T_JUDGE_SLD 500
                T_JUDGE_AIR 500
                T_JUDGE_FLK 500
                T_JUDGE_ALL 2500
                """;
            await File.WriteAllTextAsync(path, Header + records.Replace(' ', '\t'), ct);
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
            chart.Meta.C2sJudgeHldProxyBaseline = throughEditorModel ? 99 : null;
            chart.Meta.C2sJudgeSldProxyBaseline = throughEditorModel ? 99 : null;
            chart.Meta.C2sJudgeAirProxyBaseline = throughEditorModel ? 99 : null;
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            AssertCounts(await File.ReadAllTextAsync(path, ct), [3, 2, 2, 1, 1]);
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
