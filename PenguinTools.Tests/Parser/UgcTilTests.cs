using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class UgcTilTests
{
    private const string Header =
        "@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n";

    private static async Task<Chart.Models.umgr.Chart> Parse(string body)
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = Path.GetTempFileName() + ".ugc";
        await File.WriteAllTextAsync(tmp, Header + body, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded, r.ToString());
            return r.Value!;
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Til_Definition_CreatesScrollSpeedEvent()
    {
        const string ugc =
            "@VER\t8\n@TICKS\t480\n" +
            "@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n" +
            "@TIL\t3\t0'240\t10000.0\n";
        var ct = TestContext.Current.CancellationToken;
        var tmp = Path.GetTempFileName() + ".ugc";
        await File.WriteAllTextAsync(tmp, ugc, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded);
            var sse = r.Value!.Events.Children
                .OfType<ScrollSpeedEvent>()
                .SingleOrDefault(e => e.Speed == 10000.0m);
            Assert.NotNull(sse);
            Assert.Equal(240, sse!.Tick.Original);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task UseTil_AppliesToChildNoteLines()
    {
        const string ugc =
            "@USETIL\t0\n#0'0:h14\n@USETIL\t3\n#480:s\n" +
            "@USETIL\t0\n#1'0:s14\n@USETIL\t4\n#480:s24\n" +
            "@USETIL\t0\n#2'0:t14\n#2'0:H14N\n@USETIL\t5\n#480:c\n" +
            "@USETIL\t0\n#3'0:t24\n#3'0:S240AN\n@USETIL\t6\n#480:s34ZZ\n" +
            "@USETIL\t0\n#4'0:C340A1,24\n@USETIL\t7\n#480:c44ZZ\n";

        var chart = await Parse(ugc);

        Assert.Equal(3, Assert.Single(chart.Notes.Children.OfType<Hold>()).Children.Single().Timeline);
        Assert.Equal(4, Assert.Single(chart.Notes.Children.OfType<Slide>()).Children.Single().Timeline);

        var airSlides = chart.Notes.Children.OfType<AirSlide>().OrderBy(n => n.Tick.Original).ToArray();
        Assert.Equal(5, airSlides[0].Children.Single().Timeline);
        Assert.Equal(6, airSlides[1].Children.Single().Timeline);

        Assert.Equal(7, Assert.Single(chart.Notes.Children.OfType<AirCrash>()).Children.Single().Timeline);
    }

    [Fact]
    public async Task MainTil_SetsMetaMainTil()
    {
        const string ugc =
            "@VER\t8\n@TICKS\t480\n" +
            "@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n" +
            "@MAINTIL\t2\n";
        var ct = TestContext.Current.CancellationToken;
        var tmp = Path.GetTempFileName() + ".ugc";
        await File.WriteAllTextAsync(tmp, ugc, ct);
        try
        {
            var r =
                await new UgcParser(new UgcParseRequest(tmp, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(r.Succeeded);
            var errors = r.Diagnostics.Diagnostics.Where(d => d.Severity >= Severity.Warning).ToList();
            Assert.DoesNotContain(errors, d =>
                d.Message.Key == MsgKeys.Mg_Main_timeline_not_found);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}