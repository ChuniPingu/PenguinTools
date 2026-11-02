using System.Globalization;
using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class UgcNumericBoundaryTests
{
    [Theory]
    [InlineData("120.00000381469727", "1.0000000596046448")]
    [InlineData("239.99999237060547", "-1.0000000596046448")]
    [InlineData("120", "1e-7")]
    public async Task NativeNumbers_UseDoubleSemanticsBeforeC2sProjection(string bpm, string speed)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input, string.Join('\n', new[]
            {
                "@VER\t9", "@TICKS\t480", "@BEAT\t0\t4\t4", $"@BPM\t0'0\t{bpm}", $"@MAINBPM\t{bpm}",
                $"@TIL\t0\t0'0\t{speed}", $"@SPDMOD\t0'0\t{speed}", "@ENDHEAD", "#0'480:tG4"
            }), ct);
            var parsed = await new UgcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.Empty(parsed.Value!.Extras.SourceSnapshot);
            var native = new C2SChartConverter(new(parsed.Value)).Convert();
            Assert.True(native.Succeeded);
            Assert.True((await new C2SChartWriter(new(output, native.Value!)).WriteAsync(ct)).Succeeded);
            var rows = (await File.ReadAllLinesAsync(output, ct)).Select(C2SRoundTrip.Parts).ToArray();
            var expectedBpm = (float)double.Parse(bpm, CultureInfo.InvariantCulture);
            var expectedSpeed = (float)double.Parse(speed, CultureInfo.InvariantCulture);
            Assert.Equal(expectedBpm, float.Parse(Assert.Single(rows, p => p.Length > 0 && p[0] == "BPM")[3], CultureInfo.InvariantCulture));
            Assert.Equal(expectedBpm, float.Parse(Assert.Single(rows, p => p.Length > 0 && p[0] == "BPM_DEF")[1], CultureInfo.InvariantCulture));
            foreach (var kind in new[] { "SLP", "DCM" })
            {
                var speeds = rows.Where(p => p.Length > 4 && p[0] == kind)
                    .Select(p => float.Parse(p[4], CultureInfo.InvariantCulture)).ToArray();
                if (expectedSpeed != 1)
                {
                    Assert.NotEmpty(speeds);
                }
                Assert.All(speeds, actual => Assert.Equal(expectedSpeed, actual));
            }
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e100")]
    [InlineData("1e-40")]
    public async Task UnrepresentableNumber_WarnsAtTheNativeHeaderLineAndRemainsWritable(string value)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, $"@VER\t9\n@BPM\t0'0\t{value}\n@ENDHEAD\n#0'480:tG4", ct);
            var result = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);

            Assert.True(result.Succeeded);
            Assert.Equal(value is "Infinity" or "1e100" ? 100000m : 150m,
                Assert.Single(result.Value!.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BpmEvent>()).Bpm);
            var diagnostic = Assert.Single(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Number_adjusted);
            Assert.Equal(path, diagnostic.Path);
            Assert.Equal(2, diagnostic.Line);
            var converted = new C2SChartConverter(new(result.Value)).Convert();
            Assert.True(converted.Succeeded);
            Assert.True((await new C2SChartWriter(new(path, converted.Value!)).WriteAsync(ct)).Succeeded);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
