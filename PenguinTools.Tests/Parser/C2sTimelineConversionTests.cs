using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Core.Metadata;
using PenguinTools.Core;
using System.Text;
using Xunit;
using C2sChart = PenguinTools.Chart.Models.c2s.Chart;

namespace PenguinTools.Tests.Parser;

public sealed class C2sTimelineConversionTests
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
    public void Sla_IsHalfOpen_EndTickIsExcluded()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap { Tick = 0, Lane = 2, Width = 2 });
        source.Notes.Add(new Tap { Tick = 480, Lane = 2, Width = 2 });
        source.Notes.Add(new Sla { Tick = 0, Length = 480, Lane = 0, Width = 8, Timeline = 5 });

        var notes = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!.Notes.Children
            .OfType<PenguinTools.Chart.Models.umgr.Tap>().OrderBy(n => n.Tick.Original).ToArray();

        Assert.Equal(5, notes[0].Timeline);
        Assert.Equal(0, notes[1].Timeline);
    }

    [Fact]
    public void Sla_RequiresNoteFullyContainedInLanes()
    {
        var source = new C2sChart();
        // Fully inside lanes 2..6
        source.Notes.Add(new Tap { Tick = 0, Lane = 2, Width = 2 });
        // Overlaps left edge but starts outside
        source.Notes.Add(new Tap { Tick = 0, Lane = 0, Width = 3 });
        // Overlaps right edge but extends past
        source.Notes.Add(new Tap { Tick = 0, Lane = 4, Width = 4 });
        source.Notes.Add(new Sla { Tick = 0, Length = 480, Lane = 2, Width = 4, Timeline = 9 });

        var notes = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!.Notes.Children
            .OfType<PenguinTools.Chart.Models.umgr.Tap>().OrderBy(n => n.Lane).ToArray();

        Assert.Equal(0, notes[0].Timeline); // lane 0..3 only overlaps
        Assert.Equal(9, notes[1].Timeline); // lane 2..4 fully inside
        Assert.Equal(0, notes[2].Timeline); // lane 4..8 only overlaps
    }

    [Fact]
    public void DebugTil_EmitsTransparentAirCrushForOriginalSla()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap { Tick = 0, Lane = 1, Width = 2 });
        source.Notes.Add(new Sla { Tick = 100, Length = 200, Lane = 2, Width = 4, Timeline = 3 });

        var chart = new UgcChartConverter(new UgcConvertRequest(source, DebugTil: true)).Convert().Value!;
        var crash = Assert.Single(chart.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>());

        Assert.Equal(Color.NON, crash.Color);
        Assert.Equal(0m, crash.Height);
        Assert.Equal(0, crash.Timeline);
        Assert.Equal(0, crash.Density.Original);
        Assert.Equal(100, crash.Tick.Original);
        Assert.Equal(2, crash.Lane);
        Assert.Equal(4, crash.Width);

        var joint = Assert.Single(crash.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrashJoint>());
        Assert.Equal(300, joint.Tick.Original);
        Assert.Equal(0m, joint.Height);
        Assert.Equal(0, joint.Timeline);
    }

    [Fact]
    public void DebugTil_Off_DoesNotEmitSlaMarkers()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap { Tick = 0, Lane = 1, Width = 2 });
        source.Notes.Add(new Sla { Tick = 100, Length = 200, Lane = 2, Width = 4, Timeline = 3 });

        var chart = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        Assert.Empty(chart.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>());
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
    public void Converter_KeepsSharedTickSpeedPointsOnDistinctTimelines()
    {
        var source = new C2sChart();
        source.Events.Add(new Met { Tick = 0, Numerator = 4, Denominator = 4 });
        source.Events.Add(new Bpm { Tick = 0, Value = 160m });
        source.Events.Add(new Slp { Tick = 0, Length = 480, Speed = 1000m, Timeline = 14 });
        source.Events.Add(new Slp { Tick = 480, Length = 960, Speed = 0m, Timeline = 14 });
        source.Events.Add(new Slp { Tick = 1439, Length = 1, Speed = 24m, Timeline = 14 });
        source.Events.Add(new Slp { Tick = 0, Length = 480, Speed = 1000m, Timeline = 15 });
        source.Events.Add(new Slp { Tick = 480, Length = 1440, Speed = 0m, Timeline = 15 });
        source.Events.Add(new Slp { Tick = 1919, Length = 1, Speed = 48m, Timeline = 15 });
        source.Notes.Add(new Tap { Tick = 1440, Lane = 6, Width = 4 });
        source.Notes.Add(new Tap { Tick = 1920, Lane = 3, Width = 4 });
        source.Notes.Add(new Sla { Tick = 1200, Length = 480, Lane = 6, Width = 4, Timeline = 14 });
        source.Notes.Add(new Sla { Tick = 1680, Length = 480, Lane = 3, Width = 4, Timeline = 15 });

        var chart = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        var scrolls = chart.Events.Children.OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>().ToArray();

        Assert.Contains(scrolls, x => x is { Timeline: 14, Tick.Original: 0, Speed: 1000m });
        Assert.Contains(scrolls, x => x is { Timeline: 15, Tick.Original: 0, Speed: 1000m });
        Assert.Contains(scrolls, x => x is { Timeline: 14, Tick.Original: 480, Speed: 0m });
        Assert.Contains(scrolls, x => x is { Timeline: 15, Tick.Original: 480, Speed: 0m });
        Assert.Contains(scrolls, x => x is { Timeline: 14, Tick.Original: 1439, Speed: 24m });
        Assert.Contains(scrolls, x => x is { Timeline: 15, Tick.Original: 1919, Speed: 48m });
    }

    [Fact]
    public async Task MgxcWriter_IsDeterministicAndEmitsTimelines()
    {
        var source = new C2sChart { Meta = new Meta { Title = "Reverse", MainBpm = 120m } };
        source.Events.Add(new Met { Tick = 0, Numerator = 4, Denominator = 4 });
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Events.Add(new Slp { Tick = 0, Length = 480, Speed = 2m, Timeline = 4 });
        source.Notes.Add(new Tap { Tick = 0, Lane = 1, Width = 2 });
        source.Notes.Add(new Flick { Tick = 480, Lane = 1, Width = 2 });
        source.Notes.Add(new AirCrash
        {
            Tick = 480, Lane = 2, Width = 2, EndTick = 960, EndLane = 3, EndWidth = 2,
            Height = 80m, EndHeight = 60m, Density = 20
        });
        source.Notes.Add(new Sla { Tick = 0, Length = 10, Lane = 1, Width = 2, Timeline = 4 });
        var chart = new UgcChartConverter(new UgcConvertRequest(source)).Convert().Value!;
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "first.mgxc");
        var second = Path.Combine(directory, "second.mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new MgxcWriteRequest(first, chart)).WriteAsync(ct);
            await new MgxcChartWriter(new MgxcWriteRequest(second, chart)).WriteAsync(ct);

            Assert.Equal(await File.ReadAllBytesAsync(first, ct), await File.ReadAllBytesAsync(second, ct));

            var parsed = await new MgxcParser(new MgxcParseRequest(first, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Contains(parsed.Value!.Events.Children.OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>(),
                x => x is { Timeline: 4, Tick.Original: 0, Speed: 2m });
            Assert.Contains(parsed.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Flick>(),
                x => x is { Lane: 1, Width: 2 });
            Assert.Contains(parsed.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>(),
                x => x.Children.Count > 0);
            Assert.Contains(parsed.Value.Notes.Children, n => n.Timeline == 4);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SlaSnapshot_IsDroppedWhenNonZeroTimelinesChange()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Timeline = 1
        });
        source.Notes.Add(new Sla
        {
            Tick = 0,
            Timeline = 1,
            Lane = 0,
            Width = 4,
            Length = 480
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());
        Assert.NotNull(converted.Value!.Meta.C2sSlaSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sSlaEditKey);

        var tap = Assert.Single(
            converted.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Tap>());
        tap.Timeline = 2;

        var roundTrip = new C2SChartConverter(
            new C2SConvertRequest(converted.Value)).Convert();

        Assert.True(roundTrip.Succeeded, roundTrip.ToString());
        Assert.Null(roundTrip.Value!.Meta.C2sSlaSnapshot);
        Assert.Null(roundTrip.Value.Meta.C2sSlaEditKey);
    }

    [Fact]
    public void SlpSnapshot_IsPreservedWhenSpeedScaleChangesOnly()
    {
        var source = new C2sChart();
        source.Events.Add(new Slp
        {
            Tick = 0,
            Timeline = 0,
            Length = 480,
            Speed = 0.750000m
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());
        Assert.NotNull(converted.Value!.Meta.C2sSlpSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sSlpEditKey);

        var speed = Assert.Single(
            converted.Value.Events.Children
                .OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>(),
            x => x.Tick.Original == 0 &&
                 x.Timeline == 0);

        speed.Speed = 0.75m;

        var roundTrip = new C2SChartConverter(
            new C2SConvertRequest(converted.Value)).Convert();

        Assert.True(roundTrip.Succeeded, roundTrip.ToString());
        Assert.NotNull(roundTrip.Value!.Meta.C2sSlpSnapshot);
        Assert.NotNull(roundTrip.Value.Meta.C2sSlpEditKey);

        var slp = Assert.Single(
            roundTrip.Value.Events.OfType<Slp>());

        Assert.Equal(0.75m, slp.Speed);
        Assert.Equal(480, slp.Length.Original);
    }

    [Fact]
    public void SlpSnapshot_IsDroppedWhenSpeedValueChanges()
    {
        var source = new C2sChart();
        source.Events.Add(new Slp
        {
            Tick = 0,
            Timeline = 0,
            Length = 480,
            Speed = 0.750000m
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());
        Assert.NotNull(converted.Value!.Meta.C2sSlpSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sSlpEditKey);

        var speed = Assert.Single(
            converted.Value.Events.Children
                .OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>(),
            x => x.Tick.Original == 0 &&
                 x.Timeline == 0);

        speed.Speed = 0.751m;

        var roundTrip = new C2SChartConverter(
            new C2SConvertRequest(converted.Value)).Convert();

        Assert.True(roundTrip.Succeeded, roundTrip.ToString());
        Assert.Null(roundTrip.Value!.Meta.C2sSlpSnapshot);
        Assert.Null(roundTrip.Value.Meta.C2sSlpEditKey);
    }
}
