using System.Numerics;
using PenguinTools.Chart.Models;

namespace PenguinTools.Chart.Parser;

using umgr = Models.umgr;

internal readonly record struct SlaPlacement(int Tick, int Timeline, int Lane, int Width, int Length)
{
    public int EndTick => Tick + Length;
}

/// <summary>
/// Reduces one-note SLA regions to a safe rectangle cover. The optimizer first
/// computes the timeline assignment produced by the legacy regions, then only
/// creates rectangles that cannot raise a note above that assignment.
/// </summary>
internal static class SlaPlacementOptimizer
{
    private const int ExactTargetLimit = 64;
    private const int ExactCandidateLimit = 1024;
    private const int ExactSearchNodeLimit = 250_000;

    public static IReadOnlyList<SlaPlacement> Optimize(
        IReadOnlyList<umgr.Note> notes,
        IReadOnlyList<SlaPlacement> legacyAreas)
    {
        if (legacyAreas.Count == 0) return [];

        var noteInfos = notes.Select((note, index) => new NoteInfo(
            index,
            note.Tick.Round,
            note.Lane,
            note.Lane + note.Width)).ToArray();
        var effectiveTimelines = ComputeEffectiveTimelines(noteInfos, legacyAreas);
        var selected = new List<SlaCandidate>();

        foreach (var timeline in effectiveTimelines.Where(x => x > 0).Distinct().Order())
        {
            var targets = noteInfos
                .Where(x => effectiveTimelines[x.Index] == timeline)
                .OrderBy(x => x.Tick)
                .ThenBy(x => x.Left)
                .ThenBy(x => x.Right)
                .ToArray();
            if (targets.Length == 0) continue;

            var candidates = GenerateCandidates(noteInfos, effectiveTimelines, targets, timeline);
            selected.AddRange(SolveByComponent(targets.Length, candidates));
        }

        return selected
            .Select(x => x.Placement)
            .OrderBy(x => x.Tick)
            .ThenBy(x => x.Timeline)
            .ThenBy(x => x.Lane)
            .ThenBy(x => x.Width)
            .ThenBy(x => x.Length)
            .ToArray();
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
            if (!notesByTick.TryGetValue(area.Tick, out var notesAtTick)) continue;
            foreach (var note in notesAtTick)
            {
                if (!Contains(area, note)) continue;
                result[note.Index] = Math.Max(result[note.Index], area.Timeline);
            }
        }

