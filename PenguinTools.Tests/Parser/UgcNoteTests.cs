using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser.ugc;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class UgcNoteTests
{
    private const string Header =
        "@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n";

    private static async Task<Chart.Models.umgr.Chart> Parse(string body)
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = Path.GetTempFileName() + ".ugc";
        await File.WriteAllTextAsync(tmp, Header + body, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded, r.ToString());
            return r.Value!;
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Tap_Simple()
    {
        var chart = await Parse("#0'480:t64\n");
        var tap = Assert.Single(chart.Notes.Children.OfType<Tap>());
        Assert.Equal(480, tap.Tick.Original);
        Assert.Equal(6, tap.Lane);
        Assert.Equal(4, tap.Width);
    }

    [Fact]
    public async Task ExTap_WithEffect()
    {
        var chart = await Parse("#0'480:x64U\n");
        var ex = Assert.Single(chart.Notes.Children.OfType<ExTap>());
        Assert.Equal(ExEffect.UP, ex.Effect);
    }

    [Fact]
    public async Task Flick_And_Damage()
    {
        var chart = await Parse("#0'480:f24\n#0'960:d24\n");
        Assert.Single(chart.Notes.Children.OfType<Flick>());
        Assert.Single(chart.Notes.Children.OfType<Damage>());
    }

    [Fact]
    public async Task Click_IsSkipped()
    {
        var chart = await Parse("#0'480:c\n");
        Assert.Empty(chart.Notes.Children);
    }

    [Fact]
    public async Task Hold_ParentAndEnd()
    {
        var chart = await Parse("#0'0:h64\n#480:s\n");
        var hold = Assert.Single(chart.Notes.Children.OfType<Hold>());
        Assert.Equal(0, hold.Tick.Original);
        var end = Assert.Single(hold.Children.OfType<HoldJoint>());
        Assert.Equal(480, end.Tick.Original);
    }

    [Fact]
    public async Task Slide_WithControlAndEnd()
    {
        var chart = await Parse("#0'0:s14\n#240:c24\n#480:s34\n");
        var slide = Assert.Single(chart.Notes.Children.OfType<Slide>());
        Assert.Equal(2, slide.Children.Count);
        var c = Assert.IsType<SlideJoint>(slide.Children[0]);
        var e = Assert.IsType<SlideJoint>(slide.Children[1]);
        Assert.Equal(Joint.C, c.Joint);
        Assert.Equal(Joint.D, e.Joint);
    }

    [Fact]
    public async Task Air_PairsWithPrecedingTap()
    {
        var chart = await Parse("#0'480:t64\n#0'480:a64ULI\n");
        var tap = Assert.Single(chart.Notes.Children.OfType<Tap>());
        var air = Assert.Single(chart.Notes.Children.OfType<Air>());
        Assert.Same(tap, air.PairNote);
        Assert.Equal(AirDirection.UL, air.Direction);
        Assert.Equal(Color.PNK, air.Color);
    }

    [Theory]
    [InlineData("UC", "AIR")]
    [InlineData("UL", "AUL")]
    [InlineData("UR", "AUR")]
    [InlineData("DC", "ADW")]
    [InlineData("DL", "ADL")]
    [InlineData("DR", "ADR")]
    public async Task Air_DirectionSurvivesC2sConversion(string ugcDirection, string c2sId)
    {
        var chart = await Parse($"#0'480:t64\n#0'480:a64{ugcDirection}I\n");

        var convert = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();

        Assert.True(convert.Succeeded, convert.ToString());
        var air = Assert.Single(convert.Value!.Notes.OfType<Chart.Models.c2s.Air>());
        Assert.Equal(c2sId, air.Id);
    }

    [Fact]
    public async Task AirHold_ParentAndChild()
    {
        var chart = await Parse("#0'0:t14\n#0'0:H14I\n#480:c\n");
        var hold = Assert.Single(chart.Notes.Children.OfType<AirHold>());
        Assert.Equal(Color.PNK, hold.Color);
        var child = Assert.Single(hold.Children.OfType<AirHoldJoint>());
        Assert.Equal(Joint.C, child.Joint);
    }

    [Fact]
    public async Task AirHold_IgnoresOptionalHeightAndConvertsToC2s()
    {
        var chart = await Parse("#0'0:t04\n#0'0:H0428N\n#480:s0428\n");
        var airHold = Assert.Single(chart.Notes.Children.OfType<AirHold>());
        Assert.Equal(Color.DEF, airHold.Color);
        Assert.Single(airHold.Children.OfType<AirHoldJoint>());

        var convert = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();

        Assert.True(convert.Succeeded, convert.ToString());
        var c2sAirHold = Assert.Single(convert.Value!.Notes.OfType<Chart.Models.c2s.AirHold>());
        Assert.Equal("AHD", c2sAirHold.Id);
        Assert.Equal(480, c2sAirHold.EndTick.Original);
        Assert.Equal(Joint.D, c2sAirHold.Joint);
    }

    [Fact]
    public async Task AirCrush_WithIntervalAndColor()
    {
        var chart = await Parse("#0'0:C140A1,$\n#480:c24ZZ\n");
        var crash = Assert.Single(chart.Notes.Children.OfType<AirCrash>());
        Assert.Equal(Color.RED, crash.Color);
        Assert.Equal(10m, crash.Height);
        Assert.Equal(0x7FFFFFFF, crash.Density.Original);
        var end = Assert.Single(crash.Children.OfType<AirCrashJoint>());
        Assert.Equal(1295m, end.Height);
    }

    [Fact]
    public async Task AirSlide_UsesTwoDigitHeightAndSpecChildSyntax()
    {
        var chart = await Parse("#0'0:t14\n#0'0:S140AI\n#480:s24ZZ\n");
        var airSlide = Assert.Single(chart.Notes.Children.OfType<AirSlide>());
        Assert.Equal(10m, airSlide.Height);
        Assert.Equal(Color.PNK, airSlide.Color);

        var child = Assert.Single(airSlide.Children.OfType<AirSlideJoint>());
        Assert.Equal(1295m, child.Height);
        Assert.Equal(Joint.D, child.Joint);
    }
}
