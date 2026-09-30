using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.ugc;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class PayloadTests
{
    [Theory]
    [InlineData('0', 0)]
    [InlineData('9', 9)]
    [InlineData('a', 10)]
    [InlineData('z', 35)]
    [InlineData('A', 10)] // uppercase also accepted
    [InlineData('Z', 35)]
    public void Base36_ValidChars_MapsCorrectly(char c, int expected)
    {
        Assert.Equal(expected, UgcPayload.Base36(c));
    }

    [Theory]
    [InlineData('!')]
    [InlineData(' ')]
    public void Base36_InvalidChars_ReturnsMinusOne(char c)
    {
        Assert.Equal(-1, UgcPayload.Base36(c));
    }

    [Theory]
    [InlineData('U', ExEffect.UP)]
    [InlineData('D', ExEffect.DW)]
    [InlineData('C', ExEffect.CE)]
    [InlineData('L', ExEffect.LS)]
    [InlineData('R', ExEffect.RS)]
    [InlineData('A', ExEffect.LC)]
    [InlineData('W', ExEffect.RC)]
    [InlineData('I', ExEffect.BS)] // in-out burst
    public void ExEffectChar_MapsToExpectedEffect(char c, ExEffect expected)
    {
        Assert.Equal(expected, UgcPayload.ExEffectChar(c));
    }

    [Theory]
    [InlineData("UC", AirDirection.IR)]
    [InlineData("UL", AirDirection.UL)]
    [InlineData("UR", AirDirection.UR)]
    [InlineData("DC", AirDirection.DW)]
    [InlineData("DL", AirDirection.DL)]
    [InlineData("DR", AirDirection.DR)]
    public void AirDirectionCode_MapsToExpectedDirection(string code, AirDirection expected)
    {
        Assert.Equal(expected, UgcPayload.AirDirectionCode(code));
    }

    [Theory]
    [InlineData('N', Color.DEF)]
    [InlineData('I', Color.PNK)]
    public void AirColorChar_MapsToExpectedColor(char c, Color expected)
    {
        Assert.Equal(expected, UgcPayload.AirColorChar(c));
    }

    [Theory]
    [InlineData("00", 0)]
    [InlineData("0A", 10)]
    [InlineData("10", 36)]
    public void Height36_ParsesTwoDigitBase36(string s, decimal expected)
    {
        Assert.Equal(expected, UgcPayload.Height36(s));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("24", 24)]
    [InlineData("$", 0x7FFFFFFF)]
    public void AirCrashInterval_MapsExpectedDensity(string text, int expected)
    {
        Assert.Equal(expected, UgcPayload.AirCrashInterval(text));
    }
}
