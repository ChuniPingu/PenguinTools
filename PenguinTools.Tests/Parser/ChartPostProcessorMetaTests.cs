using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using Xunit;
using UmgrChart = PenguinTools.Chart.Models.umgr.Chart;

namespace PenguinTools.Tests.Parser;

public sealed class ChartPostProcessorMetaTests
{
    [Fact]
    public void BackgroundMeta_SupportsQuotedRelativePaths_AndOverridesStage()
    {
        var chartPath = Path.Combine(Path.GetTempPath(), "charts", "chart.ugc");
        var chart = CreateChart(
            "#meta bg \"images/My Background.png\"\n" +
            "#meta stage 8");
        chart.Meta.FilePath = chartPath;
        chart.Meta.BgiFilePath = "native-background.png";
        chart.Meta.IsCustomStage = true;

        new ChartPostProcessor(chart, new DiagnosticCollector(), TestAssets.Load()).Run();

        Assert.True(chart.Meta.IsCustomStage);
        Assert.Equal("images/My Background.png", chart.Meta.BgiFilePath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(chartPath)!, "images", "My Background.png")),
            chart.Meta.FullBgiFilePath);
    }

    [Fact]
    public void BackgroundMeta_OverridesStage_RegardlessOfCommandOrder()
    {
        var chart = CreateChart(
            "#meta stage 8\n" +
            "#meta bg \"Custom Background.png\"");

        new ChartPostProcessor(chart, new DiagnosticCollector(), TestAssets.Load()).Run();

        Assert.True(chart.Meta.IsCustomStage);
        Assert.Equal("Custom Background.png", chart.Meta.BgiFilePath);
    }

    [Fact]
    public void BackgroundOffset_DefaultsTo160_AndCanBeOverridden()
    {
        Assert.Equal(160, new Meta().BackgroundOffset);
        var chart = CreateChart("#meta bg_offset 240");

        new ChartPostProcessor(chart, new DiagnosticCollector(), TestAssets.Load()).Run();

        Assert.Equal(240, chart.Meta.BackgroundOffset);
    }

    private static UmgrChart CreateChart(string comment)
    {
        var chart = new UmgrChart();
        chart.Meta.Comment = comment;
        chart.Events.AppendChild(new BpmEvent { Tick = 0, Bpm = 120 });
        return chart;
    }
}
