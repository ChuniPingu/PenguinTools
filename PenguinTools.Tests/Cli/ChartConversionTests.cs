using PenguinTools.Chart.Parser.c2s;
using PenguinTools.CLI;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Tests.Cli;

public sealed class ChartConversionTests
{
    [Theory]
    [InlineData(".ugc", 3, 8)]
    [InlineData(".ugc", 7, 4)]
    [InlineData(".mgxc", 3, 8)]
    [InlineData(".mgxc", 7, 4)]
    public async Task DefaultConversion_PreservesNonFourQuarterTimingAcrossEditorRoundTrip(
        string extension, int numerator, int denominator)
    {
        var input = TestTempPaths.Create(".c2s");
        var editor = TestTempPaths.Create(extension);
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input,
                $"VERSION\t1.15.00\t1.15.00\nRESOLUTION\t384\nMET_DEF\t{denominator}\t{numerator}\n" +
                $"BPM\t0\t0\t120\nMET\t0\t0\t{denominator}\t{numerator}\n" +
                "BPM\t2\t0\t180\nTAP\t2\t0\t0\t4\n", ct);

            await ConvertAsync(input, editor, ct);
            await ConvertAsync(editor, output, ct);

            var parsed = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var chart = parsed.Value!;
            Assert.Equal(3840, Assert.Single(chart.Notes.OfType<C.Tap>()).Tick.Original);
            Assert.Equal(3840, Assert.Single(chart.Events.OfType<C.Bpm>(), b => b.Value == 180).Tick.Original);
            var meter = Assert.Single(chart.Events.OfType<C.Met>());
            Assert.Equal((0, numerator, denominator), (meter.Tick.Original, meter.Numerator, meter.Denominator));
        }
        finally
        {
            File.Delete(input);
            File.Delete(editor);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData(null, 2160)]
    [InlineData("Original", 2160)]
    [InlineData("Extended", 3840)]
    public async Task UnmarkedInput_UsesOriginalTimingUnlessExplicitlyOverridden(string? dialect, int expectedTick)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input,
                "@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120\n@BEAT\t0\t3\t8\n" +
                "@FLAG\tSOFFSET\tTRUE\n#2'0:t04\n", ct);

            await ConvertAsync(input, output, ct, dialect is null ? [] : ["--input-dialect", dialect]);

            var parsed = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(expectedTick, Assert.Single(parsed.Value!.Notes.OfType<C.Tap>()).Tick.Original);
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    private static async Task ConvertAsync(string input, string output, CancellationToken ct, params string[] options)
    {
        var parsed = RootCommands.BuildRootCommand().Parse(["chart", "convert", input, output, "--no-progress", .. options]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(CliExitCodes.Success, await parsed.InvokeAsync(cancellationToken: ct));
    }
}
