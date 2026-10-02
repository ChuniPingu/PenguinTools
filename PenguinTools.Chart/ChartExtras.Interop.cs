using System.Globalization;
using System.Text.Json.Serialization;
using C = PenguinTools.Chart.Models.c2s;
namespace PenguinTools.Chart;

// Interoperates with the September editor release's COPYRIGHT metadata.
public sealed partial class ChartExtras
{
    [JsonIgnore] public string SourceSnapshot { get; set; } = "";
    public bool Tutorial
    {
        get; set;
    }
    public List<string> RoundTripBookmarks { get; set; } = [];
    [JsonIgnore] public HashSet<string> UnchangedEventKinds { get; } = [];
    [JsonIgnore] public int Resolution { get; set; } = 384;
    [JsonIgnore]
    public bool FromC2S
    {
        get; set;
    }
    [JsonIgnore] public List<string> SourceRecords { get; } = [];
    public void CheckEventView(PenguinTools.Chart.Models.umgr.Chart chart)
    {
        if (SourceSnapshot.Length == 0)
            return;
        string Canon(string line) => string.Join("\t", C2SRoundTrip.Parts(line).Select(p => decimal.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n.ToString("0.############################", CultureInfo.InvariantCulture) : p));
        var saved = SourceSnapshot.Split('\n').Where(s => s.StartsWith("VIEW\t")).Select(s => Canon(s.Substring(5))).ToArray();
        var hashes = SourceSnapshot.Split('\n').Where(s => s.StartsWith("VIEWHASH\t")).Select(C2SRoundTrip.Parts).ToDictionary(p => p[1], p => p[2]);
        if (saved.Length == 0 && hashes.Count == 0)
            return;
        var current = EventView(chart).Split('\n');
        foreach (string kind in new[] { "BEAT", "BPM", "TIL", "SPDMOD" })
        {
            var a = saved.Where(s => s.StartsWith(kind + "\t")).OrderBy(s => s, StringComparer.Ordinal);
            var b = current.Select(Canon).Where(s => s.StartsWith(kind + "\t")).OrderBy(s => s, StringComparer.Ordinal);
            if (hashes.TryGetValue(kind, out var hash) ? C2SRoundTrip.ViewHash(b) == hash : a.SequenceEqual(b))
                UnchangedEventKinds.Add(kind);
        }
    }
    public string FinalizeText(C.Chart chart, string text)
    {
        var lines = text.Replace("\r", "").Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var headers = new List<string>();
        var events = new List<string>();
        var notes = new List<string>();
        foreach (var line in lines)
        {
            var tag = line.Split('\t')[0];
            if (EventTags.Contains(tag))
                events.Add(line);
            else if (NoteTags.Contains(tag))
                notes.Add(line);
            else if (!tag.StartsWith("T_"))
                headers.Add(line);
        }
        for (int i = 0; i < headers.Count; i++)
        {
            var tag = headers[i].Split('\t')[0];
            if (tag is not ("VERSION" or "RESOLUTION" or "CLK_DEF" or "MUSIC" or "SEQUENCEID" or "DIFFICULT" or "LEVEL") && Headers.TryGetValue(tag, out var saved))
                headers[i] = saved;
        }
        if (FromC2S)
        {
            headers.RemoveAll(s => s.StartsWith("//MGR_CLICK_V1"));

            foreach (var h in Headers)
            if (!headers.Any(s => s.Split('\t')[0] == h.Key))
                headers.Add(h.Value);
        }
        int I(string[] p, int n) => int.Parse(p[n], CultureInfo.InvariantCulture);
        string[] P(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int Rank(string tag) => Array.IndexOf(new[] { "BPM", "MET", "SLP", "SFL", "DCM", "STP", "CLK" }, tag);
        events = events.Select((s, i) => (s, i, p: P(s))).OrderBy(v => Rank(v.p[0])).ThenBy(v => I(v.p, 1)).ThenBy(v => I(v.p, 2)).ThenBy(v => v.i).Select(v => v.s).ToList();
        notes = notes.Select((s, i) => (s, i, p: P(s))).OrderBy(v => I(v.p, 1)).ThenBy(v => I(v.p, 2)).ThenBy(v => I(v.p, 3)).ThenBy(v => I(v.p, 4)).ThenBy(v => v.i).Select(v => v.s).ToList();
        headers.RemoveAll(s => s.StartsWith("//MGR_CLICK_V1"));
        var bpms = events.Select(P).Where(p => p[0] == "BPM").Select(p => decimal.Parse(p[3], CultureInfo.InvariantCulture)).ToArray();
        if (bpms.Length > 0 && !FromC2S)
        {
            int h = headers.FindIndex(s => s.StartsWith("BPM_DEF\t"));
            decimal main = chart.Meta.MainBpm > 0 ? chart.Meta.MainBpm : bpms[0];
            headers[h] = $"BPM_DEF\t{bpms[0]:F3}\t{main:F3}\t{bpms.Max():F3}\t{bpms.Min():F3}";
        }
        string sourceHash = "";
        var savedStats = new List<string>();
        bool slaCompatible = true, judgeOptionsUnchanged = true;
        if (SourceSnapshot.Length > 0)
        {
            var snapshot = SourceSnapshot.Split('\n').Select(P).Where(p => p.Length > 0).ToArray();
            sourceHash = snapshot.First(p => p[0] == "HASH")[1];
            int sourceRes = int.Parse(snapshot.First(p => p[0] == "RESOLUTION")[1]);
            judgeOptionsUnchanged = (snapshot.FirstOrDefault(p => p[0] == "TUTORIAL")?.ElementAtOrDefault(1) == "1") == Tutorial;
            foreach (var p in snapshot.Where(p => p[0] is "PROGJUDGE_BPM" or "PROGJUDGE_AER" || p[0] == "MET_DEF" && UnchangedEventKinds.Contains("BEAT")))
            {
                int h = headers.FindIndex(s => s.StartsWith(p[0] + "\t"));
                if (h >= 0)
                    headers[h] = string.Join("\t", p);
            }
            var usedExceptions = new HashSet<int>();
            foreach (var entry in snapshot.Where(p => p[0] == "COLOR"))
            {
                var original = entry.Skip(1).ToArray();
                var expected = (string[])original.Clone();
                expected[^1] = "DEF";
                string key = C2SRoundTrip.Normalize(string.Join("\t", expected), sourceRes);
                for (int i = 0; i < notes.Count; i++)
                if (!usedExceptions.Contains(i) && C2SRoundTrip.Normalize(notes[i], Resolution) == key)
                {
                    original[2] = ((decimal)I(original, 2) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
                    if (original[0] is "ASC" or "ASD")
                        original[7] = ((decimal)I(original, 7) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
                    notes[i] = string.Join("\t", original);
                    usedExceptions.Add(i);
                    break;
                }
            }
            foreach (var exception in snapshot.Where(p => p[0] == "EXCEPT"))
            {
                var original = exception.Skip(2).ToArray();
                var expected = original.ToList();
                expected[0] = expected[0].EndsWith("D") ? "SXD" : "SXC";
                if (expected.Count == 7)
                    expected.Add(expected[4]);
                if (expected.Count == 8)
                    expected.Add("SLD");
                if (expected.Count == 9)
                    expected.Add(exception[1]);
                else
                    expected[9] = exception[1];
                string key = C2SRoundTrip.Normalize(string.Join("\t", expected), sourceRes);
                for (int i = 0; i < notes.Count; i++)
                if (!usedExceptions.Contains(i) && C2SRoundTrip.Normalize(notes[i], Resolution) == key)
                {
                    original[2] = ((decimal)I(original, 2) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
                    original[5] = ((decimal)I(original, 5) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
                    notes[i] = string.Join("\t", original);
                    usedExceptions.Add(i);
                    break;
                }
            }
            string Kind(string tag) => tag == "MET" ? "BEAT" : tag == "BPM" ? "BPM" : tag == "DCM" ? "SPDMOD" : tag is "SLP" or "SFL" or "STP" ? "TIL" : "";
            events.RemoveAll(s => UnchangedEventKinds.Contains(Kind(P(s)[0])));
            foreach (var original in snapshot.Where(p => UnchangedEventKinds.Contains(Kind(p[0]))))
            {
                var p = (string[])original.Clone();
                p[2] = ((decimal)I(p, 2) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
                if (p[0] is "SLP" or "SFL" or "DCM" or "STP")
                    p[3] = ((decimal)I(p, 3) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
                events.Add(string.Join("\t", p));
            }
            events = events.Select((s, i) => (s, i, p: P(s))).OrderBy(v => Rank(v.p[0])).ThenBy(v => I(v.p, 1)).ThenBy(v => I(v.p, 2)).ThenBy(v => v.i).Select(v => v.s).ToList();
            var regions = snapshot.Where(p => p[0] == "SLA").ToArray();
            foreach (var n in chart.Notes.Where(n => n is not C.Sla))
            {
                int timeline = 0;
                foreach (var p in regions)
                {
                    decimal start = (decimal)I(p, 1) * 1920 + (decimal)I(p, 2) * 1920 / sourceRes, end = start + (decimal)I(p, 5) * 1920 / sourceRes;
                    if (n.Tick.Original >= start && n.Tick.Original < end && n.Lane >= I(p, 3) && n.Lane + n.Width <= I(p, 3) + I(p, 4))
                        timeline = I(p, 6);
                }
                if (n.Timeline != timeline)
                {
                    slaCompatible = false;
                    break;
                }
            }
            // Native saves may reorder events without changing the emitted SLA regions.
            slaCompatible |= regions.Select(p => (
                    Tick: (decimal)I(p, 1) * 1920 + (decimal)I(p, 2) * 1920 / sourceRes,
                    Lane: I(p, 3), Width: I(p, 4), Length: (decimal)I(p, 5) * 1920 / sourceRes,
                    Timeline: I(p, 6))).Order().SequenceEqual(
                chart.Notes.OfType<C.Sla>().Select(n => (
                    Tick: (decimal)n.Tick.Original, n.Lane, n.Width,
                    Length: (decimal)n.Length.Original, n.Timeline)).Order());
            if (slaCompatible)
            {
                notes.RemoveAll(s => s.StartsWith("SLA\t"));
                foreach (var p in regions)
                {
                    p[2] = ((decimal)I(p, 2) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
                    p[5] = ((decimal)I(p, 5) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
                    notes.Add(string.Join("\t", p));
                }
                notes = notes.Select((s, i) => (s, i, p: P(s))).OrderBy(v => I(v.p, 1)).ThenBy(v => I(v.p, 2)).ThenBy(v => I(v.p, 3)).ThenBy(v => I(v.p, 4)).ThenBy(v => v.i).Select(v => v.s).ToList();
            }
            var sourceOrder = new Dictionary<string, Queue<int>>();
            int sourceIndex = 0;
            foreach (var p in snapshot.Where(p => p[0] == "ORDER"))
            {
                string key = C2SRoundTrip.Normalize(string.Join("\t", p.Skip(1)), sourceRes);
                if (!sourceOrder.TryGetValue(key, out var queue))
                    sourceOrder[key] = queue = new Queue<int>();
                queue.Enqueue(sourceIndex++);
            }
            notes = notes.Select((s, i) =>
            {
                string key = C2SRoundTrip.Normalize(s, Resolution);
                int order = sourceOrder.TryGetValue(key, out var q) && q.Count > 0 ? q.Dequeue() : sourceIndex + i;
                return (s, i, order, p: P(s));
            }).OrderBy(v => I(v.p, 1)).ThenBy(v => I(v.p, 2)).ThenBy(v => I(v.p, 3)).ThenBy(v => I(v.p, 4)).ThenBy(v => v.order).ThenBy(v => v.i).Select(v => v.s).ToList();
            savedStats = snapshot.Where(p => p[0].StartsWith("T_")).Select(p => string.Join("\t", p)).ToList();
        }
        var result = string.Join("\r\n", headers) + "\r\n\r\n" + string.Join("\r\n", events) + "\r\n\r\n" + string.Join("\r\n", notes) + "\r\n";
        if (sourceHash.Length > 0 && slaCompatible && judgeOptionsUnchanged && C2SRoundTrip.Fingerprint(result) == sourceHash && savedStats.Count > 0)
            return result + "\r\n" + string.Join("\r\n", savedStats) + "\r\n";
        var outputRecords = events.Concat(notes).Select(line => string.Join("\t", P(line))).OrderBy(s => s, StringComparer.Ordinal);
        bool unchanged = FromC2S && SourceRecords.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(outputRecords);
        if (unchanged && SourceStatistics.Any(s => s.StartsWith("T_JUDGE_ALL")))
            return result + "\r\n" + string.Join("\r\n", SourceStatistics) + "\r\n";
        return result + ChartStatistics.Calculate(result);
    }
}
