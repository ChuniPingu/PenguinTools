using System.Numerics;
using PenguinTools.Chart.Models;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser;

internal readonly record struct SlaPlacement(int Tick, int Timeline, int Lane, int Width, int Length)
{
    public int EndTick => Tick + Length;
}

/// <summary>
/// Reduces one-note SLA regions to a rectangle cover, retaining the original
/// regions if serialization would change any note's timeline assignment.
/// </summary>
internal static class SlaPlacementOptimizer
{
    private const int ExactTargetLimit = 64;
    private const int ExactCandidateLimit = 1024;
    private const int ExactSearchNodeLimit = 250_000;

    public static IReadOnlyList<SlaPlacement> Optimize(
        IReadOnlyList<UmgrModel.Note> notes,
        IReadOnlyList<SlaPlacement> legacyAreas, int tickOffset = 0)
    {
        if (legacyAreas.Count == 0)
        {
            return [];
        }

        var noteInfos = notes.Select((note, index) => new NoteInfo(
            index,
            new Time(checked(note.Tick.Original + tickOffset)).Round - tickOffset,
            note.Lane,
            note.Lane + note.Width)).ToArray();
        var effectiveTimelines = ComputeEffectiveTimelines(noteInfos, legacyAreas);
        var selected = new List<SlaCandidate>();

        var orderedNotes = noteInfos.OrderBy(note => note.Tick).ToArray();
        var timelines = orderedNotes.Where(note => effectiveTimelines[note.Index] > 0)
            .GroupBy(note => effectiveTimelines[note.Index]).OrderBy(group => group.Key);
        foreach (var timeline in timelines)
        {
            var targets = timeline.OrderBy(note => note.Tick).ThenBy(note => note.Left).ThenBy(note => note.Right).ToArray();
            var candidates = GenerateCandidates(orderedNotes, effectiveTimelines, targets, timeline.Key);
            selected.AddRange(SolveByComponent(targets.Length, candidates));
        }

        var optimizedAreas = selected
            .Select(x => x.Placement)
            .OrderBy(x => x.Tick)
            .ThenBy(x => x.Timeline)
            .ThenBy(x => x.Lane)
            .ThenBy(x => x.Width)
            .ThenBy(x => x.Length)
            .ToArray();

        return PreservesSerializedTimelines(notes, legacyAreas, optimizedAreas)
            ? optimizedAreas
            : legacyAreas;
    }

    private static bool PreservesSerializedTimelines(
        IReadOnlyList<UmgrModel.Note> notes,
        IReadOnlyList<SlaPlacement> legacyAreas,
        IReadOnlyList<SlaPlacement> optimizedAreas)
    {
        // The opening measure can shift off-grid notes before C2S rounding.
        // All integer offsets have one of these five rounding phases.
        // With every endpoint on the grid, each phase translates all
        // coordinates by the same amount.
        static bool Aligned(int tick) => tick % ChartResolution.SingleTick == 0;
        static bool AlignedArea(SlaPlacement area) => Aligned(area.Tick) && Aligned(area.EndTick);
        var phases = notes.All(note => Aligned(note.Tick.Original)) &&
            legacyAreas.All(AlignedArea) && optimizedAreas.All(AlignedArea)
            ? 1 : ChartResolution.SingleTick;
        for (var phase = 0; phase < phases; phase++)
        {
            var serializedNotes = notes.Select((note, index) => new NoteInfo(
                    index, SerializeTick(note.Tick.Original, phase), note.Lane, note.Lane + note.Width))
                .OrderBy(note => note.Tick)
                .ToArray();
            var ticks = serializedNotes.Select(note => note.Tick).ToArray();
            var legacyTimelines = SerializedTimelines(serializedNotes, ticks, legacyAreas, phase);
            var optimizedTimelines = SerializedTimelines(serializedNotes, ticks, optimizedAreas, phase);
            if (!legacyTimelines.SequenceEqual(optimizedTimelines))
            {
                return false;
            }
        }

        return true;
    }

    private static int SerializeTick(int tick, int phase) =>
        (int)Math.Round(((decimal)tick + phase) / ChartResolution.SingleTick);

    private static int[] SerializedTimelines(
        IReadOnlyList<NoteInfo> notes, int[] ticks, IReadOnlyList<SlaPlacement> areas, int phase)
    {
        var result = new int[notes.Count];
        // Overlapping regions select the largest timeline id, independently of row order.
        foreach (var area in areas)
        {
            var start = SerializeTick(area.Tick, phase);
            var end = SerializeTick(area.EndTick, phase);
            for (var index = LowerBound(ticks, start); index < notes.Count && notes[index].Tick < end; index++)
            {
                var note = notes[index];
                var contains = note.Right <= note.Left || area.Width <= 0
                    ? note.Left == area.Lane && note.Right - note.Left == area.Width
                    : Contains(area.Lane, area.Lane + area.Width, note);
                if (contains)
                {
                    result[note.Index] = Math.Max(result[note.Index], area.Timeline);
                }
            }
        }

        return result;
    }

