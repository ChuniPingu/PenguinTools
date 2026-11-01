using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using C2sChart = PenguinTools.Chart.Models.c2s.Chart;

namespace PenguinTools.Tests.Parser;

public sealed class C2sEffectParsingTests
{
    [Theory]
    [InlineData("SXD", "SXD", false, ExEffect.UP)]
    [InlineData("SXC", "SXC", false, ExEffect.UP)]
    [InlineData("SXD", "SLD", false, ExEffect.UP)]
    [InlineData("SXC", "NCL", true, ExEffect.UP)]
    [InlineData("SXD", "UNKNOWN", false, ExEffect.UP)]
    [InlineData("SLD", "SXD", false, null)]
    [InlineData("SLC", "SXC", false, null)]
    [InlineData("SLD", "NCL", true, null)]
    [InlineData("SXD", "", false, ExEffect.UP)]
    [InlineData("SLD", "", false, null)]
    public async Task LinkMarkerWithoutEffect_UsesTheNoteTypeDefault(
        string type, string marker, bool noLine, ExEffect? expected)
    {
        var result = await Parse($"{type}\t0\t96\t0\t4\t96\t4\t4\t{marker}");

        Assert.True(result.Succeeded, result.ToString());
        var slide = Assert.Single(result.Value!.Notes.OfType<Slide>());
        Assert.Equal(expected, slide.Effect);
        Assert.Equal(noLine, slide.NoLine);
        Assert.Empty(result.Diagnostics.Diagnostics);
    }

    [Theory]
    [InlineData("SLD", false)]
    [InlineData("NCL", true)]
    [InlineData("SXD", false)]
    [InlineData("UNKNOWN", false)]
    [InlineData("UP", false)]
    public async Task ExplicitEffectField_TakesPrecedenceOverTheLinkMarker(string marker, bool noLine)
    {
        var result = await Parse($"SXD\t0\t96\t0\t4\t96\t4\t4\t{marker}\tDW");

        Assert.True(result.Succeeded, result.ToString());
        var slide = Assert.Single(result.Value!.Notes.OfType<Slide>());
        Assert.Equal(ExEffect.DW, slide.Effect);
        Assert.Equal(noLine, slide.NoLine);
        Assert.Empty(result.Diagnostics.Diagnostics);
    }

    [Theory]
    [InlineData(ExEffect.UP)]
    [InlineData(ExEffect.DW)]
    [InlineData(ExEffect.CE)]
    [InlineData(ExEffect.LC)]
    [InlineData(ExEffect.RC)]
    [InlineData(ExEffect.LS)]
    [InlineData(ExEffect.RS)]
    [InlineData(ExEffect.BS)]
    public async Task AbbreviatedLegacyEffect_StillParses(ExEffect effect)
    {
        var result = await Parse($"SXC\t0\t96\t0\t4\t96\t4\t4\t{effect}");

        Assert.True(result.Succeeded, result.ToString());
        var slide = Assert.Single(result.Value!.Notes.OfType<Slide>());
        Assert.Equal(effect, slide.Effect);
        Assert.False(slide.NoLine);
        Assert.Empty(result.Diagnostics.Diagnostics);
    }

    [Theory]
    [InlineData("SLD", false)]
    [InlineData("NCL", true)]
    [InlineData("SXD", false)]
    public async Task UnknownExplicitEffect_StillWarnsAndUsesUp(string marker, bool noLine)
    {
        var result = await Parse($"SXD\t0\t96\t0\t4\t96\t4\t4\t{marker}\tINVALID");

        Assert.True(result.Succeeded, result.ToString());
        var slide = Assert.Single(result.Value!.Notes.OfType<Slide>());
        Assert.Equal(ExEffect.UP, slide.Effect);
        Assert.Equal(noLine, slide.NoLine);
        var warning = Assert.Single(result.Diagnostics.Diagnostics);
        Assert.Equal(MsgKeys.C2s_Unknown_ex_effect, warning.Message.Key);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Equal(3, warning.Line);
        Assert.Equal("INVALID", warning.Message.Args!["arg0"]);
    }

    private static async Task<OperationResult<C2sChart>> Parse(string record)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "effects.c2s");
        try
        {
            await File.WriteAllTextAsync(path, $"VERSION\t1.15.00\t1.15.00\nRESOLUTION\t384\n{record}\n",
                TestContext.Current.CancellationToken);
            return await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
