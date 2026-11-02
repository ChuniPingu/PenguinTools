using System.Globalization;
using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class C2sNumericPrecisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoundTripRetainsTempoJudgementBoundaryAndNonzeroSpeeds(bool binary)
    {
        var input = TestTempPaths.Create(".c2s");
        var editor = TestTempPaths.Create(binary ? ".mgxc" : ".ugc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input, """
                VERSION 1.15.00 1.15.00
                RESOLUTION 384
                BPM_DEF 239.99999 239.99999 239.99999 239.99999
                MET_DEF 4 4
                BPM 0 0 239.99999
                MET 0 0 4 4
                SLP 0 0 384 0.0000001 0
                DCM 0 0 384 0.0000001
                HLD 0 0 0 4 384
                """, ct);
            var source = await new C2SParser(new(input)).ParseAsync(ct);
            Assert.True(source.Succeeded, source.ToString());
            var converted = new UgcChartConverter(new(source.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            var written = binary
                ? await new MgxcChartWriter(new(editor, converted.Value!)).WriteAsync(ct)
                : await new UgcChartWriter(editor, converted.Value!).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = binary
                ? await new MgxcParser(new(editor, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new(editor, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var native = new C2SChartConverter(new(parsed.Value!)).Convert();
            Assert.True(native.Succeeded, native.ToString());
            Assert.True((await new C2SChartWriter(new(output, native.Value!)).WriteAsync(ct)).Succeeded);
            AssertPrecision((await File.ReadAllLinesAsync(output, ct)).Select(C2SRoundTrip.Parts).ToArray());
        }
        finally
        {
            File.Delete(input);
            File.Delete(editor);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task AuthoredSpeedsRetainSmallDifferencesAndReportCoincidentConflicts()
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input, """
                @VER	9
                @TICKS	480
                @FLAG	SOFFSET	FALSE
                @BEAT	0	4	4
                @BPM	0'0	239.99999
                @TIL	0	0'0	0.0000001
                @TIL	1	0'0	0
                @SPDMOD	0'0	0.0000001
                @USETIL	0
                #0'0:hG4
                #1920>s
                #0'480:tK4
                @USETIL	1
                #0'480:tK4
                """, ct);
            var parsed = await new UgcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Value!.Extras.SourceSnapshot);
            var native = new C2SChartConverter(new(parsed.Value)).Convert();
            Assert.True(native.Succeeded, native.ToString());
            Assert.Single(native.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Note_overlapped_in_different_TIL);
            Assert.True((await new C2SChartWriter(new(output, native.Value!)).WriteAsync(ct)).Succeeded);
            AssertPrecision((await File.ReadAllLinesAsync(output, ct)).Select(C2SRoundTrip.Parts).ToArray());
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    private static void AssertPrecision(string[][] rows)
    {
        static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
        Assert.All(rows.Where(p => p.Length > 0 && p[0] == "BPM"), p => Assert.Equal(239.99999m, Number(p[3])));
        Assert.Equal(239.99999m, Number(Assert.Single(rows, p => p.Length > 0 && p[0] == "BPM_DEF")[1]));
        Assert.Contains(rows, p => p.Length > 4 && p[0] == "SLP" && Number(p[4]) == 0.0000001m);
        Assert.Contains(rows, p => p.Length > 4 && p[0] == "DCM" && Number(p[4]) == 0.0000001m);
        // The native float remains below 240, so the HOLD has eight judgements.
        Assert.Equal("8", Assert.Single(rows, p => p.Length > 0 && p[0] == "T_JUDGE_HLD")[1]);
    }
}
