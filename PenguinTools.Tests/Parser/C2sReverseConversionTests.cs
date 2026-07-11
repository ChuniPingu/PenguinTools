using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Core.Metadata;
using PenguinTools.Core;
using Xunit;
using C2sChart = PenguinTools.Chart.Models.c2s.Chart;

namespace PenguinTools.Tests.Parser;

public sealed class C2sReverseConversionTests
{
    [Fact]
    public void Sla_UsesHighestTimelineForNotesAndLongJoints()
    {
        var source = new C2sChart();
        source.Notes.Add(new Slide { Tick = 0, Lane = 2, Width = 3, EndTick = 480, EndLane = 4, EndWidth = 2 });
        source.Notes.Add(new Sla { Tick = 0, Length = 480, Lane = 0, Width = 8, Timeline = 2 });
        source.Notes.Add(new Sla { Tick = 480, Length = 100, Lane = 4, Width = 2, Timeline = 7 });

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded);
        var slide = Assert.IsType<PenguinTools.Chart.Models.umgr.Slide>(Assert.Single(result.Value!.Notes.Children));
        Assert.Equal(2, slide.Timeline);
        Assert.Equal(7, Assert.Single(slide.Children).Timeline);
    }

    [Fact]
    public void SpeedDurations_RestoreDefaultOrActiveSpeed()
    {
        var source = new C2sChart();
        source.Events.Add(new Slp { Tick = 0, Length = 960, Speed = 2m, Timeline = 3 });
        source.Events.Add(new Slp { Tick = 240, Length = 240, Speed = 4m, Timeline = 3 });
        source.Events.Add(new Dcm { Tick = 100, Length = 200, Speed = .5m });

        var target = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        var scroll = target.Events.Children.OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>().ToArray();
        Assert.Contains(scroll, x => x.Tick.Original == 480 && x.Speed == 2m);
        Assert.Contains(scroll, x => x.Tick.Original == 960 && x.Speed == 1m);
        Assert.Contains(target.Events.Children.OfType<PenguinTools.Chart.Models.umgr.NoteSpeedEvent>(),
            x => x.Tick.Original == 300 && x.Speed == 1m);
    }

    [Fact]
    public async Task UgcWriter_IsDeterministicUtf8AndEmitsTimelines()
    {
        var source = new C2sChart { Meta = new Meta { Title = "Reverse", MainBpm = 120m } };
        source.Events.Add(new Met { Tick = 0, Numerator = 4, Denominator = 4 });
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Events.Add(new Slp { Tick = 0, Length = 480, Speed = 2m, Timeline = 4 });
        source.Notes.Add(new Tap { Tick = 0, Lane = 1, Width = 2 });
        source.Notes.Add(new AirCrash
        {
            Tick = 480, Lane = 2, Width = 2, EndTick = 960, EndLane = 3, EndWidth = 2,
            Height = 80m, EndHeight = 60m, Density = 20
        });
        source.Notes.Add(new Sla { Tick = 0, Length = 10, Lane = 1, Width = 2, Timeline = 4 });
        var chart = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var first = Path.Combine(directory, "first.ugc");
        var second = Path.Combine(directory, "second.ugc");
        var ct = TestContext.Current.CancellationToken;
        await new UgcChartWriter(new UgcWriteRequest(first, chart)).WriteAsync(ct);
        await new UgcChartWriter(new UgcWriteRequest(second, chart)).WriteAsync(ct);

        Assert.Equal(await File.ReadAllBytesAsync(first, ct), await File.ReadAllBytesAsync(second, ct));
        var text = await File.ReadAllTextAsync(first, ct);
        Assert.Contains("@VER\t8", text);
        Assert.Contains("@TIL\t4\t0'0\t2", text);
        Assert.Contains("@USETIL\t4", text);
        Assert.Contains("28", text); // 80 encoded as two-digit base 36.
    }

    [Fact]
    public async Task LegacySldMarker_IsNotAnEffect_AndParentsAreAssignedOneToOne()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "legacy.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            SLC	0	0	0	4	384	0	4	SLD
            SLC	0	0	0	4	384	0	4	SLD
            AUL	1	0	0	4	SLD	DEF
            AUR	1	0	0	4	SLD	DEF
            """, TestContext.Current.CancellationToken);

        var result = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        var slides = result.Value!.Notes.OfType<Slide>().ToArray();
        var airs = result.Value.Notes.OfType<Air>().ToArray();
        Assert.All(slides, slide => Assert.Null(slide.Effect));
        Assert.Equal(2, airs.Length);
        Assert.NotSame(airs[0].Parent, airs[1].Parent);
        Assert.DoesNotContain(result.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Key == MsgKeys.C2s_Unknown_ex_effect);
    }
}
