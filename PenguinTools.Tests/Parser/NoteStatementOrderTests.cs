using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class NoteStatementOrderTests
{
    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task EditorAndC2sRoundTrips_PreserveNonChronologicalNotes(string extension)
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent());
        chart.Notes.AppendChild(new U.Flick { Tick = 960, Lane = 12, Width = 2 });
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = 4, Width = 2 });
        chart.Notes.AppendChild(new U.ExTap { Tick = 480, Lane = 8, Width = 2, Role = U.ExTapRole.Explicit });
        var editor = TestTempPaths.Create(extension);
        var c2s = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = extension == ".mgxc"
                ? await new MgxcChartWriter(new MgxcWriteRequest(editor, chart)).WriteAsync(ct)
                : await new UgcChartWriter(editor, chart).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = extension == ".mgxc"
                ? await new MgxcParser(new MgxcParseRequest(editor, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new UgcParseRequest(editor, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal([960, 0, 480], parsed.Value!.Notes.Children.Select(n => n.Tick.Original));

            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            var output = await new C2SChartWriter(new C2SWriteRequest(c2s, converted.Value!)).WriteAsync(ct);
            Assert.True(output.Succeeded, output.ToString());
            var read = await new C2SParser(new C2SParseRequest(c2s)).ParseAsync(ct);
            Assert.True(read.Succeeded, read.ToString());
            Assert.Equal([960, 0, 480], read.Value!.Notes.Select(n => n.Tick.Original));

            var restored = new UgcChartConverter(new UgcConvertRequest(read.Value)).Convert();
            Assert.True(restored.Succeeded, restored.ToString());
            Assert.Equal([960, 0, 480], restored.Value!.Notes.Children.Select(n => n.Tick.Original));
        }
        finally
        {
            File.Delete(editor);
            File.Delete(c2s);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReorderedAirRoots_KeepTheirContinuationsAfterTheParent(bool hold)
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent());
        var parents = new List<U.SlideJoint>();
        foreach (var lane in new[] { 0, 12 })
        {
            var slide = new U.Slide { Lane = lane, Width = 4 };
            var end = new U.SlideJoint { Tick = 480, Lane = 6, Width = 4, Joint = Joint.D };
            slide.AppendChild(end);
            chart.Notes.AppendChild(slide);
            parents.Add(end);
        }
        U.NegativeNote action;
        if (hold)
        {
            var air = new U.AirHold();
            air.AppendChild(new U.AirHoldJoint { Tick = 720, Joint = Joint.D });
            air.AppendChild(new U.AirHoldJoint { Tick = 960, Joint = Joint.D });
            action = air;
        }
        else
        {
            var air = new U.AirSlide { Height = 50 };
            air.AppendChild(new U.AirSlideJoint { Tick = 720, Lane = 4, Width = 4, Height = 50, Joint = Joint.C });
            air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 8, Width = 4, Height = 50, Joint = Joint.D });
            action = air;
        }
        parents[1].MakePair(action);
        chart.Notes.AppendChild(action);
        var arrow = new U.Air { Direction = AirDirection.IR };
        parents[0].MakePair(arrow);
        chart.Notes.AppendChild(arrow);

        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.All(converted.Value!.Notes.OfType<C.IPairable>(),
            n => Assert.Contains(n.Parent, converted.Value.Notes));
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var records = (await File.ReadAllLinesAsync(path, ct)).Select(line => line.Split('\t'))
                .Where(p => p[0] is "AIR" or "AHD" or "ASC" or "ASD").ToArray();
            Assert.Equal(["AIR", hold ? "AHD" : "ASC", hold ? "AHD" : "ASD"], records.Select(p => p[0]));
            Assert.Equal(["SLD", "SLD", hold ? "AHD" : "ASC"], records.Select(p => p[5]));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CrossingAirSlides_FollowGroundCreationOrderAtSharedCell()
    {
        var tap = new C.Tap { Lane = 0, Width = 4 };
        var slide = new C.Slide { Lane = 12, Width = 4, EndTick = 480, EndLane = 12, EndWidth = 4, Joint = Joint.D };
        var first = new C.AirSlide
        {
            Lane = 0,
            Width = 4,
            Parent = tap,
            Height = 50,
            EndTick = 960,
            EndLane = 6,
            EndWidth = 4,
            EndHeight = 50,
            Joint = Joint.C
        };
        var firstTail = new C.AirSlide
        {
            Tick = 960,
            Lane = 6,
            Width = 4,
            Parent = first,
            Height = 50,
            EndTick = 1440,
            EndLane = 0,
            EndWidth = 4,
            EndHeight = 50,
            Joint = Joint.D
        };
        var second = new C.AirSlide
        {
            Tick = 480,
            Lane = 12,
            Width = 4,
            Parent = slide,
            Height = 50,
            EndTick = 960,
            EndLane = 6,
            EndWidth = 4,
            EndHeight = 50,
            Joint = Joint.C
        };
        var secondTail = new C.AirSlide
        {
            Tick = 960,
            Lane = 6,
            Width = 4,
            Parent = second,
            Height = 50,
            EndTick = 1920,
            EndLane = 12,
            EndWidth = 4,
            EndHeight = 50,
            Joint = Joint.D
        };
        var chart = new C.Chart();
        chart.Notes.AddRange([tap, slide, first, firstTail, second, secondTail]);
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var records = (await File.ReadAllLinesAsync(path, ct)).Select(line => line.Split('\t'))
                .Where(p => p[0] == "ASD").ToArray();
            Assert.Equal(["0", "12"], records.Select(p => p[8]).Order());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Diagnostics.Diagnostics);
            foreach (var tail in parsed.Value!.Notes.OfType<C.AirSlide>().Where(n => n.Joint == Joint.D))
            {
                var root = Assert.IsType<C.AirSlide>(tail.Parent);
                Assert.Equal(tail.EndLane, root.Lane);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MeetingAirHolds_UseGroundOrderDespiteReversedRootDeclarations()
    {
        var first = new C.Tap { Lane = 6, Width = 4 };
        var second = new C.Tap { Tick = 480, Lane = 6, Width = 4 };
        C.AirHold Segment(C.Note parent, int start, int end) => new()
        {
            Tick = start,
            Lane = 6,
            Width = 4,
            Parent = parent,
            EndTick = end,
            EndLane = 6,
            EndWidth = 4
        };
        var firstRoot = Segment(first, 0, 960);
        var secondRoot = Segment(second, 480, 960);
        var chart = new C.Chart();
        chart.Notes.AddRange([first, second, secondRoot, firstRoot,
            Segment(secondRoot, 960, 1920), Segment(firstRoot, 960, 1440)]);
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Diagnostics.Diagnostics);
            var ends = parsed.Value!.Notes.OfType<C.AirHold>().Where(n => n.Parent is C.AirHold)
                .ToDictionary(n => ((C.AirHold)n.Parent!).Parent!.Tick.Original, n => n.EndTick.Original);
            Assert.Equal(1440, ends[0]);
            Assert.Equal(1920, ends[480]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnrelatedSlidePaths_KeepDeclaredRootOrder()
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        foreach (var tick in new[] { 960, 0 })
        {
            var slide = new U.Slide { Tick = tick, Lane = 0, Width = 4 };
            slide.AppendChild(new U.SlideJoint { Tick = tick + 240, Lane = 4, Width = 4, Joint = Joint.D });
            chart.Notes.AppendChild(slide);
        }
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.Equal([960, 0], converted.Value!.Notes.OfType<C.Slide>().Select(n => n.Tick.Original));
    }
}
