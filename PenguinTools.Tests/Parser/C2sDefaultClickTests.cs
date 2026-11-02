using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class C2sDefaultClickTests
{
    [Theory]
    [InlineData("c2s")]
    [InlineData("mgxc")]
    [InlineData("ugc")]
    public async Task MissingClickCutoff_KeepsAutomaticClicksDisabledAndRetainsExplicitClicks(string format)
    {
        var input = TestTempPaths.Create(".c2s");
        var editorPath = TestTempPaths.Create("." + format);
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input, """
                VERSION	1.15.00	1.15.00
                BPM	0	0	120
                MET	0	0	4	4
                CLK	0	48
                TAP	0	192	0	4
                """, ct);
            var parsed = await new C2SParser(new(input)).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.False(parsed.Value!.Extras.ClickEnabled);
            var chart = parsed.Value!;

            if (format != "c2s")
            {
                var converted = new UgcChartConverter(new(chart)).Convert();
                Assert.True(converted.Succeeded);
                var written = format == "mgxc"
                    ? await new MgxcChartWriter(new(editorPath, converted.Value!)).WriteAsync(ct)
                    : await new UgcChartWriter(editorPath, converted.Value!).WriteAsync(ct);
                Assert.True(written.Succeeded);
                var editor = format == "mgxc"
                    ? await new MgxcParser(new(editorPath, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                    : await new UgcParser(new(editorPath, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                Assert.True(editor.Succeeded);
                var restored = new C2SChartConverter(new(editor.Value!)).Convert();
                Assert.True(restored.Succeeded);
                chart = restored.Value!;
            }

            Assert.True((await new C2SChartWriter(new(output, chart)).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(output, ct);
            Assert.Contains("CLK_DEF\t0", lines);
            Assert.Contains("CLK\t0\t48", lines);
        }
        finally
        {
            File.Delete(input);
            File.Delete(editorPath);
            File.Delete(output);
        }
    }
}
