using System.Globalization;
namespace PenguinTools.Chart;

public static class ChartStatistics
{
    private sealed record Edge(string Tag, decimal Start, decimal End, string StartKey, string EndKey, string Parent, bool Judge, decimal Density, decimal StartHeight = 0, decimal EndHeight = 0);

    public static string Calculate(string text) => new Calculator(text).BuildSummary();

    private sealed class Calculator
    {
        private readonly string[][] _rows;
        private readonly decimal _resolution;
        private readonly (decimal Tick, decimal Bpm)[] _bpms;
        private readonly decimal _judgeBpm;
        private readonly decimal _bpmDefault;
        private readonly bool _tutorial;
        private readonly long[] _counts = new long[5];
        private readonly Dictionary<string, int> _attached = [];
        private readonly Dictionary<string, int> _parentOrder = [];
        private readonly List<Edge> _ground = [];
        private readonly List<Edge> _air = [];
        private readonly List<Edge> _crash = [];

        public Calculator(string text)
        {
            _rows = text.Replace("\r", "").Split('\n').Select(s => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length > 0 && !p[0].StartsWith("//", StringComparison.Ordinal)).ToArray();
            foreach (var row in _rows)
            {
                NormalizeRow(row);
            }

            _resolution = _rows.Where(p => p[0] == "RESOLUTION").Select(p => N(p, 1)).FirstOrDefault(384);
            _bpms = _rows.Where(p => p[0] == "BPM").Select(p => (Tick: Tick(p), Bpm: N(p, 3))).OrderBy(p => p.Tick).ToArray();
            _judgeBpm = _rows.Where(p => p[0] == "PROGJUDGE_BPM").Select(p => N(p, 1)).FirstOrDefault(240);
            _bpmDefault = _rows.Where(p => p[0] == "BPM_DEF").Select(p => N(p, 1)).FirstOrDefault(120);
            _tutorial = _rows.Any(p => p[0] == "TUTORIAL" && p[1] == "1");
        }

        public string BuildSummary()
        {
            CollectAttachments();
            foreach (var row in _rows)
            {
                CollectNote(row);
            }

            CollectParentOrder();
            var groundPaths = Paths(_ground, _ => true);
            CollectSlideOrder(groundPaths);
            CountGround(groundPaths);
            CountAir();
            CountCrashes();
            string[] labels = { "TAP", "HLD", "SLD", "AIR", "FLK" };
            return "\r\n" + string.Join("\r\n", labels.Select((s, i) => $"T_JUDGE_{s}\t{_counts[i]}")) + $"\r\nT_JUDGE_ALL\t{_counts.Sum()}\r\n";
        }

