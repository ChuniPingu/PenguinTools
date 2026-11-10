using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser;
using PenguinTools.Core;
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

    [Fact]
    public void IgnoreMeta_IsAKnownTag()
    {
        var diagnostics = new DiagnosticCollector();
        var chart = CreateChart("#meta ignore false");

        new ChartPostProcessor(chart, diagnostics, TestAssets.Load()).Run();

        Assert.DoesNotContain(diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Meta_Unknown_tag);
        Assert.DoesNotContain(diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Meta_Ignored);
    }

    [Fact]
    public void InvalidOptionalCommands_WarnAndKeepProcessingTheChart()
    {
        var chart = CreateChart("#meta\n#meta genre bad NewGenre\n#meta bg_offset bad\n#meta main false");
        chart.Meta.FilePath = "chart.mgxc";
        chart.Meta.Genre = new(7, "Original");
        chart.Meta.IsMain = true;
        var tap = new Tap { Tick = 480, Lane = 2, Width = 4 };
        chart.Notes.AppendChild(tap);
        var diagnostics = new DiagnosticCollector();

        new ChartPostProcessor(chart, diagnostics, TestAssets.Load()).Run();

        Assert.False(diagnostics.HasError);
        Assert.Equal(3, diagnostics.Diagnostics.Count(d => d.Message.Key == MsgKeys.Warn_Invalid_metadata &&
            d.Severity == Severity.Warning && d.Path == chart.Meta.FilePath));
        Assert.Equal("Original", chart.Meta.Genre.Str);
        Assert.Equal(160, chart.Meta.BackgroundOffset);
        Assert.False(chart.Meta.IsMain);
        Assert.Same(tap, Assert.Single(chart.Notes.Children));
    }

    [Fact]
    public void SignedAssetIdsInComments_CanBeDefinedAndReferenced()
    {
        var tags = new[] { "genre", "stage", "fline", "wetag" };
        var chart = CreateChart(string.Join("\n", tags.SelectMany(tag => new[]
        {
            $"#meta {tag} -2 Custom",
            $"#meta {tag} 0 Valid",
            $"#meta {tag} -2"
        })));
        var diagnostics = new DiagnosticCollector();

        new ChartPostProcessor(chart, diagnostics, TestAssets.Load()).Run();

        Assert.False(diagnostics.HasError);
        Assert.DoesNotContain(diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Invalid_metadata);
        Assert.All(new[] { chart.Meta.Genre!, chart.Meta.Stage, chart.Meta.NotesFieldLine, chart.Meta.WeTag },
            entry => { Assert.Equal(-2, entry.Id); Assert.Equal("Custom", entry.Str); });
    }

    private static UmgrChart CreateChart(string comment)
    {
        var chart = new UmgrChart();
        chart.Meta.Comment = comment;
        chart.Events.AppendChild(new BpmEvent { Tick = 0, Bpm = 120 });
        return chart;
    }
}