    private static int[] ComputeEffectiveTimelines(
        IReadOnlyList<NoteInfo> notes,
        IReadOnlyList<SlaPlacement> areas)
    {
        var result = new int[notes.Count];
        var notesByTick = notes.GroupBy(x => x.Tick).ToDictionary(x => x.Key, x => x.ToArray());
        foreach (var area in areas)
        {
            // Legacy regions are exactly one serialized C2S tick long. Notes are
            // rounded to that same grid, so only this tick can be affected.
            if (!notesByTick.TryGetValue(area.Tick, out var notesAtTick))
            {
                continue;
            }

            foreach (var note in notesAtTick)
            {
                if (!Contains(area, note))
                {
                    continue;
                }

                result[note.Index] = Math.Max(result[note.Index], area.Timeline);
            }
        }

        return result;
    }

    private static List<SlaCandidate> GenerateCandidates(
        IReadOnlyList<NoteInfo> allNotes,
        int[] effectiveTimelines,
        IReadOnlyList<NoteInfo> targets,
        int timeline)
    {
        var leftBounds = targets.Select(x => x.Left).Distinct().Order().ToArray();
        var rightBounds = targets.Select(x => x.Right).Distinct().Order().ToArray();
        long end = (long)targets[^1].Tick + ChartResolution.SingleTick;
        var blockers = allNotes.Where(note => note.Tick >= targets[0].Tick && note.Tick < end
            && effectiveTimelines[note.Index] < timeline).ToArray();
        var candidatesByCoverage = new Dictionary<int[], SlaCandidate>(new CoverageComparer());

        foreach (var left in leftBounds)
        {
            foreach (var right in rightBounds)
            {
                if (right <= left || right - left > 16)
                {
                    continue;
                }

                AddCandidatesForBounds(blockers, targets, timeline, left, right, candidatesByCoverage);
            }
        }

        return candidatesByCoverage.Values
            .OrderByDescending(x => x.Targets.Length)
            .ThenBy(x => x.Placement.Tick)
            .ThenBy(x => x.Placement.Lane)
            .ThenBy(x => x.Placement.Width)
            .ThenBy(x => x.Placement.Length)
            .ToList();
    }

