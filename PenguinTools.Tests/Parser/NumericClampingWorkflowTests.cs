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

public sealed class NumericClampingWorkflowTests
{
    [Theory]
    [InlineData("Infinity", "Infinity", "1e-29", true)]
    [InlineData("NaN", "-Infinity", "-1e-29", true)]
    [InlineData("120", "1e100", "1", true)]
    [InlineData("120", "10000", "100000", false)]
    [InlineData("120", "100000", "10000", false)]
    public async Task EditorRoundTrips_KeepAdjustedNumbersAndPreserveRepresentableSpeeds(
        string bpm, string speed, string modifier, bool adjusted)
    {
        var ct = TestContext.Current.CancellationToken;
        foreach (var binary in new[] { false, true })
        {
            var input = TestTempPaths.Create(".c2s");
            var editor = TestTempPaths.Create(binary ? ".mgxc" : ".ugc");
            var output = TestTempPaths.Create(".c2s");
            try
            {
                await File.WriteAllTextAsync(input, $"""
                    VERSION 1.15.00 1.15.00
                    RESOLUTION 384
                    BPM_DEF {bpm} {bpm} {bpm} {bpm}
                    MET_DEF 4 4
                    BPM 0 0 {bpm}
                    MET 0 0 4 4
                    SLP 0 0 3840 {speed} 0
                    SLP 0 192 3840 {speed} 0
                    DCM 0 0 3840 {modifier}
                    HLD 0 0 0 4 384
                    """, ct);
                var source = await new C2SParser(new(input)).ParseAsync(ct);
                Assert.True(source.Succeeded, source.ToString());
                Assert.Equal(adjusted, source.Diagnostics.Diagnostics.Any(d => d.Message.Key == MsgKeys.Warn_Number_adjusted));
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
                var text = await File.ReadAllTextAsync(output, ct);
                foreach (var invalid in new[] { "Infinity", "NaN", "1e100", "1e-29" })
                {
                    Assert.DoesNotContain(invalid, text);
                }
                var rows = text.Split('\n').Select(C2SRoundTrip.Parts).ToArray();
                Assert.All(rows.Where(p => p.Length > 0 && p[0] == "BPM"),
                    p => Assert.True(decimal.Parse(p[3], CultureInfo.InvariantCulture) > 0));
                var actualModifier = decimal.Parse(Assert.Single(rows, p => p.Length > 0 && p[0] == "DCM")[4],
                    CultureInfo.InvariantCulture);
                Assert.Equal(modifier.StartsWith('-') ? -1 : 1, Math.Sign(actualModifier));
                if (!adjusted)
                {
                    Assert.Equal(decimal.Parse(modifier, CultureInfo.InvariantCulture), actualModifier);
                    Assert.All(rows.Where(p => p.Length > 0 && p[0] == "SLP"),
                        p => Assert.Equal(decimal.Parse(speed, CultureInfo.InvariantCulture),
                            decimal.Parse(p[4], CultureInfo.InvariantCulture)));
                    Assert.DoesNotContain(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Number_adjusted);
                    Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Number_adjusted);
                }
            }
            finally
            {
                File.Delete(input);
                File.Delete(editor);
                File.Delete(output);
            }
        }
    }
}