        private static void NormalizeRow(string[] row)
        {
            for (int i = 1; i < row.Length; i++)
            {
                if (decimal.TryParse(row[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    row[i] = value.ToString("G29", CultureInfo.InvariantCulture);
                }
            }
        }

        private static decimal N(string[] p, int i) => decimal.Parse(p[i], CultureInfo.InvariantCulture);
        private decimal Tick(string[] p) => N(p, 1) * _resolution + N(p, 2);
        private static string Key(decimal t, string lane, string width, string extra = "") => $"{t}|{lane}|{width}|{extra}";
        private decimal Step(decimal t)
        {
            decimal b = _bpmDefault;
            foreach (var p in _bpms)
            {
                if (p.Tick > t)
                {
                    break;
                }

                b = p.Bpm;
            }
            decimal step = 96;
            if (b <= 0 || _judgeBpm <= 0)
            {
                throw new FormatException("BPM must be positive");
            }

            while (b < _judgeBpm)
            {
                step /= 2;
                b *= 2;
            } while (b >= _judgeBpm * 2)
            {
                step *= 2;
                b /= 2;
            }
            return Math.Max(1, Math.Floor(Math.Min(step, 384))) * _resolution / 384;
        }
        private bool Silent(decimal t) => _tutorial && _rows.Any(p =>
             (p[0] == "SFL" || (p[0] == "SLP" && p.Length > 5 && N(p, 5) == 0) || p[0] == "STP") &&
             (p[0] == "STP" || N(p, 4) == 0) && t > Tick(p) && t < Tick(p) + N(p, 3));
        private void Add(int cat)
        {
            _counts[cat]++;
        }
        private void Interior(int cat, decimal start, decimal end, bool skipFirst = false)
        {
            bool first = true;
            for (decimal t = start; t < end;)
            {
                t += Step(t);
                if (t >= end)
                {
                    break;
                }

                if ((!skipFirst || !first) && !Silent(t))
                {
                    Add(cat);
                }

                first = false;
            }
        }
        private bool Attached(decimal t, string lane, string width, string tag)
        {
            string k = $"{t}|{lane}|{width}|{tag}";
            if (_attached.GetValueOrDefault(k) <= 0)
            {
                return false;
            }

            _attached[k]--;
            return true;
        }
        private void LongJudges(int cat, decimal start, decimal end, decimal limit, bool endJudge, bool clip = false)
        {
            for (decimal t = start; t < end;)
            {
                t += Step(t);
                if (t >= end)
                {
                    break;
                }

                if (t <= limit && (!clip || t + Step(t) <= limit) && !Silent(t) && !(_tutorial && clip))
                {
                    Add(cat);
                }
            }
            if (endJudge && end <= limit && (!clip || end + Step(end) <= limit))
            {
                Add(cat);
            }
        }
        private int RootOrder(Edge e)
        {
            var parts = e.StartKey.Split('|');
            return _parentOrder.GetValueOrDefault(Key(e.Start, parts[1], parts[2], e.Parent), int.MaxValue);
        }
        private List<List<Edge>> Paths(List<Edge> edges, Func<Edge, bool> canJoin, bool airMode = false)
        {
            var ordered = edges.OrderBy(e => e.Start).ThenBy(e => decimal.Parse(e.StartKey.Split('|')[1], CultureInfo.InvariantCulture)).ThenBy(e => decimal.Parse(e.StartKey.Split('|')[2], CultureInfo.InvariantCulture)).ToArray();
            var used = new bool[ordered.Length];
            var paths = new List<List<Edge>>();
            var byStart = Enumerable.Range(0, ordered.Length).GroupBy(i => ordered[i].StartKey).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (int seed in Enumerable.Range(0, ordered.Length).OrderBy(i => airMode && canJoin(ordered[i]) ? 1 : 0).ThenBy(i => airMode ? RootOrder(ordered[i]) : i))
            {
                if (used[seed])
                {
                    continue;
                }

                var path = new List<Edge>();
                int index = seed;
                while (index >= 0)
                {
                    var e = ordered[index];
                    used[index] = true;
                    path.Add(e);
                    index = FindNextEdge(e, byStart, ordered, used, canJoin);
                }
                paths.Add(path);
            }
            return paths;
        }

        private void CollectAttachments()
        {
            foreach (var p in _rows.Where(p => p.Length > 5 && (p[0] is "AIR" or "AUL" or "AUR" or "ADW" or "ADL" or "ADR" or "ASC" or "ASD" or "AHD" or "AHX" or "ASX")))
            {
                var key = $"{Tick(p)}|{p[3]}|{p[4]}|{p[5]}";
                _attached[key] = _attached.GetValueOrDefault(key) + 1;
            }
        }

        private void CollectNote(string[] p)
        {
            var tag = p[0];
            if (!ChartExtras.NoteTags.Contains(tag))
            {
                return;
            }

            decimal t = Tick(p);
            if (tag is "TAP" or "CHR" or "MNE")
            {
                Add(0);
            }
            else if (tag == "FLK")
            {
                Add(4);
            }
            else if (tag is "AIR" or "AUL" or "AUR" or "ADW" or "ADL" or "ADR")
            {
                Add(3);
            }
            else if (tag is "HLD" or "HXD")
            {
                CountHold(p, t);
            }
            else if (tag is "SLC" or "SLD" or "SXC" or "SXD")
            {
                CollectSlide(p, t);
            }
            else if (tag is "ASC" or "ASD")
            {
                decimal end = t + N(p, 7);
                _air.Add(new(tag, t, end, Key(t, p[3], p[4], p[6] + "|" + p[11] + "|" + p[5]), Key(end, p[8], p[9], p[10] + "|" + p[11] + "|" + tag), p[5], tag == "ASD", 0, N(p, 6), N(p, 10)));
            }
            else if (tag is "AHD" or "AHX" or "ASX")
            {
                decimal end = t + N(p, 6);
                _air.Add(new(tag, t, end, Key(t, p[3], p[4], "H|" + p[5]), Key(end, p[3], p[4], "H|" + tag), p[5], tag == "AHD", 0));
            }
            else if (tag == "ALD")
            {
                decimal end = t + N(p, 7);
                string extra = string.Join("|", p.Skip(11)) + "|" + p[5];
                _crash.Add(new(tag, t, end, Key(t, p[3], p[4], p[6] + "|" + extra), Key(end, p[8], p[9], p[10] + "|" + extra), "", false, N(p, 5)));
            }
        }

        private void CountHold(string[] p, decimal tick)
        {
            decimal end = tick + N(p, 5);
            Add(0);
            bool attachedAir = Attached(end, p[3], p[4], "HLD");
            LongJudges(1, tick, end, end, !attachedAir, attachedAir);
        }

        private void CollectSlide(string[] p, decimal tick)
        {
            decimal end = tick + N(p, 5);
            _ground.Add(new(p[0], tick, end, Key(tick, p[3], p[4]), Key(end, p[6], p.Length > 7 ? p[7] : p[4]), "", p[0].EndsWith('D'), 0));
        }

        private void CollectParentOrder()
        {
            for (int index = 0; index < _rows.Length; index++)
            {
                var p = _rows[index];
                if (!ChartExtras.NoteTags.Contains(p[0]))
                {
                    continue;
                }

                if (p[0] is "TAP" or "CHR" or "FLK" or "MNE")
                {
                    _parentOrder.TryAdd(Key(Tick(p), p[3], p[4], p[0]), index);
                }

                if (p[0] is "HLD" or "HXD")
                {
                    _parentOrder.TryAdd(Key(Tick(p) + N(p, 5), p[3], p[4], "HLD"), index);
                }
            }
        }

        private void CollectSlideOrder(List<List<Edge>> groundPaths)
        {
            foreach (var path in groundPaths)
            {
                var first = path[0];
                var f = first.StartKey.Split('|');
                var last = path[^1];
                var e = last.EndKey.Split('|');
                int index = Array.FindIndex(_rows, p => p.Length > 4 && p[0] == first.Tag && Tick(p) == first.Start && p[3] == f[1] && p[4] == f[2]);
                _parentOrder.TryAdd(Key(last.End, e[1], e[2], "SLD"), index);
            }
        }

        private void CountGround(List<List<Edge>> groundPaths)
        {
            foreach (var path in groundPaths)
            {
                decimal last = path[0].Start;
                Add(0);
                var final = path[^1];
                var key = final.EndKey.Split('|');
                bool a = Attached(final.End, key[1], key[2], "SLD");
                decimal limit = final.End;
                foreach (var end in path.Where(e => e.Judge).Select(e => e.End))
                {
                    LongJudges(2, last, end, limit, true, a);
                    last = end;
                }
                if (!final.Judge)
                {
                    LongJudges(2, last, final.End, limit, true, a);
                }
            }
        }

        private void CountAir()
        {
            foreach (var path in Paths(_air, e => e.Parent is "ASC" or "ASD" or "AHD" or "AHX" or "ASX", true))
            {
                decimal last = path[0].Start;
                Add(3);
                foreach (var end in path.Where(e => e.Judge).Select(e => e.End))
                {
                    Interior(3, last, end, true);
                    Add(3);
                    last = end;
                }
                if (!path[^1].Judge)
                {
                    CountAirTail(last, path[^1].End);
                }
            }
        }

        private void CountAirTail(decimal last, decimal end)
        {
            bool first = true;
            for (decimal t = last; t < end;)
            {
                t += Step(t);
                if (t >= end)
                {
                    break;
                }

                if (t + Step(t) <= end && !first && !Silent(t))
                {
                    Add(3);
                }

                first = false;
            }
        }

        private void CountCrashes()
        {
            foreach (var path in Paths(_crash, _ => true))
            {
                decimal period = path[0].Density;
                if (period <= 0)
                {
                    continue;
                }

                decimal start = path[0].Start, end = path[^1].End;
                Add(3);
                for (decimal t = start + period; t <= end; t += period)
                {
                    Add(3);
                }
            }
        }

        private static int FindNextEdge(Edge edge, Dictionary<string, int[]> byStart, Edge[] ordered, bool[] used, Func<Edge, bool> canJoin)
        {
            if (!byStart.TryGetValue(edge.EndKey, out var next))
            {
                return -1;
            }

            return next.FirstOrDefault(k => !used[k] && canJoin(ordered[k]), -1);
        }
    }
}
