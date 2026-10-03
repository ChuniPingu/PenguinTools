using System.Globalization;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using c2s = PenguinTools.Chart.Models.c2s;
using umgr = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

/// <summary>Editor extensions that have no equivalent in the basic note models.</summary>
public sealed partial class ChartExtras
{
    [GeneratedRegex(@"(?:^|;)(PT_C2S_(?:V1|LZ1)=[A-Za-z0-9+/=]+;)", RegexOptions.None, 1000)]
    private static partial Regex CopyrightMetadataRegex();

    [GeneratedRegex(@"(?:^|;)MGR_CLKCNT=(\d+)(?:;|$)", RegexOptions.None, 1000)]
    private static partial Regex ClickCountRegex();
    internal const string BookmarkPrefix = "PT_EXTRAS_V2:";
    private const string LegacyBookmarkPrefix = "PT_EXTRAS_V1:";
    public int? ClickCount { get; set; }
    public int? SourceResolution { get; set; }
    public string? SourceMetaKey { get; set; }
    public decimal? SourceClockTicks { get; set; }
    public int? SourceClickCount { get; set; }
    public bool SourceClickEnabled { get; set; }
    public string? SourceClockMeterKey { get; set; }
    public bool ClickEnabled { get; set; } = true;
    public List<int> ClickTicks { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];
    public List<string> SourceStatistics { get; set; } = [];
    public string? SourceKey { get; set; }
    public string? UgcContentKey { get; set; }
    [JsonIgnore] public string? ParsedEventModelKey { get; set; }
    public List<MeterSnapshot> Meters { get; set; } = [];
    public string? MeterEditKey { get; set; }
    public bool HasSpeedSnapshot { get; set; }
    public List<AirAppearance> AirAppearances { get; set; } = [];
    public List<SlideEffectSnapshot> SlideEffects { get; set; } = [];
    public List<HoldEffectSnapshot> HoldEffects { get; set; } = [];
    [JsonIgnore] public string? AirModelKey { get; set; }
    [JsonIgnore] public string? SlaModelKey { get; set; }
    public List<string> TraceCrashes { get; set; } = [];
    public List<SpeedSnapshot> Speeds { get; set; } = [];
    public string? BinaryContentKey { get; set; }
    [JsonIgnore]
    public bool BinarySnapshotValid { get; set; }
    [JsonIgnore]
    public string? SpeedModelKey { get; set; }

    public static readonly FrozenSet<string> NoteTags =
        "TAP CHR FLK MNE HLD HXD SLC SLD SXC SXD SLA AIR AUL AUR ADW ADL ADR ASC ASD AHD AHX ASX ALD".Split(' ').ToFrozenSet();

    public static readonly FrozenSet<string> EventTags = "BPM MET SLP SFL DCM STP CLK".Split(' ').ToFrozenSet();
    [JsonIgnore] public string? InteropSourceText { get; set; }

    public string ToCopyright()
    {
        var json = JsonSerializer.Serialize(this, ChartExtrasJsonContext.Default.ChartExtras);
        return C2SRoundTrip.Pack(json, true).Replace("MGR_C2S_", "PT_C2S_", StringComparison.Ordinal);
    }

    internal string RestoreSourceOrder(string text, int resolution)
    {
        if (SourceSnapshot.Length == 0) return text;
        var saved = SourceSnapshot.Split('\n').Select(C2SRoundTrip.Parts).ToArray();
        var sourceResolution = int.Parse(saved.First(p => p.Length > 1 && p[0] == "RESOLUTION")[1], CultureInfo.InvariantCulture);
        var positions = new Dictionary<string, Queue<int>>();
        var count = 0;
        foreach (var row in saved.Where(p => p.Length > 1 && p[0] == "ORDER"))
        {
            var key = C2SRoundTrip.Normalize(string.Join('\t', row.Skip(1)), sourceResolution);
            if (!positions.TryGetValue(key, out var queue)) positions[key] = queue = new();
            queue.Enqueue(count++);
        }
        if (count == 0) return text;
        var lines = text.Replace("\r", "").Split('\n');
        var indices = Enumerable.Range(0, lines.Length).Where(i => NoteTags.Contains(C2SRoundTrip.Parts(lines[i]).FirstOrDefault() ?? "")).ToArray();
        var ordered = indices.Select((index, stable) =>
        {
            var line = lines[index]; var fields = C2SRoundTrip.Parts(line);
            var key = C2SRoundTrip.Normalize(line, resolution);
            var order = positions.TryGetValue(key, out var queue) && queue.TryDequeue(out var value) ? value : count + stable;
            return (line, fields, order, stable);
        }).OrderBy(v => int.Parse(v.fields[1], CultureInfo.InvariantCulture))
          .ThenBy(v => int.Parse(v.fields[2], CultureInfo.InvariantCulture))
          .ThenBy(v => int.Parse(v.fields[3], CultureInfo.InvariantCulture))
          .ThenBy(v => int.Parse(v.fields[4], CultureInfo.InvariantCulture))
          .ThenBy(v => v.order).ThenBy(v => v.stable).ToArray();
        for (var i = 0; i < indices.Length; i++) lines[indices[i]] = ordered[i].line;
        return string.Join('\n', lines);
    }