    private static void AddCandidatesForBounds(NoteInfo[] blockers, IReadOnlyList<NoteInfo> targets, int timeline,
        int left, int right, Dictionary<int[], SlaCandidate> candidatesByCoverage)
    {
        var covered = new List<int>();
        var blocker = 0;
        int firstTick = 0, lastTick = 0;

        void FinishSegment()
        {
            if (covered.Count == 0)
            {
                return;
            }
            var endTick = lastTick + ChartResolution.SingleTick;
            if (blocker == blockers.Length || blockers[blocker].Tick >= endTick)
            {
                var placement = new SlaPlacement(firstTick, timeline, left, right - left, endTick - firstTick);
                var members = covered.ToArray();
                if (!candidatesByCoverage.TryGetValue(members, out var existing) ||
                    CompareGeometry(placement, existing.Placement) < 0)
                {
                    candidatesByCoverage[members] = new SlaCandidate(placement, members);
                }
            }
            covered.Clear();
        }

        // Targets and blockers are in tick order. Each contained blocker closes the current cover segment.
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (!Contains(left, right, target))
            {
                continue;
            }
            while (blocker < blockers.Length)
            {
                var next = blockers[blocker];
                if (Contains(left, right, next))
                {
                    if (next.Tick >= target.Tick)
                    {
                        break;
                    }
                    FinishSegment();
                }
                blocker++;
            }
            if (blocker < blockers.Length && blockers[blocker].Tick == target.Tick)
            {
                FinishSegment();
                continue;
            }
            if (covered.Count == 0)
            {
                firstTick = target.Tick;
            }
            lastTick = target.Tick;
            covered.Add(index);
        }
        FinishSegment();
    }

    private sealed class CoverageComparer : IEqualityComparer<int[]>
    {
        public bool Equals(int[]? x, int[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(int[] members)
        {
            var hash = new HashCode();
            hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(members.AsSpan()));
            return hash.ToHashCode();
        }
    }

    private static List<SlaCandidate> SolveByComponent(
        int targetCount,
        IReadOnlyList<SlaCandidate> candidates)
    {
        var dsu = new DisjointSet(targetCount);
        foreach (var targets in candidates.Select(candidate => candidate.Targets))
        {
            var first = targets[0];
            for (var i = 1; i < targets.Length; i++)
            {
                dsu.Union(first, targets[i]);
            }
        }

        var targetsByRoot = Enumerable.Range(0, targetCount).GroupBy(dsu.Find);
        var candidatesByRoot = candidates.ToLookup(candidate => dsu.Find(candidate.Targets[0]));
        var result = new List<SlaCandidate>();
        foreach (var targetGroup in targetsByRoot)
        {
            var componentTargets = targetGroup.Order().ToArray();
            var componentCandidates = candidatesByRoot[targetGroup.Key].ToArray();
            result.AddRange(SolveComponent(componentTargets, componentCandidates));
        }

        return result;
    }

    private static IReadOnlyList<SlaCandidate> SolveComponent(
        int[] componentTargets,
        IReadOnlyList<SlaCandidate> candidates)
    {
        if (componentTargets.Length <= ExactTargetLimit && candidates.Count <= ExactCandidateLimit)
        {
            var targetMap = componentTargets.Select((target, local) => (target, local))
                .ToDictionary(x => x.target, x => x.local);
            var masks = candidates.Select(candidate => candidate.Targets.Aggregate(0UL,
                (mask, target) => mask | 1UL << targetMap[target])).ToArray();
            var exact = SolveExact(masks, componentTargets.Length);
            return exact.Select(x => candidates[x]).ToArray();
        }

        return SolveGreedy(componentTargets, candidates);
    }

    private static List<int> SolveExact(ulong[] masks, int targetCount) =>
        new ExactCoverSearch(masks, targetCount).Solve();

    private sealed class ExactCoverSearch
    {
        private readonly ulong[] _masks;
        private readonly int[] _targetOrder;
        private readonly ulong _all;
        private readonly List<int>[] _candidatesByTarget;
        private List<int> _incumbent;
        private readonly List<int> _current = [];
        private readonly Dictionary<ulong, int> _bestDepthByCovered = [];
        private int _visitedNodes;

        public ExactCoverSearch(ulong[] masks, int targetCount)
        {
            _masks = masks;
            _all = targetCount == 64 ? ulong.MaxValue : (1UL << targetCount) - 1;
            _candidatesByTarget = new List<int>[targetCount];
            for (var i = 0; i < targetCount; i++)
            {
                _candidatesByTarget[i] = [];
            }

            for (var candidate = 0; candidate < masks.Length; candidate++)
            {
                for (var target = 0; target < targetCount; target++)
                {
                    if ((masks[candidate] & 1UL << target) != 0)
                    {
                        _candidatesByTarget[target].Add(candidate);
                    }
                }
            }

            // Every candidate covering an uncovered target has positive gain.
            _targetOrder = Enumerable.Range(0, targetCount).OrderBy(target => _candidatesByTarget[target].Count).ToArray();
            _incumbent = SolveGreedyMasks(masks, _all);
        }

        public List<int> Solve()
        {
            Search(0);
            return _incumbent;
        }

        private void Search(ulong covered)
        {
            if (++_visitedNodes > ExactSearchNodeLimit)
            {
                return;
            }

            if (covered == _all)
            {
                if (_current.Count < _incumbent.Count)
                {
                    _incumbent = _current.ToList();
                }

                return;
            }

            if (_current.Count >= _incumbent.Count - 1)
            {
                return;
            }

            if (_bestDepthByCovered.TryGetValue(covered, out var previousDepth) && previousDepth <= _current.Count)
            {
                return;
            }

            _bestDepthByCovered[covered] = _current.Count;

            var remaining = _all & ~covered;
            var maxGain = _masks.Max(mask => BitOperations.PopCount(mask & remaining));
            if (maxGain == 0)
            {
                return;
            }

            var lowerBound = (BitOperations.PopCount(remaining) + maxGain - 1) / maxGain;
            if (_current.Count + lowerBound >= _incumbent.Count)
            {
                return;
            }

            var target = _targetOrder.First(target => (remaining & 1UL << target) != 0);
            var branches = _candidatesByTarget[target]
                .OrderByDescending(candidate => BitOperations.PopCount(_masks[candidate] & remaining))
                .ThenBy(candidate => candidate);

            foreach (var candidate in branches)
            {
                _current.Add(candidate);
                Search(covered | _masks[candidate]);
                _current.RemoveAt(_current.Count - 1);
                if (_visitedNodes > ExactSearchNodeLimit)
                {
                    return;
                }
            }
        }
        private static List<int> SolveGreedyMasks(ulong[] masks, ulong all)
        {
            var selected = new List<int>();
            var covered = 0UL;
            while (covered != all)
            {
                var best = Enumerable.Range(0, masks.Length)
                    .MaxBy(x => BitOperations.PopCount(masks[x] & ~covered));
                if ((masks[best] & ~covered) == 0)
                {
                    throw new InvalidOperationException("No SLA candidate covers the remaining notes.");
                }

                selected.Add(best);
                covered |= masks[best];
            }

            return selected;
        }
    }

    private static List<SlaCandidate> SolveGreedy(
        int[] targets,
        IReadOnlyList<SlaCandidate> candidates)
    {
        var coverage = targets.ToDictionary(target => target, _ => new TargetCoverage());
        var gains = new int[candidates.Count];
        var queue = new PriorityQueue<int, (int NegativeGain, int Candidate)>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var members = candidates[index].Targets;
            gains[index] = members.Length;
            queue.Enqueue(index, (-gains[index], index));
            foreach (var target in members)
            {
                coverage[target].Candidates.Add(index);
            }
        }

        var remaining = targets.Length;
        var selected = new List<SlaCandidate>();
        while (remaining > 0)
        {
            if (!queue.TryDequeue(out var index, out var priority))
            {
                throw new InvalidOperationException("No SLA candidate covers the remaining notes.");
            }

            var gain = gains[index];
            if (gain == 0)
            {
                continue;
            }

            // Queued gains can only overestimate, so a current gain at the
            // head is optimal with ties broken by the original candidate order.
            if (-gain != priority.NegativeGain)
            {
                queue.Enqueue(index, (-gain, index));
                continue;
            }

            var best = candidates[index];
            selected.Add(best);
            foreach (var target in best.Targets)
            {
                var member = coverage[target];
                member.SelectedCount++;
                if (member.SelectedCount != 1)
                {
                    continue;
                }

                remaining--;
                foreach (var candidate in member.Candidates)
                {
                    gains[candidate]--;
                }
            }
        }

        // Remove redundant candidates in the reverse selection order.
        var retained = new List<SlaCandidate>(selected.Count);
        for (var i = selected.Count - 1; i >= 0; i--)
        {
            var members = selected[i].Targets;
            if (members.Any(target => coverage[target].SelectedCount == 1))
            {
                retained.Add(selected[i]);
                continue;
            }

            foreach (var target in members)
            {
                coverage[target].SelectedCount--;
            }
        }

        retained.Reverse();
        return retained;
    }

    private static int LowerBound(int[] values, int value)
    {
        var left = 0;
        var right = values.Length;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (values[middle] < value)
            {
                left = middle + 1;
            }
            else
            {
                right = middle;
            }
        }

        return left;
    }

    private static bool Contains(SlaPlacement area, NoteInfo note) =>
        note.Tick >= area.Tick && note.Tick < area.EndTick &&
        note.Left >= area.Lane && note.Right <= area.Lane + area.Width;

    private static bool Contains(int left, int right, NoteInfo note) =>
        note.Left >= left && note.Right <= right;

    private static int CompareGeometry(SlaPlacement left, SlaPlacement right)
    {
        var leftArea = (long)left.Width * left.Length;
        var rightArea = (long)right.Width * right.Length;
        var result = leftArea.CompareTo(rightArea);
        if (result != 0)
        {
            return result;
        }

        result = left.Length.CompareTo(right.Length);
        if (result != 0)
        {
            return result;
        }

        result = left.Width.CompareTo(right.Width);
        if (result != 0)
        {
            return result;
        }

        result = left.Tick.CompareTo(right.Tick);
        return result != 0 ? result : left.Lane.CompareTo(right.Lane);
    }

    private readonly record struct NoteInfo(int Index, int Tick, int Left, int Right);

    private sealed record SlaCandidate(SlaPlacement Placement, int[] Targets);

    private sealed class TargetCoverage
    {
        public List<int> Candidates { get; } = [];
        public int SelectedCount { get; set; }
    }

    private sealed class DisjointSet(int count)
    {
        private readonly int[] _parents = Enumerable.Range(0, count).ToArray();
        private readonly byte[] _ranks = new byte[count];

        public int Find(int value)
        {
            if (_parents[value] != value)
            {
                _parents[value] = Find(_parents[value]);
            }

            return _parents[value];
        }

        public void Union(int left, int right)
        {
            left = Find(left);
            right = Find(right);
            if (left == right)
            {
                return;
            }

            if (_ranks[left] < _ranks[right])
            {
                (left, right) = (right, left);
            }

            _parents[right] = left;
            if (_ranks[left] == _ranks[right])
            {
                _ranks[left]++;
            }
        }
    }
}
