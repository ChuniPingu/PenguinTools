using System.Globalization;

namespace PenguinTools.Chart;

public static partial class ChartStatistics
{
    public static string Calculate(string text) => new Calculator(text).BuildSummary();

    private sealed partial class Calculator
    {
        private readonly string[][] _rows;
        private readonly decimal _resolution;
        private readonly (decimal Tick, float Bpm)[] _bpms;
        private readonly float _judgeBpm;
        private readonly float _airExclusion;
        private readonly bool _tutorial;
        private readonly long[] _counts = new long[5];
        private readonly List<Note> _notes = [];

        public Calculator(string text)
        {
            _rows = text.Replace("\r", "").Split('\n')
                .Select(C2SRoundTrip.Parts)
                .Where(p => p.Length > 0 && !p[0].StartsWith("//", StringComparison.Ordinal)).ToArray();
            _resolution = Header("RESOLUTION", 384);
            _bpms = _rows.Where(p => p[0] == "BPM")
                .Select(p => (Tick: Tick(p), Bpm: float.Parse(p[3], CultureInfo.InvariantCulture))).OrderBy(p => p.Tick).ToArray();
            _judgeBpm = FloatHeader("PROGJUDGE_BPM", 240);
            _airExclusion = FloatHeader("PROGJUDGE_AER", 0.999f);
            _tutorial = Header("TUTORIAL", 0) == 1;
        }

        private decimal Header(string tag, decimal fallback) =>
            _rows.Where(p => p[0] == tag).Select(p => N(p, 1)).LastOrDefault(fallback);
        private float FloatHeader(string tag, float fallback) =>
            _rows.Where(p => p[0] == tag).Select(p => float.Parse(p[1], CultureInfo.InvariantCulture)).LastOrDefault(fallback);
        private static decimal N(string[] p, int i) => decimal.Parse(p[i], CultureInfo.InvariantCulture);
        private static int I(string[] p, int i) => int.Parse(p[i], CultureInfo.InvariantCulture);
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
                    throw new FormatException("Judgement interval is smaller than one C2S tick.");
                }
            }
            return step * _resolution / 384;
        }

        private bool Silent(decimal tick) => _tutorial && _rows.Any(p =>
            (p[0] == "STP" || p[0] == "SFL" && N(p, 4) == 0 ||
                p[0] == "SLP" && N(p, 4) == 0 && N(p, 5) == 0) &&
            tick > Tick(p) && tick < Tick(p) + N(p, 3));

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
                case "HLD" or "SLD":
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
            var category = note.Kind == "HLD" ? 1 : 2;
            var start = note.Start.Tick;
            foreach (var segment in note.Path)
            {
                if (segment.Judge || ReferenceEquals(segment, note.Path[^1]))
                {
                    for (var tick = start + Step(start); tick < segment.End.Tick; tick += Step(tick))
                    {
                        CountGroundPoint(note, category, tick);
                    }
                    CountGroundPoint(note, category, segment.End.Tick);
                    start = segment.End.Tick;
                }
            }
        }

        private void CountGroundPoint(Note note, int category, decimal tick)
        {
            if (!Silent(tick) && (note.Air is null || !_tutorial && !ExcludedByAir(tick, note.End.Tick)))
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
            var openEnd = !air.Path[^1].Judge;
            foreach (var segment in air.Path)
            {
                if (!segment.Judge && !ReferenceEquals(segment, air.Path[^1]))
                {
                    continue;
                }
                var first = true;
                for (var tick = start + Step(start); tick < segment.End.Tick; tick += Step(tick))
                {
                    if (!first && !Silent(tick) && (!openEnd || !ExcludedByAir(tick, air.Path[^1].End.Tick)))
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
            }
        }
    }
}