    public static ChartExtras FromCopyright(string value)
    {
        var match = CopyrightMetadataRegex().Match(value);
        var result = match.Success
            ? JsonSerializer.Deserialize(C2SRoundTrip.Decode(match.Groups[1].Value.Replace("PT_C2S_", "MGR_C2S_", StringComparison.Ordinal)), ChartExtrasJsonContext.Default.ChartExtras) ?? new()
            : new ChartExtras();
        result.ReadCopyright(value);
        result.ClickTicks.Clear(); // Explicit clicks are read from the native note block.
        return result;
    }

    internal static string EventView(umgr.Chart chart) => string.Join("\n", chart.Events.Children.Select(e => e switch
    {
        umgr.BeatEvent b => $"BEAT\t{b.Bar}\t{b.Numerator}\t{b.Denominator}",
        umgr.BpmEvent b => string.Create(CultureInfo.InvariantCulture, $"BPM\t{b.Tick.Original}\t{b.Bpm}"),
        umgr.ScrollSpeedEvent s => string.Create(CultureInfo.InvariantCulture, $"TIL\t{s.Timeline}\t{s.Tick.Original}\t{s.Speed}"),
        umgr.NoteSpeedEvent s => string.Create(CultureInfo.InvariantCulture, $"SPDMOD\t{s.Tick.Original}\t{s.Speed}"),
        _ => ""
    }).Where(s => s.Length > 0));

    public void ReadCopyright(string? value)
    {
        SourceSnapshot = C2SRoundTrip.Decode(value ?? "");
        var match = ClickCountRegex().Match(value ?? "");
        if (match.Success)
            ClickCount = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    public int Count(int initial) => Math.Max(0, ClickCount ?? initial);

    public decimal ClockTicks(c2s.Chart chart)
    {
        if (SourceClockTicks.HasValue && ClickCount == SourceClickCount &&
            ClickEnabled == SourceClickEnabled && SourceClockMeterKey == ClockMeterKey(chart))
            return SourceClockTicks.Value;
        if (!ClickEnabled)
            return 0;
        var beats = chart.Events.OfType<c2s.Met>().OrderBy(e => e.Tick.Original).ToArray();
        decimal tick = 0;
        var denominator = Math.Max(1, chart.Meta.BgmInitialDenominator);
        var index = 0;
        for (var i = 0; i < Count(chart.Meta.BgmInitialNumerator); i++)
        {
            while (index < beats.Length && beats[index].Tick.Original <= tick)
                denominator = beats[index++].Denominator;
            if (denominator <= 0)
                throw new FormatException("Meter denominator must be positive.");
            var next = tick + 1920m / denominator;
            if (index < beats.Length && beats[index].Tick.Original < next)
                next = beats[index].Tick.Original;
            tick = next;
        }
        return tick;
    }

    // Preserve precision while composing records and restoring editor metadata.
    // The C2S writer quantizes the completed document to the game's 384 ticks.
    internal int SerializationResolution(c2s.Chart chart)
    {
        var ticks = chart.Notes.Select(n => n.Tick.Original)
            .Concat(chart.Notes.OfType<c2s.LongNote>().Select(n => n.EndTick.Original))
            .Concat(chart.Notes.OfType<c2s.Sla>().Select(n => n.Length.Original))
            .Concat(chart.Notes.OfType<c2s.AirCrash>().Select(n => n.Density.Original))
            .Concat(chart.Events.Select(e => e.Tick.Original))
            .Concat(chart.Events.OfType<c2s.SpeedEventBase>().Select(e => e.Length.Original))
            .Concat(ClickTicks);
        if (SourceResolution is > 0 && ticks.All(t => (long)t * SourceResolution.Value % 1920 == 0) &&
            ClockTicks(chart) * SourceResolution.Value % 1920 == 0)
            return SourceResolution.Value;
        var fine = chart.Notes.Any(n => n.Tick.Original % 5 != 0 ||
            n is c2s.LongNote l && l.EndTick.Original % 5 != 0 ||
            n is c2s.Sla sla && sla.Length.Original % 5 != 0 ||
            n is c2s.AirCrash a && a.Density.Original % 5 != 0);
        fine |= chart.Events.Any(e => e.Tick.Original % 5 != 0 ||
            e is c2s.SpeedEventBase s && s.Length.Original % 5 != 0);
        fine |= ClickTicks.Any(t => t % 5 != 0) || ClockTicks(chart) % 5 != 0;
        return fine ? 1920 : 384;
    }

    internal void InferClickCount(c2s.Chart chart)
    {
        if (ClickCount.HasValue || !Headers.TryGetValue("CLK_DEF", out var line))
            return;
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !decimal.TryParse(fields[1], CultureInfo.InvariantCulture, out var span))
            return;
        var target = span * 1920 / (SourceResolution ?? 384);
        var beats = chart.Events.OfType<c2s.Met>().OrderBy(e => e.Tick.Original).ToArray();
        decimal tick = 0;
        var count = 0;
        var index = 0;
        var denominator = Math.Max(1, chart.Meta.BgmInitialDenominator);
        while (tick < target)
        {
            while (index < beats.Length && beats[index].Tick.Original <= tick)
                denominator = beats[index++].Denominator;
            if (denominator <= 0)
                throw new FormatException("Invalid metronome denominator.");
            var next = tick + 1920m / denominator;
            if (index < beats.Length && beats[index].Tick.Original < next)
                next = beats[index].Tick.Original;
            tick = next;
            count = checked(count + 1);
        }
        ClickCount = count;
    }

