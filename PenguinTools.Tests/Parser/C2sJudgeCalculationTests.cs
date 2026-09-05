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

public sealed class C2sJudgeCalculationTests
{
    [Fact]
    public void C2sJudgeSummaryCalculator_CountsTapHeadsAndSlideRoots()
    {
        var source = new C2sChart();

        source.Notes.Add(new Tap
        {
            Tick = 0,
            Lane = 0,
            Width = 2
        });

        source.Notes.Add(new ExTap
        {
            Tick = 0,
            Lane = 2,
            Width = 2
        });

        source.Notes.Add(new Damage
        {
            Tick = 0,
            Lane = 4,
            Width = 2
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 6,
            Width = 2,
            EndTick = 480,
            EndLane = 6,
            EndWidth = 2
        });

        source.Notes.Add(new Flick
        {
            Tick = 0,
            Lane = 8,
            Width = 2
        });

        // Slide path #1, segment 1.
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 2,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 2,
            Joint = Joint.D
        });

        // Slide path #2.
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 8,
            Width = 2,
            EndTick = 480,
            EndLane = 10,
            EndWidth = 2,
            Joint = Joint.D
        });

        // Continuation of slide path #1.
        // This must NOT be counted as another TAP head.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 2,
            Width = 2,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 2,
            Joint = Joint.D
        });

        Assert.Equal(
            6,
            C2SJudgeSummaryCalculator.CalculateTap(source));

        Assert.Equal(
            1,
            C2SJudgeSummaryCalculator.CalculateFlick(source));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_ReconstructsOverlappingSlidesWithFifo()
    {
        var source = new C2sChart();

        // Two independent slide roots with exactly the same geometry.
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        // Continuation for the first open slide.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            EndTick = 960,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        // Continuation for the second open slide.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            EndTick = 960,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            2,
            C2SJudgeSummaryCalculator.CalculateTap(source));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_DoesNotConsumeNewRootAtSharedSlideJunction()
    {
        var source = new C2sChart();

        // Existing slide path.
        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        // Continuation of the existing path.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            EndTick = 960,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        // A completely new slide happens to begin at exactly
        // the same point as the continuation above.
        source.Notes.Add(new Slide
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            EndTick = 960,
            EndLane = 0,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            2,
            C2SJudgeSummaryCalculator.CalculateTap(source));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_DoesNotCountSharedLongCarrier()
    {
        var source = new C2sChart();

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 0,
            EndWidth = 4,
            Effect = ExEffect.UP
        });

        var toUmgr = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(toUmgr.Succeeded, toUmgr.ToString());

        // Simulate the auxiliary EX head used by overlapping EX long notes.
        // It must never become another real C2S CHR judgement head.
        toUmgr.Value!.Notes.AppendChild(
            new PenguinTools.Chart.Models.umgr.ExTap
            {
                Tick = 0,
                Lane = 0,
                Width = 4,
                Effect = ExEffect.UP,
                Role = PenguinTools.Chart.Models.umgr.ExTapRole.SharedLongCarrier
            });

        var backToC2s = new C2SChartConverter(
            new C2SConvertRequest(toUmgr.Value)).Convert();

        Assert.True(backToC2s.Succeeded, backToC2s.ToString());

        Assert.Equal(
            1,
            C2SJudgeSummaryCalculator.CalculateTap(backToC2s.Value!));

        Assert.Empty(
            backToC2s.Value!.Notes.OfType<ExTap>());
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_HoldProxyHandlesPairedAir()
    {
        var plainChart = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m
            }
        };

        plainChart.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        plainChart.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        });

        Assert.Equal(
            1,
            C2SJudgeSummaryCalculator.CalculateHoldProxy(
                plainChart));

        var airChart = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m
            }
        };

        airChart.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        var hold = new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        };

        airChart.Notes.Add(hold);

        airChart.Notes.Add(new Air
        {
            Tick = 240,
            Lane = 4,
            Width = 4,
            Parent = hold
        });

        Assert.Equal(
            0,
            C2SJudgeSummaryCalculator.CalculateHoldProxy(
                airChart));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_CountsAirRoots()
    {
        var chart = new C2sChart();
        var tap = new Tap { Tick = 0, Lane = 0, Width = 4 };
        chart.Notes.Add(tap);
        chart.Notes.Add(new Air
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Parent = tap
        });

        var hold = new Hold
        {
            Tick = 480,
            Lane = 0,
            Width = 4,
            EndTick = 960,
            EndLane = 0,
            EndWidth = 4
        };
        chart.Notes.Add(hold);

        var firstHold = new AirHold
        {
            Tick = 480,
            Lane = 0,
            Width = 4,
            EndTick = 720,
            EndLane = 0,
            EndWidth = 4,
            Parent = hold
        };
        var secondHold = new AirHold
        {
            Tick = 720,
            Lane = 0,
            Width = 4,
            EndTick = 960,
            EndLane = 0,
            EndWidth = 4,
            Parent = firstHold
        };
        chart.Notes.Add(firstHold);
        chart.Notes.Add(secondHold);

        Assert.Equal(2, C2SJudgeSummaryCalculator.CalculateAirProxy(chart));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_SlideProxyUsesWholePathDuration()
    {
        var singleSegmentChart = new C2sChart();

        singleSegmentChart.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 480,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            5,
            C2SJudgeSummaryCalculator.CalculateSlideProxy(
                singleSegmentChart));

        var splitSegmentChart = new C2sChart();

        splitSegmentChart.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 6,
            EndWidth = 4,
            Joint = Joint.D
        });

        splitSegmentChart.Notes.Add(new Slide
        {
            Tick = 240,
            Lane = 6,
            Width = 4,
            EndTick = 480,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            5,
            C2SJudgeSummaryCalculator.CalculateSlideProxy(
                splitSegmentChart));
    }

    [Fact]
    public void C2sJudgeSummaryCalculator_SlideProxyUsesFifoAtSharedJunction()
    {
        var chart = new C2sChart();

        chart.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 96,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        chart.Notes.Add(new Slide
        {
            Tick = 12,
            Lane = 8,
            Width = 4,
            EndTick = 96,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        chart.Notes.Add(new Slide
        {
            Tick = 96,
            Lane = 4,
            Width = 4,
            EndTick = 108,
            EndLane = 2,
            EndWidth = 4,
            Joint = Joint.D
        });

        chart.Notes.Add(new Slide
        {
            Tick = 96,
            Lane = 4,
            Width = 4,
            EndTick = 120,
            EndLane = 6,
            EndWidth = 4,
            Joint = Joint.D
        });

        Assert.Equal(
            4,
            C2SJudgeSummaryCalculator.CalculateSlideProxy(
                chart));
    }

    [Fact]
    public void UgcChartConverter_InitializesJudgeProxyBaselinesOnce()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 2,
                C2sJudgeSld = 3,
                C2sJudgeAir = 4,
                C2sJudgeFlk = 5,
                C2sJudgeAll = 15
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        });

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 8,
            Width = 4,
            EndTick = 480,
            EndLane = 12,
            EndWidth = 4,
            Joint = Joint.D
        });

        var first = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(
            first.Succeeded,
            first.ToString());

        Assert.Equal(
            1,
            first.Value!.Meta.C2sJudgeHldProxyBaseline);

        Assert.Equal(
            5,
            first.Value.Meta.C2sJudgeSldProxyBaseline);

        source.Meta.C2sJudgeHldProxyBaseline = 77;
        source.Meta.C2sJudgeSldProxyBaseline = 88;

        var second = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(
            second.Succeeded,
            second.ToString());

        Assert.Equal(
            77,
            second.Value!.Meta.C2sJudgeHldProxyBaseline);

        Assert.Equal(
            88,
            second.Value.Meta.C2sJudgeSldProxyBaseline);
    }
}
