using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PenguinTools.Core.Diagnostic;
using C2sModel = PenguinTools.Chart.Models.c2s;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

/// <summary>Editor extensions that have no equivalent in the basic note models.</summary>
public sealed partial class ChartExtras
{
    public int EditorFormatVersion { get; set; }
    public bool SegmentSlideLines { get; set; }
    public List<CurvePathProfile> CurveProfiles { get; set; } = [];
    public List<LockedCurveProfile> LockedCurves { get; set; } = [];
    public List<EditorNoteProjection> EditorNoteProjections { get; set; } = [];
    public string? EditorAirColorKey { get; set; }
    public List<EditorBeatProjection> EditorBeatProjections { get; set; } = [];
    public bool HasEditorBeatProjection { get; set; }
    public List<EditorPathProjection> EditorPathProjections { get; set; } = [];
    public List<EditorNumberProjection> EditorNumberProjections { get; set; } = [];
    public bool? OriginalBlankMeasure { get; set; }

    internal ChartExtras Copy()
    {
        var copy = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(this,
            ChartExtrasJsonContext.Default.ChartExtras), ChartExtrasJsonContext.Default.ChartExtras)!;
        copy.SourceSnapshot = SourceSnapshot;
        copy.FromC2S = FromC2S;
        copy.Resolution = Resolution;
        copy.SourceRecords.AddRange(SourceRecords);
        copy.UnchangedEventKinds.UnionWith(UnchangedEventKinds);
        copy.ParsedEventModelKey = ParsedEventModelKey;
        copy.AirModelKey = AirModelKey;
        copy.SlaModelKey = SlaModelKey;
        copy.BinarySnapshotValid = BinarySnapshotValid;
        copy.SpeedModelKey = SpeedModelKey;
        copy.InteropSourceText = InteropSourceText;
        return copy;
    }
    [GeneratedRegex(@"(?:^|;)(PT_C2S_(?:V1|LZ1)=[A-Za-z0-9+/=]+;)", RegexOptions.None, 1000)]
    private static partial Regex CopyrightMetadataRegex();

    internal const string BookmarkPrefix = "PT_EXTRAS_V2:";
    public int? ClickCount { get; set; }
    public int? SourceResolution { get; set; }
    public string? SourceMetaKey { get; set; }
    public decimal? SourceClockTicks { get; set; }
    public int? SourceClickCount { get; set; }
    public bool SourceClickEnabled { get; set; }
    public string? SourceClockMeterKey { get; set; }
    public bool ClickEnabled { get; set; } = true;
    public List<int> ClickTicks { get; set; } = [];

    internal ChartExtras WithClickOffset(int offset)
    {
        var result = (ChartExtras)MemberwiseClone();
        result.ClickTicks = ClickTicks.Select(tick => checked(tick + offset)).ToList();
        return result;
    }

    public string EditorBackgroundMode { get; set; } = "PASSIVE";
    public string EditorFieldColorName { get; set; } = string.Empty;
    public List<string> EditorHeaders { get; set; } = [];
    public List<string> EditorComments { get; set; } = [];
    public List<EditorBookmark> EditorBookmarks { get; set; } = [];
    public List<int> EditorBreakingMarkers { get; set; } = [];
    public List<byte[]> EditorImageEvents { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];
    public List<string> SourceStatistics { get; set; } = [];
    public string? SourceKey { get; set; }
    // Only populated when a source's declared, internally consistent judge
    // summary cannot be reproduced by the current generic judge calculator.
    // The writer may reuse it only after proving the entire gameplay payload
    // unchanged, and then restores the original ordered statements as well.
    public string? UnchangedJudgeSource { get; set; }
    public bool C2sCoordinateOrigin { get; set; }
    public const string FixedZeroOriginMarker = "MGR_ORIGIN=ZERO_V1";
    public bool FixedZeroOrigin { get; set; }
    public const string PrerollBar1Marker = "MGR_PREROLL=BAR1_V1";
    internal const string PendingSnapshotShiftPrefix = "MGR_PREROLL_SNAPSHOT_SHIFT=";
    public bool PrerollBar1Applied { get; set; }
    [JsonIgnore]
    internal bool UsesFixedCoordinateOrigin => FixedZeroOrigin || PrerollBar1Applied || C2sCoordinateOrigin ||
        SourceKey is not null || SourceSnapshot.Length > 0;

    internal void ApplyCoordinateOrigin(PenguinTools.Core.Metadata.Meta meta) =>
        meta.BgmFixedZeroOrigin = UsesFixedCoordinateOrigin;

    internal static bool HasFixedZeroOrigin(string? value) =>
        HasTimingMarker(value, "MGR_ORIGIN=", FixedZeroOriginMarker);

    internal static bool HasPrerollBar1(string? value) =>
        HasTimingMarker(value, "MGR_PREROLL=", PrerollBar1Marker);

    private static bool HasTimingMarker(string? value, string prefix, string marker)
    {
        var tokens = (value ?? "").Split(';').Select(token => token.Trim()).ToArray();
        var unknown = tokens.FirstOrDefault(token => token.StartsWith(prefix, StringComparison.Ordinal) &&
            token != marker);
        if (unknown is not null)
        {
            throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header, unknown, marker));
        }
        return tokens.Contains(marker, StringComparer.Ordinal);
    }

    internal static void RejectPendingSnapshotShift(string? value)
    {
        var token = (value ?? "").Split(';').Select(t => t.Trim())
            .FirstOrDefault(t => t.StartsWith(PendingSnapshotShiftPrefix, StringComparison.Ordinal));
        if (token is not null)
        {
            throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header,
                token, "chart without pending snapshot translation"));
        }
    }
    public List<EditorNoteSnapshot> MgxcNoteSnapshots { get; set; } = [];
    public int UgcSnapshotVersion { get; set; }
    public string? UgcContextKey { get; set; }
    public string? UgcMusicalKey { get; set; }
    public string? UgcSlaKey { get; set; }
    public List<EditorNoteSnapshot> UgcNoteSnapshots { get; set; } = [];
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

    internal static string CopyrightText(string value)
    {
        var text = Regex.Replace(value,
            @"(?:MGR|PT)_C2S_(?:V1|LZ1)=[A-Za-z0-9+/=]+;",
            string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return string.Join(';', text.Split(';').Where(token =>
            token.Trim() != FixedZeroOriginMarker && token.Trim() != PrerollBar1Marker &&
            !token.Trim().StartsWith("MGR_CLKCNT=", StringComparison.Ordinal)));
    }

    internal string CopyrightWithTiming(string value)
    {
        if (!FixedZeroOrigin && !PrerollBar1Applied)
        {
            return value;
        }

        var originMarker = FixedZeroOrigin ? FixedZeroOriginMarker + ";" : "";
        var prerollMarker = PrerollBar1Applied ? PrerollBar1Marker + ";" : "";
        return originMarker + prerollMarker + CopyrightText(value);
    }

    internal string RestoreSourceOrder(string text, int resolution)
    {
        if (SourceSnapshot.Length == 0)
        {
            return text;
        }

        var saved = SourceSnapshot.Split('\n').Select(C2SRoundTrip.Parts).ToArray();
        var sourceResolution = int.Parse(saved.FirstOrDefault(p => p.Length > 1 && p[0] == "RESOLUTION")?[1] ?? "384", CultureInfo.InvariantCulture);
        var lines = text.Replace("\r", "").Split('\n');
        var indices = Enumerable.Range(0, lines.Length).Where(i => NoteTags.Contains(C2SRoundTrip.Parts(lines[i]).FirstOrDefault() ?? "")).ToArray();
        var ordered = C2SRoundTrip.RestoreNoteOrder(indices.Select(i => lines[i]),
            saved.Where(p => p.Length > 1 && p[0] == "ORDER").Select(p => string.Join('\t', p.Skip(1))), resolution, sourceResolution);
        for (var i = 0; i < indices.Length; i++)
        {
            lines[indices[i]] = ordered[i];
        }

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

    internal static string EventView(UmgrModel.Chart chart) => string.Join("\n", chart.Events.Children.Select(e => e switch
    {
        UmgrModel.BeatEvent b => $"BEAT\t{b.Bar}\t{b.Numerator}\t{b.Denominator}",
        UmgrModel.BpmEvent b => string.Create(CultureInfo.InvariantCulture, $"BPM\t{b.Tick.Original}\t{b.Bpm}"),
        UmgrModel.ScrollSpeedEvent s => string.Create(CultureInfo.InvariantCulture, $"TIL\t{s.Timeline}\t{s.Tick.Original}\t{s.Speed}"),
        UmgrModel.NoteSpeedEvent s => string.Create(CultureInfo.InvariantCulture, $"SPDMOD\t{s.Tick.Original}\t{s.Speed}"),
        _ => ""
    }).Where(s => s.Length > 0));

    public void ReadCopyright(string? value)
    {
        ClickCount = MargreteInput.ClickCount(value) ?? ClickCount;
        RejectPendingSnapshotShift(value);
        FixedZeroOrigin |= HasFixedZeroOrigin(value);
        PrerollBar1Applied |= HasPrerollBar1(value);
        SourceSnapshot = C2SRoundTrip.Decode(value ?? "");
    }

    public int Count(int initial) => Math.Max(0, ClickCount ?? initial);

    public decimal ClockTicks(C2sModel.Chart chart)
    {
        if (SourceClockTicks.HasValue && ClickCount == SourceClickCount &&
            ClickEnabled == SourceClickEnabled && SourceClockMeterKey == ClockMeterKey(chart))
        {
            return SourceClockTicks.Value;
        }

        if (!ClickEnabled)
        {
            return 0;
        }

        var beats = chart.Events.OfType<C2sModel.Met>().OrderBy(e => e.Tick.Original).ToArray();
        decimal tick = 0;
        var denominator = Math.Max(1, chart.Meta.BgmInitialDenominator);
        var index = 0;
        for (var i = 0; i < Count(chart.Meta.BgmInitialNumerator); i++)
        {
            while (index < beats.Length && beats[index].Tick.Original <= tick)
            {
                denominator = beats[index++].Denominator;
            }

            if (denominator <= 0)
            {
                throw new FormatException("Meter denominator must be positive.");
            }

            var next = tick + 1920m / denominator;
            if (index < beats.Length && beats[index].Tick.Original < next)
            {
                next = beats[index].Tick.Original;
            }

            tick = next;
        }
        return tick;
    }

    // Preserve precision while composing records and restoring editor metadata.
    // The C2S writer quantizes the completed document to the game's 384 ticks.
    internal int SerializationResolution(C2sModel.Chart chart)
    {
        var ticks = chart.Notes.Select(n => n.Tick.Original)
            .Concat(chart.Notes.OfType<C2sModel.LongNote>().Select(n => n.EndTick.Original))
            .Concat(chart.Notes.OfType<C2sModel.Sla>().Select(n => n.Length.Original))
            .Concat(chart.Notes.OfType<C2sModel.AirCrash>().Select(n => n.Density.Original))
            .Concat(chart.Events.Select(e => e.Tick.Original))
            .Concat(chart.Events.OfType<C2sModel.SpeedEventBase>().Select(e => e.Length.Original))
            .Concat(ClickTicks);
        if (SourceResolution is > 0 && ticks.All(t => (long)t * SourceResolution.Value % 1920 == 0) &&
            ClockTicks(chart) * SourceResolution.Value % 1920 == 0)
        {
            return SourceResolution.Value;
        }

        var fine = chart.Notes.Any(n => n.Tick.Original % 5 != 0 ||
            n is C2sModel.LongNote l && l.EndTick.Original % 5 != 0 ||
            n is C2sModel.Sla sla && sla.Length.Original % 5 != 0 ||
            n is C2sModel.AirCrash a && a.Density.Original % 5 != 0);
        fine |= chart.Events.Any(e => e.Tick.Original % 5 != 0 ||
            e is C2sModel.SpeedEventBase s && s.Length.Original % 5 != 0);
        fine |= ClickTicks.Any(t => t % 5 != 0) || ClockTicks(chart) % 5 != 0;
        return fine ? 1920 : 384;
    }

    internal void InferClickCount(C2sModel.Chart chart)
    {
        if (ClickCount.HasValue || !Headers.TryGetValue("CLK_DEF", out var line))
        {
            return;
        }

        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !decimal.TryParse(fields[1], CultureInfo.InvariantCulture, out var span))
        {
            return;
        }

        var target = span * 1920 / (SourceResolution ?? 384);
        var beats = chart.Events.OfType<C2sModel.Met>().OrderBy(e => e.Tick.Original).ToArray();
        decimal tick = 0;
        var count = 0;
        var index = 0;
        var denominator = Math.Max(1, chart.Meta.BgmInitialDenominator);
        while (tick < target)
        {
            while (index < beats.Length && beats[index].Tick.Original <= tick)
            {
                denominator = beats[index++].Denominator;
            }

            if (denominator <= 0)
            {
                throw new FormatException("Invalid metronome denominator.");
            }

            var next = tick + 1920m / denominator;
            if (index < beats.Length && beats[index].Tick.Original < next)
            {
                next = beats[index].Tick.Original;
            }

            tick = next;
            count = checked(count + 1);
        }
        ClickCount = count;
    }

    internal void CaptureClock(C2sModel.Chart chart)
    {
        if (!Headers.TryGetValue("CLK_DEF", out var line))
        {
            return;
        }

        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !decimal.TryParse(fields[1], CultureInfo.InvariantCulture, out var span))
        {
            return;
        }

        SourceClockTicks = span * 1920 / (SourceResolution ?? 384);
        SourceClickCount = ClickCount;
        SourceClickEnabled = ClickEnabled;
        SourceClockMeterKey = ClockMeterKey(chart);
    }

    private static string ClockMeterKey(C2sModel.Chart chart) => string.Join(";",
        chart.Events.OfType<C2sModel.Met>().OrderBy(e => e.Tick.Original)
            .Select(e => $"{e.Tick.Original},{e.Numerator},{e.Denominator}"));

    internal static string MetaKey(C2sModel.Chart chart) => string.Create(CultureInfo.InvariantCulture,
        $"{chart.Meta.Id}|{chart.Meta.Difficulty}|{chart.Meta.Level:G29}|{chart.Meta.Designer}|{chart.Meta.MainBpm:G29}|{chart.Meta.BgmInitialNumerator}|{chart.Meta.BgmInitialDenominator}");

    public string ToBookmark() => BookmarkPrefix + ToCopyright() +
        (SourceSnapshot.Length > 0 ? C2SRoundTrip.Pack(SourceSnapshot, true) : "");

    internal static bool IsExtrasBookmark(string tag) =>
        tag.StartsWith(BookmarkPrefix, StringComparison.Ordinal);

    public static ChartExtras FromBookmark(string tag)
    {
        if (tag.StartsWith(BookmarkPrefix, StringComparison.Ordinal))
        {
            return FromCopyright(tag[BookmarkPrefix.Length..]);
        }

        throw new FormatException("Invalid chart metadata bookmark.");
    }

    internal static string BeatKey(UmgrModel.Chart chart) => string.Join(";",
        chart.Events.Children.OfType<UmgrModel.BeatEvent>().OrderBy(e => e.Bar)
            .Select(e => $"{e.Bar},{e.Numerator},{e.Denominator}"));

    internal static string AirKey(UmgrModel.Note note) =>
        string.Create(CultureInfo.InvariantCulture, $"{note.GetType().Name},{note.Tick.Original},{note.Lane},{note.Width},{note.GetLastTick()}");

    internal static string SlideKey(UmgrModel.Slide slide) =>
        $"{slide.Tick.Original},{slide.Lane},{slide.Width},{slide.Effect}:" +
        string.Join(";", slide.Children.OfType<UmgrModel.SlideJoint>()
            .Select(n => $"{n.Tick.Original},{n.Lane},{n.Width}" + (n.IsCurveControl ? ",BEZIER" : "")));

    internal void CaptureSlideEffects(UmgrModel.Chart chart)
    {
        TraceCrashes = chart.Notes.Children.OfType<UmgrModel.AirCrash>()
            .GroupBy(CrashKey)
            .SelectMany(group => group.Select((crash, occurrence) => (crash, occurrence))
                .Where(item => item.crash.Attr == Models.AirLadderAttr.Trace)
                .Select(item => CrashOccurrenceKey(group.Key, item.occurrence))).ToList();
        HoldEffects = chart.Notes.Children.OfType<UmgrModel.ExTapableNote>()
            .Select(h => new HoldEffectSnapshot(AirKey(h), h.Effect)).ToList();
        SlideEffects.Clear();
        var occurrences = new Dictionary<string, int>();
        foreach (var slide in chart.Notes.Children.OfType<UmgrModel.Slide>())
        {
            var key = SlideKey(slide);
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            var points = slide.Children.OfType<UmgrModel.SlideJoint>().ToArray();
            for (var i = 0; i < points.Length; i++)
            {
                if (points[i].HasEffectOverride)
                {
                    SlideEffects.Add(new SlideEffectSnapshot(key, i, points[i].SegmentEffect) { Occurrence = occurrence });
                }
            }
        }
    }

    internal void RestoreAppearance(UmgrModel.Chart chart, IReadOnlySet<UmgrModel.Note>? eligible = null)
    {
        RestoreCrashAttributes(chart, eligible);
        RestoreGroundEffects(chart, eligible);
        var appearances = AirAppearances.GroupBy(a => a.Key)
            .ToDictionary(g => g.Key, g => new Queue<AirAppearance>(g));
        foreach (var note in chart.Notes.Children)
        {
            if (!appearances.TryGetValue(AirKey(note), out var queue) || !queue.TryDequeue(out var saved))
            {
                continue;
            }
            if (eligible is not null && !eligible.Contains(note))
            {
                continue;
            }

            if (note is UmgrModel.AirSlide slide)
            {
                slide.Color = saved.ArrowColor ?? saved.Color;
                slide.Direction = saved.Direction;
            }
            if (note is UmgrModel.AirHold hold)
            {
                hold.Color = saved.Color;
                hold.Direction = saved.Direction;
            }
        }
    }

    internal void RestoreCrashAttributes(UmgrModel.Chart chart, IReadOnlySet<UmgrModel.Note>? eligible = null)
    {
        var saved = TraceCrashes.ToHashSet(StringComparer.Ordinal);
        foreach (var group in chart.Notes.Children.OfType<UmgrModel.AirCrash>().GroupBy(CrashKey))
        {
            foreach (var (crash, occurrence) in group.Select((crash, occurrence) => (crash, occurrence)))
            {
                if ((eligible is null || eligible.Contains(crash)) &&
                    (saved.Contains(CrashOccurrenceKey(group.Key, occurrence)) || saved.Contains(group.Key)))
                {
                    crash.Attr = Models.AirLadderAttr.Trace;
                }
            }
        }
    }

    private static string CrashOccurrenceKey(string key, int occurrence) =>
        string.Create(CultureInfo.InvariantCulture, $"{key}#{occurrence}");

    private void RestoreGroundEffects(UmgrModel.Chart chart, IReadOnlySet<UmgrModel.Note>? eligible)
    {
        var holds = HoldEffects.GroupBy(h => h.Key).ToDictionary(g => g.Key, g => new Queue<HoldEffectSnapshot>(g));
        foreach (var hold in chart.Notes.Children.OfType<UmgrModel.ExTapableNote>())
        {
            if (!holds.TryGetValue(AirKey(hold), out var savedHolds) || !savedHolds.TryDequeue(out var saved))
            {
                continue;
            }
            if (eligible is not null && !eligible.Contains(hold))
            {
                continue;
            }

            hold.Effect = saved.Effect;
        }

        var occurrences = new Dictionary<string, int>();
        foreach (var slide in chart.Notes.Children.OfType<UmgrModel.Slide>())
        {
            var key = SlideKey(slide);
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            if (eligible is not null && !eligible.Contains(slide))
            {
                continue;
            }

            RestoreSlideEffects(slide, key, occurrence);
        }
    }

    private void RestoreSlideEffects(UmgrModel.Slide slide, string key, int occurrence)
    {
        var points = slide.Children.OfType<UmgrModel.SlideJoint>().ToArray();
        foreach (var saved in SlideEffects.Where(s => s.Key == key && (s.Occurrence is null || s.Occurrence == occurrence)))
        {
            if (saved.Index >= 0 && saved.Index < points.Length)
            {
                points[saved.Index].HasEffectOverride = true;
                points[saved.Index].SegmentEffect = saved.Effect;
            }
        }
    }

    internal static string CrashKey(UmgrModel.AirCrash crash) =>
        string.Create(CultureInfo.InvariantCulture, $"{crash.Tick.Original},{crash.Lane},{crash.Width},{crash.Height},{crash.GetLastTick()}");

    internal static string SpeedKey(UmgrModel.Chart chart) => string.Join(";",
        chart.Events.Children.OfType<UmgrModel.SpeedEventBase>()
            .OrderBy(e => e.GetType().Name, StringComparer.Ordinal)
            .ThenBy(e => e is UmgrModel.ScrollSpeedEvent s ? s.Timeline : 0)
            .ThenBy(e => e.Tick)
            .Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.GetType().Name},{e.Tick.Original},{(e is UmgrModel.ScrollSpeedEvent s ? s.Timeline : 0)},{e.Speed}")));
}

public sealed record SlideEffectSnapshot(string Key, int Index, PenguinTools.Chart.Models.ExEffect? Effect)
{
    public int? Occurrence { get; init; }
}
public sealed record HoldEffectSnapshot(string Key, PenguinTools.Chart.Models.ExEffect? Effect);
public sealed record EditorNoteSnapshot(string Key, ExTapRoleSnapshot? ExTap);
public sealed record ExTapRoleSnapshot(UmgrModel.ExTapRole Role, UmgrModel.AirActionCarrierParent Parent,
    Models.Joint ParentJoint, bool ParentHasExEffect);
public sealed record AirAppearance(string Key, PenguinTools.Chart.Models.Color Color, PenguinTools.Chart.Models.AirDirection Direction)
{
    // Read older metadata that stored the controlling AIR color separately.
    // New output uses Color for the entire long note and omits this field.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PenguinTools.Chart.Models.Color? ArrowColor { get; init; }
}
public sealed record MeterSnapshot(int Tick, int Numerator, int Denominator);
public sealed record SpeedSnapshot(string Tag, int Tick, int Length, decimal Speed, int Timeline);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChartExtras))]
internal partial class ChartExtrasJsonContext : JsonSerializerContext;
