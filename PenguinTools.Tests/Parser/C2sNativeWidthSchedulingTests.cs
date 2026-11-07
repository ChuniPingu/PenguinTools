using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sNativeWidthSchedulingTests
{
    [Theory]
    [InlineData("SLIDE", 17, 16)]
    [InlineData("ALD", 17, 16)]
    [InlineData("AHD", 17, 16)]
    [InlineData("ASD", 17, 16)]
    public async Task NativeEquivalentWidths_DoNotChangePathOwnership(string kind, int firstWidth, int secondWidth)
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        var first = AddPath(chart, kind, 0, firstWidth, false);
        var second = AddPath(chart, kind, 240, secondWidth, true);
        var conversion = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(conversion.Succeeded, conversion.ToString());
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, conversion.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Diagnostics.Diagnostics);
            var restored = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
            Assert.True(restored.Succeeded, restored.ToString());
            var roots = restored.Value!.Notes.Children.Where(n => n.GetType() == first.GetType())
                .OrderBy(n => n.Tick.Original).ToArray();

            Assert.Equal(2, roots.Length);
            Assert.Equal(first.Tick, roots[0].Tick);
            Assert.Equal(second.Tick, roots[1].Tick);
            Assert.Equal(480, roots[0].LastChild!.Tick.Original);
            Assert.Equal([480, 960], roots[1].Children.Select(n => n.Tick.Original));
            Assert.Equal(secondWidth, roots[0].LastChild!.Width);
        }
        finally { File.Delete(path); }
    }

    private static U.Note AddPath(U.Chart chart, string kind, int start, int width, bool continuing)
    {
        U.Note root = kind switch
        {
            "SLIDE" => new U.Slide { Tick = start, Lane = 4, Width = width },
            "ALD" => new U.AirCrash { Tick = start, Lane = 4, Width = width, Density = 320 },
            "AHD" => new U.AirHold(),
            _ => new U.AirSlide()
        };
        if (root is U.NegativeNote air)
        {
            var tap = new U.Tap { Tick = start, Lane = 4, Width = width };
            tap.MakePair(air);
            chart.Notes.AppendChild(tap);
        }
        foreach (var tick in continuing ? new[] { 480, 960 } : new[] { 480 })
        {
            U.Note joint = kind switch
            {
                "SLIDE" => new U.SlideJoint(),
                "ALD" => new U.AirCrashJoint(),
                "AHD" => new U.AirHoldJoint(),
                _ => new U.AirSlideJoint()
            };
            joint.Tick = tick;
            joint.Lane = 4;
            joint.Width = width;
            root.AppendChild(joint);
        }
        chart.Notes.AppendChild(root);
        return root;
    }
}
