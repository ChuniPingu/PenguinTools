using System.Globalization;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using Xunit;
using C2sChart = PenguinTools.Chart.Models.c2s.Chart;

namespace PenguinTools.Tests.Parser;

public sealed class ChartCultureTests
{
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ar-EG")]
    [InlineData("tr-TR")]
    public void FinalizeText_UsesInvariantBpmHeaders(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var chart = CreateChart();

            var text = chart.Extras.FinalizeText(chart, """
                RESOLUTION	384
                BPM_DEF	120.000	120.000	120.000	120.000
                BPM	0	0	193.125
                TAP	0	0	0	4
                """);

            Assert.Contains("BPM_DEF\t193.125\t193.125\t193.125\t193.125", text, StringComparison.Ordinal);
            Assert.Contains("T_JUDGE_TAP", text, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ar-EG")]
    [InlineData("tr-TR")]
    public async Task Writers_ProduceIdenticalBytesAcrossCultures(string cultureName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var expected = await WriteChartsAsync(directory, "invariant");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var actual = await WriteChartsAsync(directory, "localized");

            Assert.Equal(expected.C2s, actual.C2s);
            Assert.Equal(expected.Mgxc, actual.Mgxc);
            Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            Directory.Delete(directory, true);
        }
    }

    private static C2sChart CreateChart()
    {
        var chart = new C2sChart();
        chart.Meta.Id = 2999;
        chart.Meta.Level = 15.6m;
        chart.Meta.MainBpm = 193.125m;
        chart.Meta.BgmInitialNumerator = 4;
        chart.Meta.BgmInitialDenominator = 4;
        chart.Events.Add(new Bpm { Tick = 0, Value = 193.125m });
        chart.Notes.Add(new Tap { Tick = 0, Lane = -2, Width = 4 });
        return chart;
    }

    private static async Task<(byte[] C2s, byte[] Mgxc)> WriteChartsAsync(string directory, string name)
    {
        var chart = CreateChart();
        var c2sPath = Path.Combine(directory, name + ".c2s");
        var c2sResult = await new C2SChartWriter(new C2SWriteRequest(c2sPath, chart))
            .WriteAsync(TestContext.Current.CancellationToken);
        Assert.True(c2sResult.Succeeded, c2sResult.ToString());

        var converted = new UgcChartConverter(new UgcConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var mgxcPath = Path.Combine(directory, name + ".mgxc");
        var mgxcResult = await new MgxcChartWriter(new MgxcWriteRequest(mgxcPath, converted.Value!))
            .WriteAsync(TestContext.Current.CancellationToken);
        Assert.True(mgxcResult.Succeeded, mgxcResult.ToString());

        return (
            await File.ReadAllBytesAsync(c2sPath, TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(mgxcPath, TestContext.Current.CancellationToken));
    }
}
