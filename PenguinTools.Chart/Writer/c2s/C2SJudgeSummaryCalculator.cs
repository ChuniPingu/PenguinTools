namespace PenguinTools.Chart.Writer.c2s;

using c2s = Models.c2s;

internal static class C2SJudgeSummaryCalculator
{
    public static int CalculateTap(c2s.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var shortAndHoldHeads = chart.Notes.Count(note =>
            note is c2s.Tap
                or c2s.ExTap
                or c2s.Damage
                or c2s.Hold);

        return shortAndHoldHeads + GetSlideRoots(chart).Count;
    }

    public static int CalculateHoldProxy(c2s.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var bpmEvents = chart.Events
            .OfType<c2s.Bpm>()
            .Where(x => x.Value > 0)
            .OrderBy(x => x.Tick.Original)
            .ToArray();

        var total = 0;

        foreach (var hold in chart.Notes.OfType<c2s.Hold>())
        {
            var length = hold.Length.Scaled;

            if (length <= 0)
                continue;

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

    public static int CalculateSlideProxy(c2s.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        const int judgeInterval = 96;

        var active =
            new Dictionary<SlidePoint, Queue<int>>();

        var pathStart =
            new Dictionary<int, int>();

        var pathEnd =
            new Dictionary<int, int>();

        var nextChainId = 0;

        foreach (var entry in chart.Notes
                     .OfType<c2s.Slide>()
                     .Select((segment, sourceOrder) =>
                         (Segment: segment, SourceOrder: sourceOrder))
                     .OrderBy(x => x.Segment.Tick.Original)
                     .ThenBy(x => x.SourceOrder))
        {
            var segment = entry.Segment;

            var start = new SlidePoint(
                segment.Tick.Original,
                segment.Lane,
                segment.Width);

            int chainId;

            if (active.TryGetValue(start, out var queue) &&
                queue.Count > 0)
            {
                chainId = queue.Dequeue();

                if (queue.Count == 0)
                    active.Remove(start);
            }
            else
            {
                chainId = nextChainId++;

                pathStart[chainId] =
                    segment.Tick.Original;
            }

            pathEnd[chainId] =
                segment.EndTick.Original;

            var end = new SlidePoint(
                segment.EndTick.Original,
                segment.EndLane,
                segment.EndWidth);

            if (!active.TryGetValue(end, out var endQueue))
            {
                endQueue = new Queue<int>();
                active[end] = endQueue;
            }

            endQueue.Enqueue(chainId);
        }

        long total = 0;

        foreach (var chainId in pathStart.Keys)
        {
            var duration =
                (long)pathEnd[chainId] -
                pathStart[chainId];

            if (duration <= 0)
                continue;

            total +=
                (duration + judgeInterval - 1) /
                judgeInterval;

            if (total > int.MaxValue)
                return int.MaxValue;
        }

        return (int)total;
    }

    public static int CalculateFlick(c2s.Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return chart.Notes.Count(note => note is c2s.Flick);
    }

    private static decimal GetBpmAt(
        c2s.Chart chart,
        IReadOnlyList<c2s.Bpm> bpmEvents,
        int tick)
    {
        var bpm = chart.Meta.MainBpm > 0
            ? chart.Meta.MainBpm
            : chart.Meta.BgmInitialBpm > 0
                ? chart.Meta.BgmInitialBpm
                : 120m;

        foreach (var bpmEvent in bpmEvents)
        {
            if (bpmEvent.Tick.Original > tick)
                break;

            bpm = bpmEvent.Value;
        }

        return bpm;
    }

    private static int GetHoldJudgeInterval(decimal bpm)
    {
        if (bpm < 120m)
            return 24;

        if (bpm < 240m)
            return 48;

        return 96;
    }

    private static int GetHoldAirReplacementCount(
        c2s.Chart chart,
        c2s.Hold hold)
    {
        return chart.Notes.Count(note =>
            note.Tick.Original == hold.EndTick.Original &&
            (note is c2s.Air
                or c2s.AirSlide
                or c2s.AirHold) &&
            note is c2s.IPairable pairable &&
            ReferenceEquals(pairable.Parent, hold));
    }

    private static List<c2s.Slide> GetSlideRoots(c2s.Chart chart)
    {
        var active = new Dictionary<SlidePoint, Queue<int>>();
        var roots = new List<c2s.Slide>();
        var nextChainId = 0;

        foreach (var entry in chart.Notes
                     .OfType<c2s.Slide>()
                     .Select((segment, sourceOrder) =>
                         (Segment: segment, SourceOrder: sourceOrder))
                     .OrderBy(x => x.Segment.Tick.Original)
                     .ThenBy(x => x.SourceOrder))
        {
            var segment = entry.Segment;

            var start = new SlidePoint(
                segment.Tick.Original,
                segment.Lane,
                segment.Width);

            int chainId;

            if (active.TryGetValue(start, out var queue) &&
                queue.Count > 0)
            {
                chainId = queue.Dequeue();

                if (queue.Count == 0)
                    active.Remove(start);
            }
            else
            {
                chainId = nextChainId++;
                roots.Add(segment);
            }

            var end = new SlidePoint(
                segment.EndTick.Original,
                segment.EndLane,
                segment.EndWidth);

            if (!active.TryGetValue(end, out var endQueue))
            {
                endQueue = new Queue<int>();
                active[end] = endQueue;
            }

            endQueue.Enqueue(chainId);
        }

        return roots;
    }

    private readonly record struct SlidePoint(
        int Tick,
        int Lane,
        int Width);
}
