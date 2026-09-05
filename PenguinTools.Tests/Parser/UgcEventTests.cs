using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser.ugc;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class UgcEventTests
{
    private static async Task<Chart.Models.umgr.Chart> Parse(string ugc)
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        await File.WriteAllTextAsync(tmp, ugc, ct);
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
    public async Task Bpm_MultipleEntries_CreatesBpmEvents()
    {
        const string ugc =
            "@VER\t8\n@TICKS\t480\n" +
            "@BPM\t0'0\t200.0\n" +
            "@BPM\t4'0\t180.0\n" +
            "@BEAT\t0\t4\t4\n";

        var chart = await Parse(ugc);
        var bpms = chart.Events.Children.OfType<BpmEvent>()
            .OrderBy(e => e.Tick).ToArray();
        Assert.Equal(2, bpms.Length);
        Assert.Equal(0, bpms[0].Tick.Original);
        Assert.Equal(200.0m, bpms[0].Bpm);
        Assert.Equal(7680, bpms[1].Tick.Original);
        Assert.Equal(180.0m, bpms[1].Bpm);
    }

    [Fact]
    public async Task Beat_MultipleEntries_AccumulateTicks()
    {
        const string ugc =
            "@VER\t8\n@TICKS\t480\n" +
            "@BPM\t0'0\t120.0\n" +
            "@BEAT\t0\t4\t4\n" +
            "@BEAT\t2\t3\t4\n";

        var chart = await Parse(ugc);
        var beats = chart.Events.Children.OfType<BeatEvent>()
            .OrderBy(e => e.Bar).ToArray();
        Assert.Equal(2, beats.Length);
        Assert.Equal(0, beats[0].Bar);
        Assert.Equal(2, beats[1].Bar);
        Assert.Equal(3840, beats[1].Tick.Original);
    }

    [Fact]
    public async Task SpdMod_CreatesNoteSpeedEvent()
    {
        const string ugc =
            "@VER\t8\n@TICKS\t480\n" +
            "@BPM\t0'0\t120.0\n" +
            "@BEAT\t0\t4\t4\n" +
            "@SPDMOD\t1'0\t0.5\n";

        var chart = await Parse(ugc);
        var smod = chart.Events.Children.OfType<NoteSpeedEvent>().Single();
        Assert.Equal(1920, smod.Tick.Original);
        Assert.Equal(0.5m, smod.Speed);
    }

    [Fact]
    public async Task MissingBeat_DefaultsToFourFourForBarTickConversion()
    {
        const string ugc =
            "@VER\t8\n@TICKS\t480\n" +
            "@BPM\t0'0\t120.0\n" +
            "#2'240:t64\n";

        var chart = await Parse(ugc);
        var tap = Assert.Single(chart.Notes.Children.OfType<Tap>());
        Assert.Equal(4080, tap.Tick.Original);
    }
}
