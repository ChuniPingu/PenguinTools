
using C2sModel = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart.Writer.c2s;

internal static class C2SJudgeSummaryCalculator
{
    public static int CalculateTap(C2sModel.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var shortAndHoldHeads = chart.Notes.Count(note =>
            note is C2sModel.Tap
                or C2sModel.ExTap
                or C2sModel.Damage
                or C2sModel.Hold);

        return shortAndHoldHeads + GetSlideRoots(chart).Count;
    }

    public static int CalculateHoldProxy(C2sModel.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var bpmEvents = chart.Events
            .OfType<C2sModel.Bpm>()
            .Where(x => x.Value > 0)
            .OrderBy(x => x.Tick.Original)
            .ToArray();

        var total = 0;

        foreach (var hold in chart.Notes.OfType<C2sModel.Hold>())
        {
            var length = hold.Length.Scaled;

            if (length <= 0)
            {
                continue;
            }

            var bpm = GetBpmAt(
                chart,
                bpmEvents,
                hold.Tick.Original);

            var interval = GetHoldJudgeInterval(bpm);
            var count = (length + interval - 1) / interval;

            var replacements = GetHoldAirReplacementCount(
                chart,
                hold);

            total += Math.Max(
                0,
                count - replacements);
        }

        return total;
    }

    public static int CalculateSlideProxy(C2sModel.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        const int judgeInterval = 96;

        long total = 0;
        foreach (var path in C2sSlidePaths.Build(chart.Notes.OfType<C2sModel.Slide>()))
        {
            var duration = (long)path[^1].EndTick.Original - path[0].Tick.Original;

            if (duration <= 0)
            {
                continue;
            }

            total +=
                (duration + judgeInterval - 1) /
                judgeInterval;

            if (total > int.MaxValue)
            {
                return int.MaxValue;
            }
        }

        return (int)total;
    }

    public static int CalculateFlick(C2sModel.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return chart.Notes.Count(note => note is C2sModel.Flick);
    }

    public static int CalculateAirProxy(C2sModel.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return chart.Notes.Count(note =>
            note is C2sModel.Air
                or C2sModel.AirHold { Parent: not C2sModel.AirHold }
                or C2sModel.AirSlide { Parent: not C2sModel.AirSlide });
    }

    private static decimal GetBpmAt(
        C2sModel.Chart chart,
        IReadOnlyList<C2sModel.Bpm> bpmEvents,
        int tick)
    {
        var initialBpm = chart.Meta.BgmInitialBpm > 0 ? chart.Meta.BgmInitialBpm : 120m;
        var bpm = chart.Meta.MainBpm > 0 ? chart.Meta.MainBpm : initialBpm;

        foreach (var bpmEvent in bpmEvents)
        {
            if (bpmEvent.Tick.Original > tick)
            {
                break;
            }

            bpm = bpmEvent.Value;
        }

        return bpm;
    }

    private static int GetHoldJudgeInterval(decimal bpm)
    {
        if (bpm < 120m)
        {
            return 24;
        }

        if (bpm < 240m)
        {
            return 48;
        }

        return 96;
    }

    private static int GetHoldAirReplacementCount(
        C2sModel.Chart chart,
        C2sModel.Hold hold)
    {
        return chart.Notes.Count(note =>
            note.Tick.Original == hold.EndTick.Original &&
            note.Lane == hold.Lane &&
            note.Width == hold.Width &&
            note is (C2sModel.Air or C2sModel.AirSlide or C2sModel.AirHold) and
                C2sModel.IPairable { Parent: C2sModel.Hold });
    }

    private static List<C2sModel.Slide> GetSlideRoots(C2sModel.Chart chart) =>
        C2sSlidePaths.Build(chart.Notes.OfType<C2sModel.Slide>()).Select(path => path[0]).ToList();
}