    internal void CaptureClock(c2s.Chart chart)
    {
        if (!Headers.TryGetValue("CLK_DEF", out var line))
            return;
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !decimal.TryParse(fields[1], CultureInfo.InvariantCulture, out var span))
            return;
        SourceClockTicks = span * 1920 / (SourceResolution ?? 384);
        SourceClickCount = ClickCount;
        SourceClickEnabled = ClickEnabled;
        SourceClockMeterKey = ClockMeterKey(chart);
    }

    private static string ClockMeterKey(c2s.Chart chart) => string.Join(";",
        chart.Events.OfType<c2s.Met>().OrderBy(e => e.Tick.Original)
            .Select(e => $"{e.Tick.Original},{e.Numerator},{e.Denominator}"));

    internal static string MetaKey(c2s.Chart chart) => string.Create(CultureInfo.InvariantCulture,
        $"{chart.Meta.Id}|{chart.Meta.Difficulty}|{chart.Meta.Level:G29}|{chart.Meta.Designer}|{chart.Meta.MainBpm:G29}|{chart.Meta.BgmInitialNumerator}|{chart.Meta.BgmInitialDenominator}");

    public string ToBookmark() => BookmarkPrefix + ToCopyright() +
        (SourceSnapshot.Length > 0 ? C2SRoundTrip.Pack(SourceSnapshot, true) : "");

    internal static bool IsExtrasBookmark(string tag) =>
        tag.StartsWith(BookmarkPrefix, StringComparison.Ordinal) ||
        tag.StartsWith(LegacyBookmarkPrefix, StringComparison.Ordinal);

    public static ChartExtras FromBookmark(string tag)
    {
        if (tag.StartsWith(BookmarkPrefix, StringComparison.Ordinal))
            return FromCopyright(tag[BookmarkPrefix.Length..]);
        var extras = JsonSerializer.Deserialize(Convert.FromBase64String(tag[LegacyBookmarkPrefix.Length..]),
            ChartExtrasJsonContext.Default.ChartExtras) ?? throw new FormatException("Empty chart extension.");
        extras.ClickTicks.Clear(); // Explicit clicks are read from the native note block.
        return extras;
    }

    internal static string BeatKey(umgr.Chart chart) => string.Join(";",
        chart.Events.Children.OfType<umgr.BeatEvent>().OrderBy(e => e.Bar)
            .Select(e => $"{e.Bar},{e.Numerator},{e.Denominator}"));

    internal static string AirKey(umgr.Note note) =>
        string.Create(CultureInfo.InvariantCulture, $"{note.GetType().Name},{note.Tick.Original},{note.Lane},{note.Width},{note.GetLastTick()}");

    internal static string SlideKey(umgr.Slide slide) =>
        $"{slide.Tick.Original},{slide.Lane},{slide.Width},{slide.Effect}:" +
        string.Join(";", slide.Children.OfType<umgr.SlideJoint>()
            .Select(n => $"{n.Tick.Original},{n.Lane},{n.Width}"));

    internal void CaptureSlideEffects(umgr.Chart chart)
    {
        HoldEffects = chart.Notes.Children.OfType<umgr.ExTapableNote>()
            .Select(h => new HoldEffectSnapshot(AirKey(h), h.Effect)).ToList();
        SlideEffects.Clear();
        foreach (var slide in chart.Notes.Children.OfType<umgr.Slide>())
        {
            var points = slide.Children.OfType<umgr.SlideJoint>().ToArray();
            for (var i = 0; i < points.Length; i++)
                if (points[i].HasEffectOverride)
                    SlideEffects.Add(new SlideEffectSnapshot(SlideKey(slide), i, points[i].SegmentEffect));
        }
    }

    internal void RestoreAppearance(umgr.Chart chart)
    {
        RestoreGroundEffects(chart);
        var appearances = AirAppearances.GroupBy(a => a.Key)
            .ToDictionary(g => g.Key, g => new Queue<AirAppearance>(g));
        foreach (var note in chart.Notes.Children)
        {
            if (!appearances.TryGetValue(AirKey(note), out var queue) || !queue.TryDequeue(out var saved))
                continue;
            if (note is umgr.AirSlide slide)
            {
                slide.Color = saved.Color;
                slide.Direction = saved.Direction;
            }
            if (note is umgr.AirHold hold)
            {
                hold.Color = saved.Color;
                hold.Direction = saved.Direction;
            }
        }
    }

    private void RestoreGroundEffects(umgr.Chart chart)
    {
        var holds = HoldEffects.GroupBy(h => h.Key).ToDictionary(g => g.Key, g => new Queue<HoldEffectSnapshot>(g));
        foreach (var hold in chart.Notes.Children.OfType<umgr.ExTapableNote>())
            if (holds.TryGetValue(AirKey(hold), out var savedHolds) && savedHolds.TryDequeue(out var saved))
                hold.Effect = saved.Effect;
        foreach (var slide in chart.Notes.Children.OfType<umgr.Slide>())
        {
            var points = slide.Children.OfType<umgr.SlideJoint>().ToArray();
            foreach (var saved in SlideEffects.Where(s => s.Key == SlideKey(slide)))
                if (saved.Index >= 0 && saved.Index < points.Length)
                {
                    points[saved.Index].HasEffectOverride = true;
                    points[saved.Index].SegmentEffect = saved.Effect;
                }
        }
    }

    internal static string CrashKey(umgr.AirCrash crash) =>
        string.Create(CultureInfo.InvariantCulture, $"{crash.Tick.Original},{crash.Lane},{crash.Width},{crash.Height},{crash.GetLastTick()}");

    internal static string SpeedKey(umgr.Chart chart) => string.Join(";",
        chart.Events.Children.OfType<umgr.SpeedEventBase>()
            .Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.GetType().Name},{e.Tick.Original},{(e is umgr.ScrollSpeedEvent s ? s.Timeline : 0)},{e.Speed}"))
            .Order(StringComparer.Ordinal));
}

public sealed record SlideEffectSnapshot(string Key, int Index, PenguinTools.Chart.Models.ExEffect? Effect);
public sealed record HoldEffectSnapshot(string Key, PenguinTools.Chart.Models.ExEffect? Effect);
public sealed record AirAppearance(string Key, PenguinTools.Chart.Models.Color Color, PenguinTools.Chart.Models.AirDirection Direction);
public sealed record MeterSnapshot(int Tick, int Numerator, int Denominator);
public sealed record SpeedSnapshot(string Tag, int Tick, int Length, decimal Speed, int Timeline);

[JsonSerializable(typeof(ChartExtras))]
internal partial class ChartExtrasJsonContext : JsonSerializerContext;
