using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Tests.Parser;

public sealed class C2sAirStatementOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HoldContinuations_KeepTheirGroundParentsAfterHeadReordering(bool roundedCollision)
    {
        var first = new C.Tap { Tick = 0, Lane = 4, Width = 4 };
        var second = new C.Tap { Tick = 480, Lane = 4, Width = 4 };
        var end1 = roundedCollision ? 961 : 960;
        var end2 = roundedCollision ? 962 : 960;
        var head1 = Hold(first, 0, end1);
        var head2 = Hold(second, 480, end2);
        var tail1 = Hold(head1, end1, 1440);
        var tail2 = Hold(head2, end2, 1920);
        var chart = new C.Chart();
        chart.Notes.AddRange([first, second, head2, tail1, head1, tail2]);

        var parsed = await RoundTrip(chart);

        var tails = parsed.Notes.OfType<C.AirHold>().Where(n => n.Tick.Original == 960).ToArray();
        Assert.Equal(2, tails.Length);
        var endsByGround = tails.ToDictionary(n => ((C.AirHold)n.Parent!).Parent!.Tick.Original, n => n.EndTick.Original);
        Assert.Equal(1440, endsByGround[0]);
        Assert.Equal(1920, endsByGround[480]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlideContinuations_KeepTheirGroundParentsWhenHeadOrderDiffers(bool genericParents)
    {
        var first = new C.Tap { Tick = 0, Lane = 0, Width = 4 };
        var second = new C.Tap { Tick = 480, Lane = 8, Width = 4 };
        var head1 = Slide(genericParents ? new C.Tap() : first, 0, 0, 960, 4);
        var head2 = Slide(genericParents ? new C.Tap() : second, 480, 8, 960, 4);
        var tail1 = Slide(head1, 960, 4, 1440, 0);
        var tail2 = Slide(head2, 960, 4, 1920, 8);
        var chart = new C.Chart();
        chart.Notes.AddRange([first, second, head2, head1, tail2, tail1]);

        var parsed = await RoundTrip(chart);

        var tails = parsed.Notes.OfType<C.AirSlide>().Where(n => n.Tick.Original == 960).ToArray();
        Assert.Equal(2, tails.Length);
        var endsByGround = tails.ToDictionary(n => ((C.AirSlide)n.Parent!).Parent!.Tick.Original, n => n.EndTick.Original);
        Assert.Equal(1440, endsByGround[0]);
        Assert.Equal(1920, endsByGround[480]);
    }

    [Fact]
    public async Task TerminalAirSlide_WaitsUntilAnotherPathsContinuationHasPassed()
    {
        var first = new C.Tap { Tick = 480, Lane = 0, Width = 4 };
        var second = new C.Tap { Tick = 0, Lane = 8, Width = 4 };
        var terminal = Slide(first, 480, 0, 960, 4);
        var head = Slide(second, 0, 8, 960, 4);
        var continuation = Slide(head, 960, 4, 1440, 8);
        var chart = new C.Chart();
        chart.Notes.AddRange([first, second, terminal, head, continuation]);

        var parsed = await RoundTrip(chart);

        Assert.Equal([480, 0], parsed.Notes.OfType<C.Tap>().Select(n => n.Tick.Original));
        var tail = Assert.Single(parsed.Notes.OfType<C.AirSlide>(), n => n.Tick.Original == 960);
        var parent = Assert.IsType<C.AirSlide>(tail.Parent);
        Assert.Equal(0, parent.Tick.Original);
        Assert.Equal(8, parent.Parent!.Lane);
        Assert.Same(parent, parsed.Notes.OfType<C.AirSlide>().First());
    }

    [Fact]
    public async Task TerminalAirSlide_WithDifferentDecimalButEqualNativeHeight_DoesNotCaptureContinuation()
    {
        var first = new C.Tap { Tick = 480, Lane = 0, Width = 4 };
        var second = new C.Tap { Tick = 0, Lane = 8, Width = 4 };
        var terminal = Slide(first, 480, 0, 960, 4);
        terminal.EndHeight = 0.2m; // Displayed 1.01, native integer height 10.
        var head = Slide(second, 0, 8, 960, 4);
        head.EndHeight = 0.8m; // Displayed 1.04, also native integer height 10.
        var continuation = Slide(head, 960, 4, 1440, 8);
        continuation.Height = head.EndHeight;
        var chart = new C.Chart();
        chart.Notes.AddRange([first, second, terminal, head, continuation]);

        var parsed = await RoundTrip(chart);

        var tail = Assert.Single(parsed.Notes.OfType<C.AirSlide>(), n => n.Tick.Original == 960);
        var parent = Assert.IsType<C.AirSlide>(tail.Parent);
        Assert.Equal(0, parent.Tick.Original);
        Assert.Equal(8, parent.Parent!.Lane);
        Assert.Equal(1.04m, parent.EndHeight.Result);
        var other = Assert.Single(parsed.Notes.OfType<C.AirSlide>(), n => n.Tick.Original == 480);
        Assert.Equal(1.01m, other.EndHeight.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterleavedAirPathsWithSharedGroundEndpoint_KeepAllJoints(bool airHold)
    {
        var first = new C.Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 240,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        };
        var second = new C.Slide
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        };
        C.LongNote Part(C.Note parent, int tick, int end) => airHold
            ? new C.AirHold
            {
                Parent = parent,
                Tick = tick,
                EndTick = end,
                Lane = 8,
                Width = 4,
                EndLane = 8,
                EndWidth = 4
            }
            : Slide(parent, tick, 8, end, 8);
        var head1 = Part(first, 240, 480);
        var head2 = Part(second, 240, 720);
        var tail1 = Part(head1, 480, 960);
        var middle2 = Part(head2, 720, 960);
        var tail2 = Part(middle2, 960, 1200);
        var chart = new C.Chart();
        // Both heads bind before B passes through A's future terminal cell.
        chart.Notes.AddRange([first, second, head1, head2, middle2, tail2, tail1]);

        var parsed = await RoundTrip(chart);

        var actions = parsed.Notes.OfType<C.LongNote>().Where(n => n is C.AirHold or C.AirSlide).ToArray();
        int[] JointTicks(int groundLane)
        {
            var head = Assert.Single(actions, n => ((C.IPairable)n).Parent is C.Slide ground && ground.Lane == groundLane);
            var ticks = new List<int> { head.Tick.Original };
            C.LongNote? current = head;
            while (current is not null)
            {
                ticks.Add(current.EndTick.Original);
                current = actions.SingleOrDefault(n => ReferenceEquals(((C.IPairable)n).Parent, current));
            }
            return ticks.ToArray();
        }
        Assert.Equal(5, actions.Length);
        Assert.Equal([240, 480, 960], JointTicks(0));
        Assert.Equal([240, 720, 960, 1200], JointTicks(4));
    }

    [Fact]
    public async Task GroundSlideSegments_KeepTheirDeclaredOrder()
    {
        var root = new C.Slide { Tick = 0, Lane = 0, Width = 4, EndTick = 480, EndLane = 4, EndWidth = 4, Joint = Joint.C };
        var tail = new C.Slide { Tick = 480, Lane = 4, Width = 4, EndTick = 960, EndLane = 8, EndWidth = 4, Joint = Joint.D };
        var other = new C.Slide { Tick = 240, Lane = 12, Width = 4, EndTick = 1440, EndLane = 12, EndWidth = 4, Joint = Joint.D };
        var head = Slide(tail, 960, 8, 1440, 4);
        var continuation = Slide(head, 1440, 4, 1920, 0);
        var chart = new C.Chart();
        chart.Notes.AddRange([root, tail, other, continuation, head]);

        var parsed = await RoundTrip(chart);

        Assert.Equal([0, 480, 240], parsed.Notes.OfType<C.Slide>().Select(n => n.Tick.Original));
        var air = Assert.Single(parsed.Notes.OfType<C.AirSlide>(), n => n.Tick.Original == 960);
        Assert.Equal(480, air.Parent!.Tick.Original);
        Assert.Same(air, parsed.Notes.OfType<C.AirSlide>().Last().Parent);
    }

    private static C.AirHold Hold(C.Note parent, int tick, int end) => new()
    {
        Parent = parent,
        Tick = tick,
        EndTick = end,
        Lane = 4,
        Width = 4,
        EndLane = 4,
        EndWidth = 4
    };

    private static C.AirSlide Slide(C.Note parent, int tick, int lane, int end, int endLane) => new()
    {
        Parent = parent,
        Tick = tick,
        EndTick = end,
        Lane = lane,
        Width = 4,
        EndLane = endLane,
        EndWidth = 4,
        Joint = Joint.D
    };

    private static async Task<C.Chart> RoundTrip(C.Chart chart)
    {
        var path = TestTempPaths.Create(".c2s");
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Diagnostics.Diagnostics);
            return parsed.Value!;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
