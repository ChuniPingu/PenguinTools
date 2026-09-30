using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace PenguinTools.Chart;

public static class ChartStatistics
{
    record Edge(string Tag, decimal Start, decimal End, string StartKey, string EndKey, string Parent, bool Judge, decimal Density, decimal StartHeight = 0, decimal EndHeight = 0);
    public static string Calculate(string text)
    {
        var rows = text.Replace("\r", "").Split('\n').Select(s => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length > 0 && !p[0].StartsWith("//")).ToArray();
        foreach (var row in rows)
        for (int i = 1; i < row.Length; i++)
            if (decimal.TryParse(row[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                row[i] = value.ToString("G29", CultureInfo.InvariantCulture);
        decimal N(string[] p, int i) => decimal.Parse(p[i], CultureInfo.InvariantCulture);
        decimal resolution = rows.Where(p => p[0] == "RESOLUTION").Select(p => N(p, 1)).FirstOrDefault(384);
        decimal Tick(string[] p) => N(p, 1) * resolution + N(p, 2);
        var bpms = rows.Where(p => p[0] == "BPM").Select(p => (Tick: Tick(p), Bpm: N(p, 3))).OrderBy(p => p.Tick).ToArray();
        decimal judgeBpm = rows.Where(p => p[0] == "PROGJUDGE_BPM").Select(p => N(p, 1)).FirstOrDefault(240);
        decimal bpmDefault = rows.Where(p => p[0] == "BPM_DEF").Select(p => N(p, 1)).FirstOrDefault(120);
        decimal Step(decimal t)
        {
            decimal b = bpmDefault;
            foreach (var p in bpms)
            {
                if (p.Tick > t)
                    break;
                b = p.Bpm;
            }
            decimal step = 96;
            if (b <= 0 || judgeBpm <= 0)
                throw new FormatException("BPM must be positive");
            while (b < judgeBpm)
            {
                step /= 2;
                b *= 2;
            } while (b >= judgeBpm * 2)
            {
                step *= 2;
                b /= 2;
            }
            return Math.Max(1, Math.Floor(Math.Min(step, 384))) * resolution / 384;
        }
        bool tutorial = rows.Any(p => p[0] == "TUTORIAL" && p[1] == "1");
        bool Silent(decimal t) => tutorial && rows.Any(p =>
         (p[0] == "SFL" || (p[0] == "SLP" && p.Length > 5 && N(p, 5) == 0) || p[0] == "STP") &&
         (p[0] == "STP" || N(p, 4) == 0) && t > Tick(p) && t < Tick(p) + N(p, 3));
        var counts = new long[5];
        var judges = new List<decimal>();
        void Add(int cat, decimal tick)
        {
            counts[cat]++;
            judges.Add(tick);
        }
        void Interior(int cat, decimal start, decimal end, bool skipFirst = false)
        {
            bool first = true;
            for (decimal t = start; t < end;)
            {
                t += Step(t);
                if (t >= end)
                    break;
                if ((!skipFirst || !first) && !Silent(t))
                    Add(cat, t);
                first = false;
            }
        }
        var attached = new Dictionary<string, int>();
        foreach (var p in rows)
        if (p.Length > 5 && (p[0] is "AIR" or "AUL" or "AUR" or "ADW" or "ADL" or "ADR" or "ASC" or "ASD" or "AHD" or "AHX" or "ASX"))
        {
            var key = $"{Tick(p)}|{p[3]}|{p[4]}|{p[5]}";
            attached[key] = attached.GetValueOrDefault(key) + 1;
        }
        bool Attached(decimal t, string lane, string width, string tag)
        {
            string k = $"{t}|{lane}|{width}|{tag}";
            if (attached.GetValueOrDefault(k) <= 0)
                return false;
            attached[k]--;
            return true;
        }
        void LongJudges(int cat, decimal start, decimal end, decimal limit, bool endJudge, bool clip = false)
        {
            for (decimal t = start; t < end;)
            {
                t += Step(t);
                if (t >= end)
                    break;
                if (t <= limit && (!clip || t + Step(t) <= limit) && !Silent(t) && !(tutorial && clip))
                    Add(cat, t);
            }
            if (endJudge && end <= limit && (!clip || end + Step(end) <= limit))
                Add(cat, end);
        }
        var ground = new List<Edge>();
        var air = new List<Edge>();
        var crash = new List<Edge>();
        string Key(decimal t, string lane, string width, string extra = "") => $"{t}|{lane}|{width}|{extra}";
        foreach (var p in rows)
        {
            var tag = p[0];
            if (!ChartExtras.NoteTags.Contains(tag))
                continue;
            decimal t = Tick(p);
            if (tag is "TAP" or "CHR" or "MNE")
                Add(0, t);
            else if (tag == "FLK")
                Add(4, t);
            else if (tag is "AIR" or "AUL" or "AUR" or "ADW" or "ADL" or "ADR")
                Add(3, t);
            else if (tag is "HLD" or "HXD")
            {
                decimal end = t + N(p, 5);
                Add(0, t);
                bool a = Attached(end, p[3], p[4], "HLD");
                LongJudges(1, t, end, end, !a, a);
            }
            else if (tag is "SLC" or "SLD" or "SXC" or "SXD")
            {
                decimal end = t + N(p, 5);
                ground.Add(new(tag, t, end, Key(t, p[3], p[4]), Key(end, p[6], p.Length > 7 ? p[7] : p[4]), "", tag.EndsWith("D"), 0));
            }
            else if (tag is "ASC" or "ASD")
            {
                decimal end = t + N(p, 7);
                air.Add(new(tag, t, end, Key(t, p[3], p[4], p[6] + "|" + p[11] + "|" + p[5]), Key(end, p[8], p[9], p[10] + "|" + p[11] + "|" + tag), p[5], tag == "ASD", 0, N(p, 6), N(p, 10)));
            }
            else if (tag is "AHD" or "AHX" or "ASX")
            {
                decimal end = t + N(p, 6);
                air.Add(new(tag, t, end, Key(t, p[3], p[4], "H|" + p[5]), Key(end, p[3], p[4], "H|" + tag), p[5], tag == "AHD", 0));
            }
            else if (tag == "ALD")
            {
                decimal end = t + N(p, 7);
                string extra = string.Join("|", p.Skip(11)) + "|" + p[5];
                crash.Add(new(tag, t, end, Key(t, p[3], p[4], p[6] + "|" + extra), Key(end, p[8], p[9], p[10] + "|" + extra), "", false, N(p, 5)));
            }
        }
        var parentOrder = new Dictionary<string, int>();
        for (int index = 0; index < rows.Length; index++)
        {
            var p = rows[index];
            if (!ChartExtras.NoteTags.Contains(p[0]))
                continue;
            if (p[0] is "TAP" or "CHR" or "FLK" or "MNE")
                parentOrder.TryAdd(Key(Tick(p), p[3], p[4], p[0]), index);
            if (p[0] is "HLD" or "HXD")
                parentOrder.TryAdd(Key(Tick(p) + N(p, 5), p[3], p[4], "HLD"), index);
        }
        int RootOrder(Edge e)
        {
            var parts = e.StartKey.Split('|');
            return parentOrder.GetValueOrDefault(Key(e.Start, parts[1], parts[2], e.Parent), int.MaxValue);
        }
        List<List<Edge>> Paths(List<Edge> edges, Func<Edge, bool> canJoin, bool airMode = false)
        {
            var ordered = edges.OrderBy(e => e.Start).ThenBy(e => decimal.Parse(e.StartKey.Split('|')[1], CultureInfo.InvariantCulture)).ThenBy(e => decimal.Parse(e.StartKey.Split('|')[2], CultureInfo.InvariantCulture)).ToArray();
            var used = new bool[ordered.Length];
            var paths = new List<List<Edge>>();
            var byStart = Enumerable.Range(0, ordered.Length).GroupBy(i => ordered[i].StartKey).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (int seed in Enumerable.Range(0, ordered.Length).OrderBy(i => airMode && canJoin(ordered[i]) ? 1 : 0).ThenBy(i => airMode ? RootOrder(ordered[i]) : i))
            {
                if (used[seed])
                    continue;
                var path = new List<Edge>();
                int index = seed;
                while (index >= 0)
                {
                    var e = ordered[index];
                    used[index] = true;
                    path.Add(e);
                    index = -1;
                    if (byStart.TryGetValue(e.EndKey, out var next))
                    foreach (var k in next)
                    if (!used[k] && canJoin(ordered[k]))
                    {
                        index = k;
                        break;
                    }
                }
                paths.Add(path);
            }
            return paths;
        }
        var groundPaths = Paths(ground, _ => true);
        foreach (var path in groundPaths)
        {
            var first = path[0];
            var f = first.StartKey.Split('|');
            var last = path[^1];
            var e = last.EndKey.Split('|');
            int index = Array.FindIndex(rows, p => p.Length > 4 && p[0] == first.Tag && Tick(p) == first.Start && p[3] == f[1] && p[4] == f[2]);
            parentOrder.TryAdd(Key(last.End, e[1], e[2], "SLD"), index);
        }
        foreach (var path in groundPaths)
        {
            decimal last = path[0].Start;
            Add(0, last);
            var final = path[^1];
            var key = final.EndKey.Split('|');
            bool a = Attached(final.End, key[1], key[2], "SLD");
            decimal limit = final.End;
            foreach (var e in path)
            if (e.Judge)
            {
                LongJudges(2, last, e.End, limit, true, a);
                last = e.End;
            }
            if (!final.Judge)
                LongJudges(2, last, final.End, limit, true, a);
        }
        foreach (var path in Paths(air, e => e.Parent is "ASC" or "ASD" or "AHD" or "AHX" or "ASX", true))
        {
            decimal last = path[0].Start;
            Add(3, last);
            foreach (var e in path)
            if (e.Judge)
            {
                Interior(3, last, e.End, true);
                Add(3, e.End);
                last = e.End;
            }
            if (!path[^1].Judge)
            {
                decimal end = path[^1].End;
                bool first = true;
                for (decimal t = last; t < end;)
                {
                    t += Step(t);
                    if (t >= end)
                        break;
                    if (t + Step(t) <= end && !first && !Silent(t))
                        Add(3, t);
                    first = false;
                }
            }
        }
        foreach (var path in Paths(crash, _ => true))
        {
            decimal period = path[0].Density;
            if (period <= 0)
                continue;
            decimal start = path[0].Start, end = path[^1].End;
            Add(3, start);
            for (decimal t = start + period; t <= end; t += period)
                Add(3, t);
        }
        string[] labels = { "TAP", "HLD", "SLD", "AIR", "FLK" };
        return "\r\n" + string.Join("\r\n", labels.Select((s, i) => $"T_JUDGE_{s}\t{counts[i]}")) + $"\r\nT_JUDGE_ALL\t{counts.Sum()}\r\n";
    }
}
