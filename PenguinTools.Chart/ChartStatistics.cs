using System.Globalization;

namespace PenguinTools.Chart;

public static partial class ChartStatistics
{
    public static string Calculate(string text) => Calculate(
        text.Replace("\r", "").Split('\n').Select(C2SRoundTrip.Parts));

    internal static string Calculate(IEnumerable<string[]> parts, Func<decimal, float, Exception>? collapsedStep = null) =>
        new Calculator(parts, collapsedStep).BuildSummary();

    private sealed partial class Calculator
    {
        private readonly string[][] _rows;
        private readonly decimal _resolution;
        private readonly (decimal Tick, float Bpm)[] _bpms;
        private readonly float _judgeBpm;
        private readonly float _airExclusion;
        private readonly bool _tutorial;
        private readonly (decimal Start, decimal End, bool Stops)[] _tutorialIntervals;
        private readonly Func<decimal, float, Exception>? _collapsedStep;
        private readonly long[] _counts = new long[5];
        private readonly List<Note> _notes = [];

        public Calculator(IEnumerable<string[]> parts, Func<decimal, float, Exception>? collapsedStep)
        {
            _rows = parts
                .Where(p => p.Length > 0 && !p[0].StartsWith("//", StringComparison.Ordinal)).ToArray();
            _resolution = Header("RESOLUTION", 384);
            _collapsedStep = collapsedStep;
            _bpms = _rows.Where(p => p[0] == "BPM")
                .Select(p => (Tick: Tick(p), Bpm: float.Parse(p[3], CultureInfo.InvariantCulture))).OrderBy(p => p.Tick).ToArray();
            _judgeBpm = FloatHeader("PROGJUDGE_BPM", 240);
            _airExclusion = FloatHeader("PROGJUDGE_AER", 0.999f);
            _tutorial = Header("TUTORIAL", 0) > 0;
            _tutorialIntervals = !_tutorial ? [] : _rows
                .Where(p => p[0] is "STP" or "SFL" || p[0] == "SLP" && I(p, 5) == 0)
                .Select(p => (Tick(p), Tick(p) + N(p, 3), p[0] == "STP" || Float(p, 4) == 0))
                .ToArray();
        }

        private decimal Header(string tag, decimal fallback) =>
            _rows.Where(p => p[0] == tag).Select(p => N(p, 1)).LastOrDefault(fallback);
        private float FloatHeader(string tag, float fallback) =>
            _rows.Where(p => p[0] == tag).Select(p => float.Parse(p[1], CultureInfo.InvariantCulture)).LastOrDefault(fallback);
        private static decimal N(string[] p, int i) => decimal.Parse(p[i], CultureInfo.InvariantCulture);
        private static int I(string[] p, int i) =>
            i < p.Length ? int.Parse(p[i], CultureInfo.InvariantCulture) : 0;
        private static float Float(string[] p, int i) =>
            i < p.Length ? float.Parse(p[i], CultureInfo.InvariantCulture) : 0;
        private decimal Tick(string[] p) => N(p, 1) * _resolution + N(p, 2);

        public string BuildSummary()
        {
            ReadNotes();
            foreach (var note in _notes)
            {
                CountNote(note);
            }
            string[] labels = ["TAP", "HLD", "SLD", "AIR", "FLK"];
            return "\r\n" + string.Join("\r\n", labels.Select((s, i) =>
                string.Create(CultureInfo.InvariantCulture, $"T_JUDGE_{s}\t{_counts[i]}"))) +
                string.Create(CultureInfo.InvariantCulture, $"\r\nT_JUDGE_ALL\t{_counts.Sum()}\r\n");
        }

