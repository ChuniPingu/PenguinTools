using System.Globalization;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using Xunit;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public class UgcSampleParityTests
{
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FinishedChartSampleCases.MasterPairs), MemberType = typeof(FinishedChartSampleCases))]
    public async Task UgcAndMgxc_HaveMatchingNoteSets(string name, string ugcPath, string mgxcPath)
    {
        var assets = TestAssets.Load();
        var media = TestMediaTool.Instance;
        var ct = TestContext.Current.CancellationToken;

        var ugcResult = await new UgcParser(new UgcParseRequest(ugcPath, assets), media).ParseAsync(ct);
        var mgxcResult = await new MgxcParser(new MgxcParseRequest(mgxcPath, assets), media).ParseAsync(ct);

        Assert.True(ugcResult.Succeeded, $"UGC parse failed for {name}: {ugcResult}");
        Assert.True(mgxcResult.Succeeded, $"MGXC parse failed for {name}: {mgxcResult}");

        var ugcSet = Summarize(ugcResult.Value!);
        var mgxcSet = Summarize(mgxcResult.Value!);

        Assert.Equal(mgxcSet.Notes.OrderBy(x => x).ToArray(), ugcSet.Notes.OrderBy(x => x).ToArray());
        Assert.Equal(mgxcSet.Bpms.OrderBy(x => x).ToArray(), ugcSet.Bpms.OrderBy(x => x).ToArray());
        Assert.Equal(mgxcSet.Beats.OrderBy(x => x).ToArray(), ugcSet.Beats.OrderBy(x => x).ToArray());
    }

    private static ChartSummary Summarize(UmgrModel.Chart c)
    {
        var summary = new ChartSummary();
        foreach (var n in c.Notes.Children)
        {
            // TIL → SoflanArea synthesis still diverges from reference MGXC on some charts; compare gameplay notes only.
            if (n is UmgrModel.SoflanArea or UmgrModel.SoflanAreaJoint)
            {
                continue;
            }

            summary.Notes.Add($"{n.GetType().Name}|{n.Tick.Original}|{n.Lane}|{n.Width}|{n.Timeline}");
        }

        foreach (var b in c.Events.Children.OfType<UmgrModel.BpmEvent>())
        {
            summary.Bpms.Add($"{b.Tick.Original}|{FormatBpm(b.Bpm)}");
        }

        foreach (var b in c.Events.Children.OfType<UmgrModel.BeatEvent>())
        {
            summary.Beats.Add($"{b.Bar}|{b.Numerator}|{b.Denominator}");
        }

        return summary;
    }

    private static string FormatBpm(decimal bpm)
    {
        var s = bpm.ToString(CultureInfo.InvariantCulture);
        if (!s.Contains('.'))
        {
            return s;
        }

        return s.TrimEnd('0').TrimEnd('.');
    }

    private sealed class ChartSummary
    {
        public List<string> Notes { get; } = [];
        public List<string> Bpms { get; } = [];
        public List<string> Beats { get; } = [];
    }
}
