using System.Globalization;
using System.Text.Json.Serialization;
using C = PenguinTools.Chart.Models.c2s;
namespace PenguinTools.Chart;

// Interoperates with the September editor release's COPYRIGHT metadata.
public sealed partial class ChartExtras
{
    private static readonly string[] EventOrder = ["BPM", "MET", "SLP", "SFL", "DCM", "STP", "CLK"];
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
        RestoreHeaders(headers);
        events = SortEvents(events);
        notes = SortNotes(notes);
        UpdateDefaultBpm(chart, headers, events);
        var snapshot = RestoreSnapshot(chart, headers, ref events, ref notes);
        var result = string.Join("\r\n", headers) + "\r\n\r\n" + string.Join("\r\n", events) + "\r\n\r\n" + string.Join("\r\n", notes) + "\r\n";
        if (snapshot.Hash.Length > 0 && snapshot.SlaCompatible && snapshot.JudgeOptionsUnchanged && C2SRoundTrip.Fingerprint(result) == snapshot.Hash && snapshot.Statistics.Count > 0)
            return result + "\r\n" + string.Join("\r\n", snapshot.Statistics) + "\r\n";
        var outputRecords = events.Concat(notes).Select(line => string.Join("\t", P(line))).OrderBy(s => s, StringComparer.Ordinal);
        bool unchanged = FromC2S && SourceRecords.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(outputRecords);
        if (unchanged && SourceStatistics.Any(s => s.StartsWith("T_JUDGE_ALL")))
            return result + "\r\n" + string.Join("\r\n", SourceStatistics) + "\r\n";
        return result + ChartStatistics.Calculate(result);
    }

    private sealed record SnapshotState(string Hash, List<string> Statistics, bool SlaCompatible, bool JudgeOptionsUnchanged);

    private void RestoreHeaders(List<string> headers)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            var tag = headers[i].Split('\t')[0];
            if (tag is not ("VERSION" or "RESOLUTION" or "CLK_DEF" or "MUSIC" or "SEQUENCEID" or "DIFFICULT" or "LEVEL") && Headers.TryGetValue(tag, out var saved))
                headers[i] = saved;
        }
        if (FromC2S)
        {
            headers.RemoveAll(s => s.StartsWith("//MGR_CLICK_V1"));

            foreach (var h in Headers.Where(h => !headers.Any(s => s.Split('\t')[0] == h.Key)))
                headers.Add(h.Value);
        }
    }

    private static int I(string[] p, int n) => int.Parse(p[n], CultureInfo.InvariantCulture);
    private static string[] P(string s) => C2SRoundTrip.Parts(s);
    private static int Rank(string tag) => Array.IndexOf(EventOrder, tag);
    private static List<string> SortEvents(List<string> events) => events.Select((s, i) => (s, i, p: P(s))).OrderBy(v => Rank(v.p[0])).ThenBy(v => I(v.p, 1)).ThenBy(v => I(v.p, 2)).ThenBy(v => v.i).Select(v => v.s).ToList();
    private static List<string> SortNotes(List<string> notes) => notes.Select((s, i) => (s, i, p: P(s))).OrderBy(v => I(v.p, 1)).ThenBy(v => I(v.p, 2)).ThenBy(v => I(v.p, 3)).ThenBy(v => I(v.p, 4)).ThenBy(v => v.i).Select(v => v.s).ToList();

    private void UpdateDefaultBpm(C.Chart chart, List<string> headers, List<string> events)
    {
        headers.RemoveAll(s => s.StartsWith("//MGR_CLICK_V1"));
        var bpms = events.Select(P).Where(p => p[0] == "BPM").Select(p => decimal.Parse(p[3], CultureInfo.InvariantCulture)).ToArray();
        if (bpms.Length > 0 && !FromC2S)
        {
            int h = headers.FindIndex(s => s.StartsWith("BPM_DEF\t"));
            decimal main = chart.Meta.MainBpm > 0 ? chart.Meta.MainBpm : bpms[0];
            headers[h] = $"BPM_DEF\t{bpms[0]:F3}\t{main:F3}\t{bpms.Max():F3}\t{bpms.Min():F3}";
        }
    }

    private SnapshotState RestoreSnapshot(C.Chart chart, List<string> headers, ref List<string> events, ref List<string> notes)
    {
        if (SourceSnapshot.Length == 0)
            return new("", [], true, true);
        var snapshot = SourceSnapshot.Split('\n').Select(P).Where(p => p.Length > 0).ToArray();
        var sourceHash = snapshot.First(p => p[0] == "HASH")[1];
        int sourceRes = int.Parse(snapshot.First(p => p[0] == "RESOLUTION")[1]);
        var judgeOptionsUnchanged = (snapshot.FirstOrDefault(p => p[0] == "TUTORIAL")?.ElementAtOrDefault(1) == "1") == Tutorial;
        RestoreJudgeHeaders(snapshot, headers);
        var usedExceptions = new HashSet<int>();
        RestoreColors(snapshot, sourceRes, notes, usedExceptions);
        RestoreSlideExceptions(snapshot, sourceRes, notes, usedExceptions);
        events = RestoreEvents(snapshot, sourceRes, events);
        var regions = snapshot.Where(p => p[0] == "SLA").ToArray();
        bool slaCompatible = MatchesSlaTimelines(chart, regions, sourceRes) || MatchesSlaRegions(chart, regions, sourceRes);
        if (slaCompatible)
            notes = RestoreSlaRegions(notes, regions, sourceRes);
        notes = RestoreNoteOrder(notes, snapshot, sourceRes);
        var savedStats = snapshot.Where(p => p[0].StartsWith("T_")).Select(p => string.Join("\t", p)).ToList();
        return new(sourceHash, savedStats, slaCompatible, judgeOptionsUnchanged);
    }

    private void RestoreJudgeHeaders(string[][] snapshot, List<string> headers)
    {
        foreach (var p in snapshot.Where(p => p[0] is "PROGJUDGE_BPM" or "PROGJUDGE_AER" || p[0] == "MET_DEF" && UnchangedEventKinds.Contains("BEAT")))
        {
            int h = headers.FindIndex(s => s.StartsWith(p[0] + "\t"));
            if (h >= 0)
                headers[h] = string.Join("\t", p);
        }
    }

    private void RestoreColors(string[][] snapshot, int sourceRes, List<string> notes, HashSet<int> usedExceptions)
    {
        foreach (var entry in snapshot.Where(p => p[0] == "COLOR"))
        {
            var original = entry.Skip(1).ToArray();
            var expected = (string[])original.Clone();
            expected[^1] = "DEF";
            string key = C2SRoundTrip.Normalize(string.Join("\t", expected), sourceRes);
            RestoreException(notes, usedExceptions, original, key, sourceRes, original[0] is "ASC" or "ASD" ? 7 : -1);
        }
    }

    private void RestoreSlideExceptions(string[][] snapshot, int sourceRes, List<string> notes, HashSet<int> usedExceptions)
    {
        foreach (var exception in snapshot.Where(p => p[0] == "EXCEPT"))
        {
            var original = exception.Skip(2).ToArray();
            var expected = original.ToList();
            expected[0] = expected[0].EndsWith('D') ? "SXD" : "SXC";
            if (expected.Count == 7)
                expected.Add(expected[4]);
            if (expected.Count == 8)
                expected.Add("SLD");
            if (expected.Count == 9)
                expected.Add(exception[1]);
            else
                expected[9] = exception[1];
            string key = C2SRoundTrip.Normalize(string.Join("\t", expected), sourceRes);
            RestoreException(notes, usedExceptions, original, key, sourceRes, 5);
        }
    }

    private void RestoreException(List<string> notes, HashSet<int> usedExceptions, string[] original, string key, int sourceRes, int durationIndex)
    {
        for (int i = 0; i < notes.Count; i++)
        {
            if (usedExceptions.Contains(i) || C2SRoundTrip.Normalize(notes[i], Resolution) != key)
                continue;
            ScaleField(original, 2, sourceRes);
            if (durationIndex >= 0)
                ScaleField(original, durationIndex, sourceRes);
            notes[i] = string.Join("\t", original);
            usedExceptions.Add(i);
            return;
        }
    }

    private void ScaleField(string[] fields, int index, int sourceRes) => fields[index] = ((decimal)I(fields, index) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);

    private static string Kind(string tag) => tag switch
    {
        "MET" => "BEAT",
        "BPM" => "BPM",
        "DCM" => "SPDMOD",
        "SLP" or "SFL" or "STP" => "TIL",
        _ => ""
    };

    private List<string> RestoreEvents(string[][] snapshot, int sourceRes, List<string> events)
    {
        events.RemoveAll(s => UnchangedEventKinds.Contains(Kind(P(s)[0])));
        foreach (var original in snapshot.Where(p => UnchangedEventKinds.Contains(Kind(p[0]))))
        {
            var p = (string[])original.Clone();
            p[2] = ((decimal)I(p, 2) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
            if (p[0] is "SLP" or "SFL" or "DCM" or "STP")
                p[3] = ((decimal)I(p, 3) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
            events.Add(string.Join("\t", p));
        }
        return SortEvents(events);
    }

    private static bool MatchesSlaTimelines(C.Chart chart, string[][] regions, int sourceRes)
    {
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
                return false;
            }
        }
        return true;
    }

    private static bool MatchesSlaRegions(C.Chart chart, string[][] regions, int sourceRes)
    {
        // Native saves may reorder events without changing the emitted SLA regions.
        return regions.Select(p => (
                Tick: (decimal)I(p, 1) * 1920 + (decimal)I(p, 2) * 1920 / sourceRes,
                Lane: I(p, 3), Width: I(p, 4), Length: (decimal)I(p, 5) * 1920 / sourceRes,
                Timeline: I(p, 6))).Order().SequenceEqual(
            chart.Notes.OfType<C.Sla>().Select(n => (
                Tick: (decimal)n.Tick.Original, n.Lane, n.Width,
                Length: (decimal)n.Length.Original, n.Timeline)).Order());
    }

    private List<string> RestoreSlaRegions(List<string> notes, string[][] regions, int sourceRes)
    {
        notes.RemoveAll(s => s.StartsWith("SLA\t"));
        foreach (var p in regions)
        {
            p[2] = ((decimal)I(p, 2) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
            p[5] = ((decimal)I(p, 5) * Resolution / sourceRes).ToString(CultureInfo.InvariantCulture);
            notes.Add(string.Join("\t", p));
        }
        return SortNotes(notes);
    }

    private List<string> RestoreNoteOrder(List<string> notes, string[][] snapshot, int sourceRes)
    {
        var sourceOrder = new Dictionary<string, Queue<int>>();
        int sourceIndex = 0;
        foreach (var p in snapshot.Where(p => p[0] == "ORDER"))
        {
            string key = C2SRoundTrip.Normalize(string.Join("\t", p.Skip(1)), sourceRes);
            if (!sourceOrder.TryGetValue(key, out var queue))
                sourceOrder[key] = queue = new Queue<int>();
            queue.Enqueue(sourceIndex++);
        }
        return notes.Select((s, i) =>
        {
            string key = C2SRoundTrip.Normalize(s, Resolution);
            int order = sourceOrder.TryGetValue(key, out var q) && q.Count > 0 ? q.Dequeue() : sourceIndex + i;
            return (s, i, order, p: P(s));
        }).OrderBy(v => I(v.p, 1)).ThenBy(v => I(v.p, 2)).ThenBy(v => I(v.p, 3)).ThenBy(v => I(v.p, 4)).ThenBy(v => v.order).ThenBy(v => v.i).Select(v => v.s).ToList();
    }
}
