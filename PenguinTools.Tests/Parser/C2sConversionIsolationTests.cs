using PenguinTools.Chart;
using PenguinTools.Chart.Converter.ugc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Tests.Parser;

public sealed class C2sConversionIsolationTests
{
    [Fact]
    public void Convert_LeavesSourceMetadataAndRoundTripStateUnchanged()
    {
        var source = CreateChart();
        var meta = source.Meta with { };
        var extras = source.Extras.ToCopyright();

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(meta, source.Meta);
        Assert.Equal(extras, source.Extras.ToCopyright());
        Assert.NotSame(source.Meta, result.Value!.Meta);
        Assert.NotSame(source.Extras, result.Value.Extras);
        Assert.True(result.Value.Meta.BgmFixedZeroOrigin);
        Assert.True(result.Value.Extras.C2sCoordinateOrigin);
        Assert.Single(result.Value.Extras.Speeds);
    }

    [Fact]
    public void Convert_EditingOneResultDoesNotAffectSourceOrAnotherResult()
    {
        var source = CreateChart();
        var first = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        var second = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;

        first.Meta.Designer = "Edited";
        first.Extras.Headers["PROGJUDGE_BPM"] = "PROGJUDGE_BPM\t480";
        first.Extras.Speeds.Clear();
        first.Extras.ClickTicks.Add(960);

        Assert.Equal("Source", source.Meta.Designer);
        Assert.Equal("Source", second.Meta.Designer);
        Assert.Equal("PROGJUDGE_BPM\t240", source.Extras.Headers["PROGJUDGE_BPM"]);
        Assert.Equal(source.Extras.Headers["PROGJUDGE_BPM"], second.Extras.Headers["PROGJUDGE_BPM"]);
        Assert.Single(second.Extras.Speeds);
        Assert.Equal([480], source.Extras.ClickTicks);
        Assert.Equal([480], second.Extras.ClickTicks);
    }

    private static C.Chart CreateChart()
    {
        var chart = new C.Chart();
        chart.Meta.Designer = "Source";
        chart.Meta.C2sJudgeTap = 1;
        chart.Meta.C2sJudgeHld = 0;
        chart.Meta.C2sJudgeSld = 0;
        chart.Meta.C2sJudgeAir = 0;
        chart.Meta.C2sJudgeFlk = 0;
        chart.Meta.C2sJudgeAll = 1;
        chart.Extras.Headers["PROGJUDGE_BPM"] = "PROGJUDGE_BPM\t240";
        chart.Extras.ClickTicks.Add(480);
        chart.Events.Add(new C.Bpm { Tick = 0, Value = 120 });
        chart.Events.Add(new C.Met { Tick = 0, Numerator = 4, Denominator = 4 });
        chart.Events.Add(new C.Slp { Tick = 0, Length = 1920, Timeline = 1, Speed = 2 });
        chart.Notes.Add(new C.Tap { Tick = 480, Lane = 0, Width = 4 });
        return chart;
    }
}
