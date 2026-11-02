using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class UgcLowPrecisionHeightTests
{
    [Theory]
    [InlineData(8, false, "")]
    [InlineData(9, false, "Y")]
    [InlineData(9, true, "Z")]
    public async Task SingleDigitHeights_PreserveColorsAxesAndChildren(int version, bool omitFlag, string axis)
    {
        var path = TestTempPaths.Create(".ugc");
        var binary = TestTempPaths.Create(".mgxc");
        var rewritten = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        var lane = version == 9 ? "G" : "0";
        var endLane = version == 9 ? "K" : "4";
        var flag = omitFlag ? "" : "@FLAG\tHIPRECISION\tFALSE\n";
        try
        {
            await File.WriteAllTextAsync(path,
                $"@VER\t{version}\n@FLAG\tSOFFSET\tFALSE\n@BPM\t0'0\t120\n{flag}" +
                $"#0'0:t{lane}4\n#0'0:S{lane}48I\n#480>s{endLane}4Z\n" +
                $"#1'0:C{lane}48B{axis},240\n#480>c{endLane}4Z\n", ct);
            var parsed = await Read(path);
            Check(parsed, axis);
            var c2s = new C2SChartConverter(new(parsed)).Convert();
            Assert.True(c2s.Succeeded, c2s.ToString());
            var air = Assert.Single(c2s.Value!.Notes.OfType<C.AirSlide>());
            Assert.Equal(80, air.Height.Original);
            Assert.Equal(350, air.EndHeight.Original);

            Assert.True((await new MgxcChartWriter(new(binary, parsed)).WriteAsync(ct)).Succeeded);
            Assert.Equal("MGXC"u8.ToArray(), (await File.ReadAllBytesAsync(binary, ct))[..4]);
            var fromBinary = await new MgxcParser(new(binary, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(fromBinary.Succeeded, fromBinary.ToString());
            Check(fromBinary.Value!, axis);

            Assert.True((await new UgcChartWriter(rewritten, parsed).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(rewritten, ct);
            Assert.Contains("@FLAG\tHIPRECISION\tTRUE", lines);
            await File.WriteAllLinesAsync(rewritten, lines.Where(l => !l.StartsWith("@COPYRIGHT", StringComparison.Ordinal)), ct);
            Check(await Read(rewritten), axis);
        }
        finally
        {
            File.Delete(path);
            File.Delete(binary);
            File.Delete(rewritten);
        }
    }

    private static void Check(U.Chart chart, string axis)
    {
        var air = Assert.Single(chart.Notes.Children.OfType<U.AirSlide>());
        Assert.Equal(80, air.Height);
        Assert.Equal(Color.PNK, air.Color);
        Assert.Equal(350, Assert.IsType<U.AirSlideJoint>(Assert.Single(air.Children)).Height);
        var crash = Assert.Single(chart.Notes.Children.OfType<U.AirCrash>());
        Assert.Equal(80, crash.Height);
        Assert.Equal(Color.VLT, crash.Color);
        Assert.Equal(axis switch { "Y" => AirLadderAttr.AxisY, "Z" => AirLadderAttr.AxisZ, _ => AirLadderAttr.DEF }, crash.Attr);
        Assert.Equal(240, crash.Density.Original);
        Assert.Equal(350, Assert.IsType<U.AirCrashJoint>(Assert.Single(crash.Children)).Height);
    }

    private static async Task<U.Chart> Read(string path)
    {
        var parsed = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance)
            .ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        return parsed.Value!;
    }
}