        return result;
    }

    private static List<SlaCandidate> GenerateCandidates(
        IReadOnlyList<NoteInfo> allNotes,
        IReadOnlyList<int> effectiveTimelines,
        IReadOnlyList<NoteInfo> targets,
        int timeline)
    {
        var leftBounds = targets.Select(x => x.Left).Distinct().Order().ToArray();
        var rightBounds = targets.Select(x => x.Right).Distinct().Order().ToArray();
        var candidatesByCoverage = new Dictionary<string, SlaCandidate>(StringComparer.Ordinal);

        foreach (var left in leftBounds)
            foreach (var right in rightBounds)
            {
                if (right <= left) continue;

                var containedTargets = targets
                    .Select((note, targetIndex) => (note, targetIndex))
                    .Where(x => Contains(left, right, x.note))
                    .ToArray();
                if (containedTargets.Length == 0) continue;

                var blockerTicks = allNotes
                    .Where(x => effectiveTimelines[x.Index] < timeline && Contains(left, right, x))
                    .Select(x => x.Tick)
                    .Distinct()
                    .Order()
                    .ToArray();

                var targetsBySegment = containedTargets
                    .Where(x => Array.BinarySearch(blockerTicks, x.note.Tick) < 0)
                    .GroupBy(x => LowerBound(blockerTicks, x.note.Tick));

                foreach (var segment in targetsBySegment)
                {
                    var covered = segment.Select(x => x.targetIndex).Distinct().Order().ToArray();
                    var firstTick = segment.Min(x => x.note.Tick);
                    var lastTick = segment.Max(x => x.note.Tick);
                    var endTick = lastTick + ChartResolution.SingleTick;

                    // The next blocked tick normally lies at least one C2S tick after
                    // the last target. Keep this guard for malformed/off-grid input.
                    if (blockerTicks.Any(x => x >= firstTick && x < endTick)) continue;

                    var candidate = new SlaCandidate(
                        new SlaPlacement(firstTick, timeline, left, right - left, endTick - firstTick),
                        covered);
                    var key = string.Join(',', covered);

                    if (!candidatesByCoverage.TryGetValue(key, out var existing) ||
                        CompareGeometry(candidate.Placement, existing.Placement) < 0)
                        candidatesByCoverage[key] = candidate;
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

    private static IReadOnlyList<SlaCandidate> SolveByComponent(
        int targetCount,
        IReadOnlyList<SlaCandidate> candidates)
    {
        var dsu = new DisjointSet(targetCount);
        foreach (var candidate in candidates)
        {
            var first = candidate.Targets[0];
            for (var i = 1; i < candidate.Targets.Length; i++) dsu.Union(first, candidate.Targets[i]);
        }

        var targetsByRoot = Enumerable.Range(0, targetCount).GroupBy(dsu.Find);
        var result = new List<SlaCandidate>();
        foreach (var targetGroup in targetsByRoot)
        {
            var componentTargets = targetGroup.Order().ToArray();
            var targetSet = componentTargets.ToHashSet();
            var componentCandidates = candidates.Where(x => targetSet.Contains(x.Targets[0])).ToArray();
            result.AddRange(SolveComponent(componentTargets, componentCandidates));
        }

        return result;
    }

    private static IReadOnlyList<SlaCandidate> SolveComponent(
        IReadOnlyList<int> componentTargets,
        IReadOnlyList<SlaCandidate> candidates)
    {
        if (componentTargets.Count <= ExactTargetLimit && candidates.Count <= ExactCandidateLimit)
        {
            var targetMap = componentTargets.Select((target, local) => (target, local))
                .ToDictionary(x => x.target, x => x.local);
            var masks = candidates.Select(candidate => candidate.Targets.Aggregate(0UL,
                (mask, target) => mask | 1UL << targetMap[target])).ToArray();
            var exact = SolveExact(masks, componentTargets.Count);
            return exact.Select(x => candidates[x]).ToArray();
        }

        return SolveGreedy(componentTargets, candidates);
    }

    private static IReadOnlyList<int> SolveExact(IReadOnlyList<ulong> masks, int targetCount)
    {
        var all = targetCount == 64 ? ulong.MaxValue : (1UL << targetCount) - 1;
        var candidatesByTarget = new List<int>[targetCount];
        for (var i = 0; i < targetCount; i++) candidatesByTarget[i] = [];
        for (var candidate = 0; candidate < masks.Count; candidate++)
            for (var target = 0; target < targetCount; target++)
                if ((masks[candidate] & 1UL << target) != 0)
                    candidatesByTarget[target].Add(candidate);

        var incumbent = SolveGreedyMasks(masks, all);
        var current = new List<int>();
        var bestDepthByCovered = new Dictionary<ulong, int>();
        var visitedNodes = 0;

        Search(0);
        return incumbent;

        void Search(ulong covered)
        {
            if (++visitedNodes > ExactSearchNodeLimit) return;
            if (covered == all)
            {
                if (current.Count < incumbent.Count) incumbent = current.ToList();
                return;
            }

            if (current.Count >= incumbent.Count - 1) return;
            if (bestDepthByCovered.TryGetValue(covered, out var previousDepth) && previousDepth <= current.Count) return;
            bestDepthByCovered[covered] = current.Count;

            var remaining = all & ~covered;
            var maxGain = masks.Max(mask => BitOperations.PopCount(mask & remaining));
            if (maxGain == 0) return;
            var lowerBound = (BitOperations.PopCount(remaining) + maxGain - 1) / maxGain;
            if (current.Count + lowerBound >= incumbent.Count) return;

            var target = Enumerable.Range(0, targetCount)
                .Where(x => (remaining & 1UL << x) != 0)
                .MinBy(x => candidatesByTarget[x].Count(candidate => (masks[candidate] & remaining) != 0));
            var branches = candidatesByTarget[target]
                .Where(candidate => (masks[candidate] & remaining) != 0)
                .OrderByDescending(candidate => BitOperations.PopCount(masks[candidate] & remaining))
                .ThenBy(candidate => candidate);

            foreach (var candidate in branches)
            {
                current.Add(candidate);
                Search(covered | masks[candidate]);
                current.RemoveAt(current.Count - 1);
                if (visitedNodes > ExactSearchNodeLimit) return;
            }
        }
    }

    private static List<int> SolveGreedyMasks(IReadOnlyList<ulong> masks, ulong all)
    {
        var selected = new List<int>();
        var covered = 0UL;
        while (covered != all)
        {
            var best = Enumerable.Range(0, masks.Count)
                .MaxBy(x => BitOperations.PopCount(masks[x] & ~covered));
            if ((masks[best] & ~covered) == 0)
                throw new InvalidOperationException("No SLA candidate covers the remaining notes.");
            selected.Add(best);
            covered |= masks[best];
        }

        return selected;
    }

    private static IReadOnlyList<SlaCandidate> SolveGreedy(
        IReadOnlyList<int> targets,
        IReadOnlyList<SlaCandidate> candidates)
    {
        var uncovered = targets.ToHashSet();
        var selected = new List<SlaCandidate>();
        while (uncovered.Count > 0)
        {
            var best = candidates
                .Select((candidate, index) => new
                {
                    Candidate = candidate,
                    Index = index,
                    Gain = candidate.Targets.Count(uncovered.Contains)
                })
                .OrderByDescending(x => x.Gain)
                .ThenBy(x => x.Index)
                .First();
            if (best.Gain == 0)
                throw new InvalidOperationException("No SLA candidate covers the remaining notes.");

            selected.Add(best.Candidate);
            uncovered.ExceptWith(best.Candidate.Targets);
        }

        // Remove candidates made redundant by later, wider choices.
        for (var i = selected.Count - 1; i >= 0; i--)
        {
            var without = selected.Where((_, index) => index != i)
                .SelectMany(x => x.Targets)
                .ToHashSet();
            if (!targets.All(without.Contains)) continue;
            selected.RemoveAt(i);
        }

        return selected;
    }

    private static int LowerBound(IReadOnlyList<int> values, int value)
    {
        var left = 0;
        var right = values.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (values[middle] < value) left = middle + 1;
            else right = middle;
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
        if (result != 0) return result;
        result = left.Length.CompareTo(right.Length);
        if (result != 0) return result;
        result = left.Width.CompareTo(right.Width);
        if (result != 0) return result;
        result = left.Tick.CompareTo(right.Tick);
        return result != 0 ? result : left.Lane.CompareTo(right.Lane);
    }

    private readonly record struct NoteInfo(int Index, int Tick, int Left, int Right);

    private sealed record SlaCandidate(SlaPlacement Placement, int[] Targets);

    private sealed class DisjointSet(int count)
    {
        private readonly int[] _parents = Enumerable.Range(0, count).ToArray();
        private readonly byte[] _ranks = new byte[count];

        public int Find(int value)
        {
            if (_parents[value] != value) _parents[value] = Find(_parents[value]);
            return _parents[value];
        }

        public void Union(int left, int right)
        {
            left = Find(left);
            right = Find(right);
            if (left == right) return;
            if (_ranks[left] < _ranks[right]) (left, right) = (right, left);
            _parents[right] = left;
            if (_ranks[left] == _ranks[right]) _ranks[left]++;
        }
    }
}
