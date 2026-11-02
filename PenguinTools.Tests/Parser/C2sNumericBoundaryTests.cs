using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class C2sNumericBoundaryTests
{
    [Theory]
    [InlineData("1e-29")]
    [InlineData("-1e-29")]
    public async Task NonzeroSpeedOutsideDecimalRange_WarnsAndKeepsItsSign(string speed)
    {
        var input = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input,
                "VERSION\t1.15.00\t1.15.00\nBPM\t0\t0\t120\nMET\t0\t0\t4\t4\n" +
                $"DCM\t0\t0\t384\t{speed}\nTAP\t0\t192\t0\t4\n", ct);

            var result = await new C2SParser(new(input)).ParseAsync(ct);

            Assert.True(result.Succeeded);
            var adjusted = Assert.Single(result.Value!.Events.OfType<PenguinTools.Chart.Models.c2s.Dcm>()).Speed;
            Assert.Equal(speed.StartsWith('-') ? -0.0000000000000000000000000001m : 0.0000000000000000000000000001m, adjusted);
            Assert.DoesNotContain(speed, result.Value.Extras.InteropSourceText!);
            var diagnostic = Assert.Single(result.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Warn_Number_adjusted);
            Assert.Equal(Severity.Warning, diagnostic.Severity);
            Assert.Equal(input, diagnostic.Path);
            Assert.Equal(4, diagnostic.Line);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task BpmBeyondDecimalPrecision_RetainsNativeJudgementCount()
    {
        var input = TestTempPaths.Create(".c2s");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input, """
                VERSION	1.15.00	1.15.00
                BPM	0	0	239.999992370605468749999999999
                MET	0	0	4	4
                HLD	0	0	0	4	384
                """, ct);

            var parsed = await new C2SParser(new(input)).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            var written = await new C2SChartWriter(new(output, parsed.Value!)).WriteAsync(ct);

            Assert.True(written.Succeeded);
            Assert.Contains("T_JUDGE_HLD\t8", await File.ReadAllLinesAsync(output, ct));
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }
}
