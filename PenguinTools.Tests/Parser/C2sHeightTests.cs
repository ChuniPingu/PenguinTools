using System.Globalization;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Core;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Tests.Parser;

public sealed class C2sHeightTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1.01", 10)]
    [InlineData("1.04", 10)]
    [InlineData("1.05", 11)]
    [InlineData("1.04999999", 11)]
    [InlineData("1.14999999", 12)]
    [InlineData("-1.01", -9)]
    [InlineData("-1.04", -9)]
    [InlineData("-1.05", -10)]
    [InlineData("214748350", 2147483520)]
    [InlineData("214748400", int.MaxValue)]
    [InlineData("-214748400", int.MinValue)]
    [InlineData("1000000000000", int.MaxValue)]
    public void NativeHeightKey_UsesFloatTenthsAndSaturatingIntegerConversion(string displayed, int expected)
    {
        var height = new Height((decimal.Parse(displayed, CultureInfo.InvariantCulture) - 1m) * 20m);

        Assert.Equal(expected, Chart.C2sHeight.Key(height));
    }

    [Theory]
    [InlineData("1.01", "1.04", true)]
    [InlineData("1.04", "1.05", false)]
    [InlineData("1.04999999", "1.05", true)]
    [InlineData("-1.01", "-1.04", true)]
    [InlineData("-1.04", "-1.05", false)]
    [InlineData("214748400", "1000000000000", true)]
    public async Task AirSlideContinuation_ComparesNativeHeightsAndRetainsEditablePrecision(
        string endHeight, string startHeight, bool attaches)
    {
        var path = TestTempPaths.Create(".c2s");
        try
        {
            await File.WriteAllTextAsync(path, $"""
                VERSION 1.15.00 1.15.00
                RESOLUTION 384
                TAP 0 0 0 4
                ASC 0 0 0 4 TAP 1.0 96 4 4 {endHeight} DEF
                ASD 0 96 4 4 ASC {startHeight} 96 8 4 5.0 DEF
                """, TestContext.Current.CancellationToken);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());
            var slides = parsed.Value!.Notes.OfType<C.AirSlide>().ToArray();
            Assert.Equal(decimal.Parse(endHeight, CultureInfo.InvariantCulture), slides[0].EndHeight.Result);
            Assert.Equal(decimal.Parse(startHeight, CultureInfo.InvariantCulture), slides[1].Height.Result);
            if (attaches)
            {
                Assert.Same(slides[0], slides[1].Parent);
                Assert.Empty(parsed.Diagnostics.Diagnostics);
            }
            else
            {
                Assert.Null(slides[1].Parent);
                var warning = Assert.Single(parsed.Diagnostics.Diagnostics);
                Assert.Equal(MsgKeys.C2s_Parent_not_resolved, warning.Message.Key);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
