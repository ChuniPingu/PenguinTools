using System.Diagnostics;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Media;
using Xunit;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public class MgxcRegressionTests
{
    [Fact]
    public void GetCalculator_WithOnlyBarBasedBeatEvents_FormatsAfterTimeSignatureChanges()
    {
        var chart = new UmgrModel.Chart();
        chart.Events.AppendChild(new UmgrModel.BeatEvent { Bar = 38, Numerator = 6, Denominator = 4 });
        chart.Events.AppendChild(new UmgrModel.BeatEvent { Bar = 64, Numerator = 4, Denominator = 4 });
        chart.Events.AppendChild(new UmgrModel.BeatEvent { Bar = 80, Numerator = 6, Denominator = 4 });

        var calculator = chart.GetCalculator();

        Assert.Equal("72:1.0", calculator.FormatTick(161280));
        Assert.Equal([0, 0, 0],
            chart.Events.Children.OfType<UmgrModel.BeatEvent>().Select(e => e.Tick.Original).ToArray());
    }

    [Fact]
    public async Task ParseKnownSample_StillProducesChart()
    {
        var masterMgxcPath = Path.Combine(ChartTestPaths.AssetsDirectory, "Ver seX.mgxc");
        if (!File.Exists(masterMgxcPath) && Directory.Exists(ChartTestPaths.AssetsDirectory))
        {
            masterMgxcPath = Directory.EnumerateFiles(ChartTestPaths.AssetsDirectory, "*.mgxc").FirstOrDefault() ?? masterMgxcPath;
        }

        Assert.SkipWhen(!File.Exists(masterMgxcPath), $"Optional chart sample is missing: {masterMgxcPath}");

        var assets = TestAssets.Load();
        var parser = new MgxcParser(new MgxcParseRequest(masterMgxcPath, assets), TestMediaTool.Instance);

        var result = await parser.ParseAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value!.Notes.Children);
    }

}
