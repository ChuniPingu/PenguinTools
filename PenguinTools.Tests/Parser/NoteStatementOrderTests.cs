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
