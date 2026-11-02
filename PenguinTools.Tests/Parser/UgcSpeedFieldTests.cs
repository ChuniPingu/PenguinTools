using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class UgcSpeedFieldTests
{
    [Fact]
    public async Task AppliedRelativeSpeed_WarnsForC2sAndMgxcButIsPreservedByUgc()
    {
        var source = TestTempPaths.Create(".ugc");
        var ugc = TestTempPaths.Create(".ugc");
        var mgxc = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(source,
                "@VER\t9\n@TICKS\t480\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n" +
                "@FLAG\tSOFFSET\tFALSE\n@SPDDEF\t1\t480\t0.5\n@SPDFLD\t1\t0'0\t0\t4\t1920\n#0'960:tG4\n", ct);
            var parsed = await new UgcParser(new UgcParseRequest(source, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.Empty(Warnings(parsed));
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded);
            Assert.Equal("C2S", Assert.Single(Warnings(converted)).Message.Args!["arg0"]);

            var written = await new MgxcChartWriter(new MgxcWriteRequest(mgxc, parsed.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded);
            Assert.Equal("MGXC v4", Assert.Single(Warnings(written)).Message.Args!["arg0"]);
            var readMgxc = await new MgxcParser(new MgxcParseRequest(mgxc, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(readMgxc.Succeeded);
            var saved = await new UgcChartWriter(ugc, readMgxc.Value!).WriteAsync(ct);
            Assert.Empty(Warnings(saved));
            var text = await File.ReadAllTextAsync(ugc, ct);
            Assert.Contains("@SPDDEF\t1\t480\t0.5", text);
            Assert.Contains("@SPDFLD\t1\t0'0\t0\t4\t1920", text);
        }
        finally
        {
            File.Delete(source);
            File.Delete(ugc);
            File.Delete(mgxc);
        }
    }

    [Fact]
    public void FieldSelection_UsesHalfOpenTimeFullLaneContainmentAndLastField()
    {
        var chart = Chart();
        chart.Extras.EditorHeaders.AddRange(["@SPDDEF\t1\t480\t2", "@SPDFLD\t1\t0'0\t0\t4\t480"]);
        chart.Notes.AppendChild(new U.Tap { Tick = 480, Lane = 0, Width = 4 });
        chart.Notes.AppendChild(new U.Tap { Tick = 240, Lane = 3, Width = 2 });
        Assert.Empty(Warnings(new C2SChartConverter(new C2SConvertRequest(chart)).Convert()));

        chart.Notes.AppendChild(new U.Tap { Tick = 240, Lane = 0, Width = 4 });
        Assert.Single(Warnings(new C2SChartConverter(new C2SConvertRequest(chart)).Convert()));
        chart.Extras.EditorHeaders.AddRange(["@SPDDEF\t2\t0\t1", "@SPDFLD\t2\t0'0\t0\t4\t480"]);
        Assert.Empty(Warnings(new C2SChartConverter(new C2SConvertRequest(chart)).Convert()));
    }

    [Fact]
    public void FieldSelection_IncludesGeneratedAirCrushRungs()
    {
        var chart = Chart();
        chart.Extras.EditorHeaders.AddRange(["@SPDDEF\t1\t480\t2", "@SPDFLD\t1\t0'230\t4\t4\t20"]);
        var crash = new U.AirCrash { Tick = 0, Lane = 0, Width = 4, Height = 80, Density = 240 };
        crash.AppendChild(new U.AirCrashJoint { Tick = 480, Lane = 8, Width = 4, Height = 80 });
        chart.Notes.AppendChild(crash);
        var warning = Assert.Single(Warnings(new C2SChartConverter(new C2SConvertRequest(chart)).Convert()));
        Assert.Equal(240, warning.Time);
    }

    [Fact]
    public async Task RelativeSpeedHeaders_NormalizeTheirTickResolutionForUgcOutput()
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path,
                "@VER\t9\n@TICKS\t960\n@BPM\t0'0\t120\n" +
                "@SPDDEF\t1\t960\t0.5\n@SPDFLD\t1\t0'960\t0\t4\t960\n#0'1440:tG4\n", ct);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.Contains("@SPDDEF\t1\t480\t0.5", parsed.Value!.Extras.EditorHeaders);
            Assert.Contains("@SPDFLD\t1\t0'480\t0\t4\t480", parsed.Value.Extras.EditorHeaders);
            Assert.Single(Warnings(new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert()));
            var written = await new UgcChartWriter(path, parsed.Value).WriteAsync(ct);
            Assert.Empty(Warnings(written));
            var read = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.Equal(parsed.Value.Extras.EditorHeaders, read.Value!.Extras.EditorHeaders);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static U.Chart Chart()
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        return chart;
    }

    private static IEnumerable<Diagnostic> Warnings(OperationResult result) =>
        result.Diagnostics.Diagnostics.Where(d => d.Message.Key == MsgKeys.Warn_Ugc_relative_speed_not_reproduced);

    private static IEnumerable<Diagnostic> Warnings<T>(OperationResult<T> result) => Warnings(result.ToResult());
}
