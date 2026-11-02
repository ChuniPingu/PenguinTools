using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sAirCrashSchedulingTests
{
    [Theory]
    [InlineData("2.06", "2.10", 1)]
    [InlineData("2.04", "2.09", 2)]
    [InlineData("2.05", "2.10", 1)]
    public async Task NativeHeightKeys_DetermineImportedPaths(string endHeight, string nextHeight, int roots)
    {
        var chart = await Parse($"ALD\t0\t0\t0\t4\t64\t1\t96\t4\t4\t{endHeight}\tDEF\tAxisY\n" +
            $"ALD\t0\t96\t4\t4\t64\t{nextHeight}\t96\t8\t4\t3\tDEF\tAxisZ");
        var editor = new UgcChartConverter(new UgcConvertRequest(chart)).Convert();

        Assert.True(editor.Succeeded, editor.ToString());
        var crashes = editor.Value!.Notes.Children.OfType<U.AirCrash>().ToArray();
        Assert.Equal(roots, crashes.Length);
        Assert.Equal(AirLadderAttr.AxisY, crashes[0].Attr);
        Assert.Equal(2, crashes.Sum(n => n.Children.Count));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TouchingIndependentPaths_KeepTheirOwnRungOrigins(bool reverse, bool differentAxis)
    {
        var chart = CreateChart();
        var first = Crash(0, 0, 0, AirLadderAttr.AxisY, (480, 4, 20m));
        var second = Crash(480, 4, 20, differentAxis ? AirLadderAttr.AxisZ : AirLadderAttr.AxisY, (960, 8, 40m));
        foreach (var crash in reverse ? new[] { second, first } : new[] { first, second })
        {
            chart.Notes.AppendChild(crash);
        }

        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.Equal([480, 0], converted.Value!.Notes.OfType<C.AirCrash>().Select(n => n.Tick.Original));
        var roundTrip = await WriteRead(converted.Value);
        var paths = new UgcChartConverter(new UgcConvertRequest(roundTrip)).Convert().Value!.Notes.Children.OfType<U.AirCrash>()
            .OrderBy(n => n.Tick.Original).ToArray();

        Assert.Equal(2, paths.Length);
        Assert.Equal([0, 480], paths.Select(n => n.Tick.Original));
        Assert.Equal([480, 960], paths.Select(n => n.LastChild!.Tick.Original));
        Assert.All(paths, n => Assert.Equal(320, n.Density.Original));
        Assert.Equal(second.Attr, paths[1].Attr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalAtAnotherPathsIntermediatePoint_DoesNotStealItsContinuation(bool reverse)
    {
        var chart = CreateChart();
        var ending = Crash(0, 0, 0, AirLadderAttr.AxisY, (480, 4, 20m));
        var passing = Crash(240, 2, 10, AirLadderAttr.AxisZ, (480, 4, 20m), (960, 8, 40m));
        foreach (var crash in reverse ? new[] { passing, ending } : new[] { ending, passing })
        {
            chart.Notes.AppendChild(crash);
        }
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var roundTrip = await WriteRead(converted.Value!);
        var paths = new UgcChartConverter(new UgcConvertRequest(roundTrip)).Convert().Value!.Notes.Children.OfType<U.AirCrash>()
            .OrderBy(n => n.Tick.Original).ToArray();

        Assert.Equal(2, paths.Length);
        Assert.Equal(480, paths[0].LastChild!.Tick.Original);
        Assert.Equal([480, 960], paths[1].Children.Select(n => n.Tick.Original));
        Assert.Equal(AirLadderAttr.AxisZ, paths[1].Attr);
    }

    private static U.Chart CreateChart()
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        return chart;
    }

    private static U.AirCrash Crash(int tick, int lane, decimal height, AirLadderAttr axis,
        params (int Tick, int Lane, decimal Height)[] joints)
    {
        var crash = new U.AirCrash { Tick = tick, Lane = lane, Width = 4, Height = height, Density = 320, Attr = axis };
        foreach (var joint in joints)
        {
            crash.AppendChild(new U.AirCrashJoint { Tick = joint.Tick, Lane = joint.Lane, Width = 4, Height = joint.Height });
        }
        return crash;
    }

    private static async Task<C.Chart> Parse(string records)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "VERSION\t1.15.00\t1.15.00\nBPM\t0\t0\t120\n" + records, ct);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }

    private static async Task<C.Chart> WriteRead(C.Chart chart)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }
}