        private decimal Step(decimal tick)
        {
            var bpm = _bpms.FirstOrDefault((0, 150f)).Bpm;
            foreach (var change in _bpms)
            {
                if (change.Tick > tick)
                {
                    break;
                }
                bpm = change.Bpm;
            }
            if (bpm <= 0)
            {
                throw new FormatException("BPM must be positive.");
            }
            var step = 384;
            while (bpm < _judgeBpm * 4)
            {
                bpm *= 2;
                step /= 2;
                if (step == 0)
                {
                    throw _collapsedStep?.Invoke(tick, bpm) ??
                        new FormatException("Judgement interval is smaller than one C2S tick.");
                }
            }
            return step * _resolution / 384;
        }

        private bool Silent(decimal tick)
        {
            // Judge points are built before speed intervals are sorted.
            foreach (var (start, end, stops) in _tutorialIntervals)
            {
                if (start >= tick)
                {
                    return false;
                }
                if (stops && tick < end)
                {
                    return true;
                }
            }
            return false;
        }

        private bool ExcludedByAir(decimal tick, decimal end)
        {
            if (_airExclusion < 0)
            {
                return false;
            }
            var margin = MathF.Floor((float)(Step(tick) * 384 / _resolution) * _airExclusion);
            return tick + (decimal)margin * _resolution / 384 >= end;
        }

        private void CountNote(Note note)
        {
            switch (note.Kind)
            {
                case "TAP" or "CHR" or "MNE":
                    _counts[0]++;
                    break;
                case "FLK":
                    _counts[4]++;
                    break;
                case "HLD" or "HHD" or "SLD":
                    _counts[0]++;
                    CountGround(note);
                    break;
                case "ALD" when note.Density > 0 && note.End.Tick > note.Start.Tick:
                    _counts[3] += 1 + (long)Math.Floor((note.End.Tick - note.Start.Tick) / note.Density);
                    break;
            }
            if (note.Air is { } air)
            {
                CountAir(note, air);
            }
        }

        private void CountGround(Note note)
        {
            var category = note.Kind == "SLD" ? 2 : 1;
            var start = note.Start.Tick;
            var cursor = start;
            var restart = true;
            var segments = category == 2 ? note.Path : note.Path.TakeLast(1);
            foreach (var segment in segments)
            {
                var step = Step(start);
                if (restart)
                {
                    cursor = start + step;
                }
                for (; cursor < segment.End.Tick; cursor += Step(cursor))
                {
                    CountGroundPoint(note, category, cursor);
                }
                if (segment.Judge || ReferenceEquals(segment, note.Path[^1]))
                {
                    CountGroundPoint(note, category, segment.End.Tick);
                }
                start = segment.End.Tick;
                restart = segment.Judge;
            }
        }

        private void CountGroundPoint(Note note, int category, decimal tick)
        {
            var excludedByAir = note.Air is not null && ExcludedByAir(tick, note.End.Tick);
            if (!excludedByAir && !Silent(tick) && (!_tutorial || note.Air is null))
            {
                _counts[category]++;
            }
        }

        private void CountAir(Note note, Air air)
        {
            _counts[3]++;
            if (air.Kind == "AIR")
            {
                return;
            }

            var start = note.End.Tick;
            var cursor = start;
            var first = true;
            var restart = true;
            var end = air.Path[^1].End.Tick;
            var openEnd = !air.Path[^1].Judge && _airExclusion >= 0;
            foreach (var segment in air.Path)
            {
                if (air.Kind == "AHD" && !segment.Judge && !ReferenceEquals(segment, air.Path[^1]))
                {
                    continue;
                }
                var step = Step(start);
                if (restart)
                {
                    cursor = start + step;
                    first = true;
                }
                for (; cursor < segment.End.Tick; cursor += Step(cursor))
                {
                    var excluded = openEnd && ExcludedByAir(cursor, end);
                    if (!first && !excluded && !Silent(cursor))
                    {
                        _counts[3]++;
                    }
                    first = false;
                }
                if (segment.Judge)
                {
                    _counts[3]++;
                }
                start = segment.End.Tick;
                restart = segment.Judge;
            }
            if (openEnd)
            {
                _ = Step(end);
            }
        }
    }
}
