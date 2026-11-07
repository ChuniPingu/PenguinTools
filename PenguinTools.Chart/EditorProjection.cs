using System.Globalization;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

public sealed record EditorNoteProjection(string Key, int Lane, int Width, decimal? Height, Color? Color, bool? NoLine)
{
    public string? AirPathKey { get; init; }
}
public sealed record EditorBeatProjection(int Bar, int Tick, int Numerator, int Denominator);
public sealed record EditorPathPoint(int Tick, int Timeline, int Lane, int Width, decimal? Height,
    Joint? Joint, bool NoLine, bool IsCurveControl, bool HasEffectOverride, ExEffect? SegmentEffect);
public sealed record EditorPathProjection(string Key, List<EditorPathPoint> Points);

/// <summary>Creates the official editor view while retaining checked round-trip information.</summary>
internal static class EditorProjection
{
    internal static U.Chart Create(U.Chart source, bool ugc, DiagnosticCollector diagnostics,
        EditorTarget target = EditorTarget.Extended)
    {
        var chart = ChartCopy.Create(source);
        NoteWidthRules.Validate(chart);
        foreach (var point in chart.Notes.Children.OfType<U.Slide>()
                     .SelectMany(slide => slide.Children.SkipLast(1)).OfType<U.SlideJoint>())
        {
            if (point.PairNote is not null)
            {
                throw new TimedDiagnosticException(Msg.Create(MsgKeys.MgCrit_Editor_interior_air,
                    ugc ? "UGC" : "MGXC"), point.Tick.Original, point);
            }
        }
        foreach (var crash in chart.Notes.Children.OfType<U.AirCrash>())
        {
            LegacyCrashMetadata.Validate(crash);
        }
        if (target != EditorTarget.Legacy)
        {
            EditorCoordinates.UseFixedOrigin(chart);
        }
        EditorTargetRules.Prepare(chart, target, ugc, diagnostics);
        var version = EditorTargetRules.Version(target, ugc);
        var format = (ugc ? "UGC v" : "MGXC v") + version;
        if (!ugc)
        {
            UgcSpeedFields.WarnIfUnsupported(chart, format, diagnostics);
        }
        PrepareSnapshots(chart);
        if (ugc)
        {
            UgcNoteOrder.Apply(chart);
        }
        MargreteCurveMetadata.CaptureProfiles(chart);
        EditorNumbers.Project(chart, diagnostics);
        chart.Extras.EditorFormatVersion = version;
        NormalizeMeters(chart, diagnostics, format);
        diagnostics.TimeCalculator = chart.GetCalculator();
        diagnostics.BackfillTimeCalculator();
        var notes = AllNotes(chart).ToArray();
        if (ugc)
        {
            foreach (var hold in notes.OfType<U.AirHold>())
            {
                var control = hold.Children.OfType<U.AirHoldJoint>().SkipLast(1)
                    .FirstOrDefault(joint => joint.Joint == Joint.C);
                if (control is not null)
                {
                    Warn(diagnostics, MsgKeys.Warn_Ugc_air_hold_controls, control.Tick.Original);
                }
            }
        }
        var paths = notes.Where(n => n.Parent == chart.Notes && n is U.Slide or U.AirSlide or U.AirCrash)
            .ToDictionary(n => n, n => n.Children.Select(CapturePoint).ToList());
        chart.Meta.ExtendedField |= notes.Any(n => !CurveExpansion.IsControl(n) && (n.Lane < 0 || n.Lane + (long)n.Width > 16));
        // Capture attributes before changing shared pair coordinates.
        var before = notes.ToDictionary(n => n, Capture);
        foreach (var note in notes)
        {
            NormalizeNote(note, ugc, chart.Meta.ExtendedField, chart.Extras.SegmentSlideLines, target);
        }
        CaptureNoteProjections(chart, before, diagnostics, format);
        if (ugc)
        {
            CapturePathProjections(chart, paths, before, diagnostics, format);
        }
        CaptureAirColorKeys(chart);
        return chart;
    }

    private static void PrepareSnapshots(U.Chart chart)
    {
        var extras = chart.Extras;
        extras.EditorNoteProjections.Clear();
        extras.EditorAirColorKey = null;
        extras.EditorBeatProjections.Clear();
        extras.HasEditorBeatProjection = false;
        extras.EditorPathProjections.Clear();
        // Check the editable source before normalization or curve expansion changes
        // its view. A newly written checksum must not validate an older snapshot.
        var airKey = C2sRoundTripKeys.FormatAirEditKey(chart);
        if (chart.Meta.C2sAirSnapshot is not null &&
            !(extras.BinarySnapshotValid && extras.AirModelKey == airKey) && chart.Meta.C2sAirEditKey != airKey)
        {
            chart.Meta.C2sAirSnapshot = null;
            chart.Meta.C2sAirEditKey = null;
        }
        var slaKey = C2sRoundTripKeys.FormatSlaEditKey(chart);
        if (chart.Meta.C2sSlaSnapshot is not null &&
            !(extras.BinarySnapshotValid && extras.SlaModelKey == slaKey) && chart.Meta.C2sSlaEditKey != slaKey)
        {
            chart.Meta.C2sSlaSnapshot = null;
            chart.Meta.C2sSlaEditKey = null;
        }
        if (extras.SpeedModelKey != ChartExtras.SpeedKey(chart))
        {
            extras.HasSpeedSnapshot = false;
        }
        extras.OriginalBlankMeasure = null;
        if (extras.UsesFixedCoordinateOrigin && chart.Meta.BgmEnableBarOffset)
        {
            extras.OriginalBlankMeasure = true;
            chart.Meta.BgmEnableBarOffset = false;
        }
    }

    private static void NormalizeNote(U.Note note, bool ugc, bool extendedField, bool segmentLines, EditorTarget target)
    {
        var pathPoint = target != EditorTarget.Legacy && NoteWidthRules.IsClippedControl(note);
        note.Width = Math.Clamp(note.Width, pathPoint ? 0 : 1, 16);
        var expanded = extendedField || CurveExpansion.IsControl(note);
        note.Lane = Math.Clamp(note.Lane, expanded ? -16 : 0, (expanded ? 32 : 16) - note.Width);
        if (GetHeight(note) is { } height)
        {
            // UGC writes sampled points, not the Bezier handles themselves.
            if (!ugc || !CurveExpansion.IsControl(note))
            {
                ValidateHeight(note, height, ugc, target);
            }
            SetHeight(note, decimal.Round(height, 0, MidpointRounding.AwayFromZero));
        }
        if (GetColor(note) is not (null or Color.DEF or Color.PNK))
        {
            SetColor(note, Color.DEF);
        }
        if (!segmentLines && note is U.Slide slide)
        {
            foreach (var joint in slide.Children.OfType<U.SlideJoint>())
            {
                joint.NoLine = slide.NoLine;
            }
        }
    }

    internal static void ValidateUgcHeights(U.Chart chart)
    {
        foreach (var note in AllNotes(chart))
        {
            if (GetHeight(note) is { } height)
            {
                ValidateHeight(note, height, ugc: true);
            }
        }
    }

    private static void ValidateHeight(U.Note note, decimal height, bool ugc, EditorTarget target = EditorTarget.Extended)
    {
        var signed = !ugc && target == EditorTarget.Extended;
        var maximum = ugc ? 1295 : signed ? int.MaxValue : ushort.MaxValue;
        if (height < (signed ? int.MinValue : 0) || height > maximum)
        {
            throw new TimedDiagnosticException(
                Msg.Create(MsgKeys.MgCrit_Unrepresentable_height,
                    (ugc ? "UGC v" : "MGXC v") + EditorTargetRules.Version(target, ugc), height, maximum,
                    signed ? int.MinValue : 0),
                note.Tick.Original, note);
        }
    }

    private static void CaptureNoteProjections(U.Chart chart, Dictionary<U.Note, EditorNoteProjection> before,
        DiagnosticCollector diagnostics, string format)
    {
        // Retain unchanged colliding notes too, so FIFO restoration cannot apply a
        // changed note's attributes to an earlier overlapping note.
        foreach (var group in AllNotes(chart).GroupBy(Key))
        {
            var snapshots = group.Select(n => (Note: n, Before: before[n] with { Key = group.Key }, After: Capture(n))).ToArray();
            if (!snapshots.Any(p => p.Before != p.After))
            {
                continue;
            }

            chart.Extras.EditorNoteProjections.AddRange(snapshots.Select(p => p.Before));
            // Native integral height rounding and line flags are retained in metadata.
            foreach (var p in snapshots.Where(p => (p.Before with { NoLine = p.After.NoLine, Height = p.After.Height }) != p.After))
            {
                var name = NoteName(p.Note);
                var tick = p.Note.Tick.Original;
                if (p.Before.Lane != p.After.Lane)
                {
                    Warn(diagnostics, MsgKeys.Warn_Editor_lane_adjusted, tick, format, name, p.Before.Lane, p.After.Lane);
                }
                if (p.Before.Width != p.After.Width)
                {
                    Warn(diagnostics, MsgKeys.Warn_Editor_width_adjusted, tick, format, name, p.Before.Width, p.After.Width);
                }
                if (p.Before.Color != p.After.Color)
                {
                    Warn(diagnostics, MsgKeys.Warn_Editor_color_defaulted, tick, format, name, p.Before.Color.ToString());
                }
            }
        }
    }

    private static void CapturePathProjections(U.Chart chart, Dictionary<U.Note, List<EditorPathPoint>> paths,
        Dictionary<U.Note, EditorNoteProjection> before, DiagnosticCollector diagnostics, string format)
    {
        // Flattened curves no longer contain handles to match by note key.
        // Retain their full editable path when any part needed approximation.
        var changedPaths = paths.Keys.Where(n => n.Children.Any(CurveExpansion.IsControl))
            .Where(root => new[] { root }.Concat(root.Children).Any(n => (before[n] with { Key = Key(n) }) != Capture(n)))
            .ToHashSet();
        changedPaths.UnionWith(chart.Notes.Children.OfType<U.AirCrash>()
            .Where(crash => RemoveDuplicateCrashPoints(crash, diagnostics, format)));
        foreach (var group in paths.Keys.GroupBy(Key).Where(g => g.Any(changedPaths.Contains)))
        {
            // Include unchanged overlapping paths to preserve FIFO identity.
            chart.Extras.EditorPathProjections.AddRange(group.Select(n => new EditorPathProjection(group.Key, paths[n])));
        }
    }

    private static bool RemoveDuplicateCrashPoints(U.AirCrash crash, DiagnosticCollector diagnostics, string format)
    {
        var removed = false;
        foreach (var group in crash.Children.GroupBy(p => p.Tick.Original).Where(g => g.Count() > 1).ToArray())
        {
            Warn(diagnostics, MsgKeys.Warn_Editor_crash_points_merged, group.Key, format, group.Count());
            foreach (var point in group.SkipLast(1))
            {
                crash.RemoveChild(point);
            }

            removed = true;
        }
        return removed;
    }

    private static IEnumerable<U.Note> AllNotes(U.Chart chart)
    {
        var written = new HashSet<U.Note>();
        IEnumerable<U.Note> WithPairedNotes(U.Note root)
        {
            if (!written.Add(root))
            {
                yield break;
            }
            yield return root;
            foreach (var child in root.Children)
            {
                if (written.Add(child))
                {
                    yield return child;
                }
            }
            foreach (var positive in new[] { root }.Concat(root.Children).OfType<U.PositiveNote>())
            {
                if (positive.PairNote is not { } paired)
                {
                    continue;
                }
                foreach (var note in WithPairedNotes(paired))
                {
                    yield return note;
                }
            }
        }

        // Both editors write each paired AIR immediately after its ground path.
        // Coincident projection queues must follow that same ownership order.
        foreach (var root in chart.Notes.Children.Where(n => n is not U.NegativeNote)
                     .Concat(chart.Notes.Children.OfType<U.NegativeNote>()))
        {
            foreach (var note in WithPairedNotes(root))
            {
                yield return note;
            }
        }
    }

    internal static void Restore(U.Chart chart, IReadOnlySet<U.Note>? eligible = null, bool restoreEvents = false)
    {
        var extras = chart.Extras;
        // Native export can change container checksums without editing any AIR.
        // A separate complete path check permits color restoration across formats.
        var colors = CaptureUnchangedAirColors(chart);
        if (!extras.BinarySnapshotValid && eligible is null)
        {
            RestoreAirColors(colors);
            return;
        }
        // Resolve keys first: restoring a ground note also changes its paired AIR coordinates.
        var matches = AllNotes(chart).Select(n => (Note: n, Key: Key(n))).ToArray();
        RestoreNotes(extras.EditorNoteProjections, matches, eligible);
        RestorePaths(extras.EditorPathProjections, matches, eligible);
        if (extras.BinarySnapshotValid || restoreEvents)
        {
            RestoreEvents(chart);
        }
        RestoreAirColors(colors);
    }

    internal static void CaptureAirColorKeys(U.Chart chart)
    {
        var projections = chart.Extras.EditorNoteProjections;
        var indices = projections.Select((p, i) => (p, i)).GroupBy(x => x.p.Key)
            .ToDictionary(g => g.Key, g => new Queue<int>(g.Select(x => x.i)));
        foreach (var note in AllNotes(chart).OfType<U.NegativeNote>())
        {
            if (indices.TryGetValue(Key(note), out var queue) && queue.TryDequeue(out var i))
            {
                projections[i] = projections[i] with { AirPathKey = C2sRoundTripKeys.FormatAirPathKey(note) };
            }
        }
        chart.Extras.EditorAirColorKey = projections.Any(p => p.AirPathKey is not null && p.Color is not null)
            ? C2sRoundTripKeys.FormatAirPathEditKey(chart) : null;
    }

    private static List<(U.Note Note, Color Color)> CaptureUnchangedAirColors(U.Chart chart)
    {
        if (chart.Extras.EditorAirColorKey is not { } key || key != C2sRoundTripKeys.FormatAirPathEditKey(chart))
        {
            return [];
        }
        var saved = chart.Extras.EditorNoteProjections.Where(p => p.AirPathKey is not null && p.Color is not null)
            .GroupBy(p => p.AirPathKey!).ToDictionary(g => g.Key, g => new Queue<EditorNoteProjection>(g));
        var result = new List<(U.Note, Color)>();
        foreach (var note in AllNotes(chart).OfType<U.NegativeNote>())
        {
            if (saved.TryGetValue(C2sRoundTripKeys.FormatAirPathKey(note), out var queue) && queue.TryDequeue(out var p))
            {
                result.Add((note, p.Color!.Value));
            }
        }
        return result;
    }

    private static void RestoreAirColors(List<(U.Note Note, Color Color)> colors)
    {
        foreach (var (note, color) in colors)
        {
            SetColor(note, color);
        }
    }

    private static void RestoreNotes(List<EditorNoteProjection> projections, (U.Note Note, string Key)[] matches,
        IReadOnlySet<U.Note>? eligible)
    {
        var saved = projections.GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => new Queue<EditorNoteProjection>(g));
        foreach (var (note, key) in matches)
        {
            if (!saved.TryGetValue(key, out var queue) || !queue.TryDequeue(out var p))
            {
                continue;
            }
            if (eligible is not null && !eligible.Contains(note))
            {
                continue;
            }

            RestoreNote(note, p);
        }
    }

    private static void RestoreNote(U.Note note, EditorNoteProjection projection)
    {
        note.Lane = projection.Lane;
        note.Width = projection.Width;
        if (projection.Height is { } height)
        {
            SetHeight(note, height);
        }
        if (projection.Color is { } color)
        {
            SetColor(note, color);
        }
        if (projection.NoLine is { } noLine)
        {
            if (note is U.Slide slide)
            {
                slide.NoLine = noLine;
            }
            if (note is U.SlideJoint joint)
            {
                joint.NoLine = noLine;
            }
        }
    }

    private static void RestorePaths(List<EditorPathProjection> projections, (U.Note Note, string Key)[] matches,
        IReadOnlySet<U.Note>? eligible)
    {
        var paths = projections.GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => new Queue<EditorPathProjection>(g));
        foreach (var (note, key) in matches)
        {
            if (!paths.TryGetValue(key, out var queue) || !queue.TryDequeue(out var savedPath))
            {
                continue;
            }
            if (eligible is not null && !eligible.Contains(note))
            {
                continue;
            }
            RestorePath(note, savedPath);
        }
    }

    private static void RestorePath(U.Note note, EditorPathProjection savedPath)
    {
        // Reuse surviving endpoints so AIR pair references remain attached.
        var existing = note.Children.GroupBy(n => (n.Tick.Original, n.Timeline))
            .ToDictionary(g => g.Key, g => new Queue<U.Note>(g));
        foreach (var child in note.Children.ToArray())
        {
            note.RemoveChild(child);
        }
        foreach (var point in savedPath.Points)
        {
            U.Note child = note switch
            {
                U.Slide => new U.SlideJoint(),
                U.AirSlide => new U.AirSlideJoint(),
                U.AirCrash => new U.AirCrashJoint(),
                _ => throw new InvalidOperationException("Invalid retained editor path.")
            };
            if (!point.IsCurveControl && existing.TryGetValue((point.Tick, point.Timeline), out var candidates)
                && candidates.TryDequeue(out var endpoint))
            {
                child = endpoint;
            }
            RestorePathPoint(child, point);
            note.AppendChild(child);
        }
    }

    private static void RestorePathPoint(U.Note child, EditorPathPoint point)
    {
        child.Tick = point.Tick;
        child.Timeline = point.Timeline;
        child.Lane = point.Lane;
        child.Width = point.Width;
        if (point.Height is { } height)
        {
            SetHeight(child, height);
        }
        if (child is U.SlideJoint slide)
        {
            slide.Joint = point.Joint ?? Joint.D;
            slide.NoLine = point.NoLine;
            slide.IsCurveControl = point.IsCurveControl;
            slide.HasEffectOverride = point.HasEffectOverride;
            slide.SegmentEffect = point.SegmentEffect;
        }
        if (child is U.AirSlideJoint air)
        {
            air.Joint = point.Joint ?? Joint.D;
            air.IsCurveControl = point.IsCurveControl;
        }
    }

    private static void RestoreEvents(U.Chart chart)
    {
        var extras = chart.Extras;
        if (extras.HasEditorBeatProjection || extras.EditorBeatProjections.Count > 0)
        {
            foreach (var beat in chart.Events.Children.OfType<U.BeatEvent>().ToArray())
            {
                chart.Events.RemoveChild(beat);
            }

            foreach (var beat in extras.EditorBeatProjections)
            {
                chart.Events.AppendChild(new U.BeatEvent
                { Bar = beat.Bar, Tick = beat.Tick, Numerator = beat.Numerator, Denominator = beat.Denominator });
            }
            chart.Events.Sort();
            if (extras.EditorBeatProjections.FirstOrDefault(b => b.Bar == 0) is { } initial)
            {
                chart.Meta.BgmInitialNumerator = initial.Numerator;
                chart.Meta.BgmInitialDenominator = initial.Denominator;
            }
        }
        if (extras.OriginalBlankMeasure is { } blank)
        {
            chart.Meta.BgmEnableBarOffset = blank;
        }
    }

    internal static string Key(U.Note n) =>
        (n.Parent?.Parent is not null ? Key(n.Parent) + "/" : "") +
        string.Create(CultureInfo.InvariantCulture, $"{n.GetType().Name},{n.Tick.Original},{n.Timeline},{n.Lane},{n.Width}");

    private static string NoteName(U.Note note) => note.GetType().Name switch
    {
        "ExTap" => "EX-TAP",
        "HoldJoint" => "HOLD",
        "SlideJoint" => "SLIDE",
        "AirHold" or "AirHoldJoint" => "AIR-HOLD",
        "AirSlide" or "AirSlideJoint" => "AIR-SLIDE",
        "AirCrash" or "AirCrashJoint" => "AIR-CRUSH",
        var name => name.ToUpperInvariant()
    };

    private static EditorNoteProjection Capture(U.Note n) => new(Key(n), n.Lane, n.Width, GetHeight(n), GetColor(n),
        n switch { U.Slide s => s.NoLine, U.SlideJoint s when s.NextSibling is not null => s.NoLine, _ => null });

    private static EditorPathPoint CapturePoint(U.Note n) => new(n.Tick.Original, n.Timeline, n.Lane, n.Width, GetHeight(n),
        n switch { U.SlideJoint s => s.Joint, U.AirSlideJoint a => a.Joint, _ => null },
        n is U.SlideJoint { NoLine: true }, CurveExpansion.IsControl(n),
        n is U.SlideJoint { HasEffectOverride: true }, (n as U.SlideJoint)?.SegmentEffect);

    private static decimal? GetHeight(U.Note n) => n switch
    {
        U.AirSlide a => a.Height,
        U.AirSlideJoint a => a.Height,
        U.AirCrash a => a.Height,
        U.AirCrashJoint a => a.Height,
        _ => null
    };

    private static void SetHeight(U.Note n, decimal height)
    {
        switch (n)
        {
            case U.AirSlide slide:
                slide.Height = height;
                break;
            case U.AirSlideJoint slideJoint:
                slideJoint.Height = height;
                break;
            case U.AirCrash crash:
                crash.Height = height;
                break;
            case U.AirCrashJoint crashJoint:
                crashJoint.Height = height;
                break;
        }
    }

    private static Color? GetColor(U.Note n) => n switch
    { U.Air a => a.Color, U.AirSlide a => a.Color, U.AirHold a => a.Color, _ => null };

    private static void SetColor(U.Note n, Color color)
    {
        switch (n)
        {
            case U.Air air:
                air.Color = color;
                break;
            case U.AirSlide slide:
                slide.Color = color;
                break;
            case U.AirHold hold:
                hold.Color = color;
                break;
        }
    }

    private static void NormalizeMeters(U.Chart chart, DiagnosticCollector diagnostics, string format)
    {
        var beats = chart.Events.Children.OfType<U.BeatEvent>().OrderBy(b => b.Bar).ToArray();
        var original = beats.Select(b => new EditorBeatProjection(b.Bar, b.Tick.Original, b.Numerator, b.Denominator)).ToList();
        var meters = chart.Extras.MeterEditKey == ChartExtras.BeatKey(chart) && chart.Extras.Meters.Count == beats.Length
            ? chart.Extras.Meters.OrderBy(m => m.Tick).ToArray() : null;
        var accepted = new List<U.BeatEvent> { new() { Bar = 0, Tick = 0 } };
        long sourceTick = 0;
        var sourceBar = 0;
        var sourceLength = 1920;
        var changed = false;
        for (var i = 0; i < beats.Length; i++)
        {
            var beat = beats[i];
            sourceTick = meters is null ? sourceTick + (long)(beat.Bar - sourceBar) * sourceLength : meters[i].Tick;
            original[i] = original[i] with { Tick = checked((int)sourceTick) };
            sourceBar = beat.Bar;
            sourceLength = SourceMeasureLength(beat, sourceLength);
            var previous = accepted[^1];
            var length = 1920L * previous.Numerator / previous.Denominator;
            var delta = sourceTick - previous.Tick.Original;
            if (beat.Numerator < 0 || beat.Denominator <= 0 || sourceLength < 0 || delta < 0 ||
                (length == 0 ? delta != 0 : delta % length != 0))
            {
                changed = true;
                Warn(diagnostics, MsgKeys.Warn_Editor_meter_skipped, checked((int)sourceTick), format,
                    $"{beat.Numerator}/{beat.Denominator}", $"{previous.Numerator}/{previous.Denominator}");
                continue;
            }
            // Zero-length measures occupy bar numbers without advancing time.
            // Keep those numbers so a native save does not erase their events.
            var bar = length == 0 ? beat.Bar : checked(previous.Bar + (int)(delta / length));
            changed |= bar != beat.Bar;
            if (bar == previous.Bar)
            {
                accepted.RemoveAt(accepted.Count - 1);
            }

            accepted.Add(new U.BeatEvent { Bar = bar, Tick = checked((int)sourceTick), Numerator = beat.Numerator, Denominator = beat.Denominator });
        }
        foreach (var beat in beats)
        {
            chart.Events.RemoveChild(beat);
        }

        foreach (var beat in accepted)
        {
            chart.Events.AppendChild(beat);
        }

        chart.Events.Sort();
        chart.Meta.BgmInitialNumerator = accepted[0].Numerator;
        chart.Meta.BgmInitialDenominator = accepted[0].Denominator;
        if (changed || !original.SequenceEqual(accepted.Select(b =>
                new EditorBeatProjection(b.Bar, b.Tick.Original, b.Numerator, b.Denominator))))
        {
            chart.Extras.EditorBeatProjections = original;
            chart.Extras.HasEditorBeatProjection = true;
        }
    }

    private static int SourceMeasureLength(U.BeatEvent beat, int previous) =>
        beat.Denominator > 0 && beat.Numerator >= 0
            ? checked((int)(1920L * beat.Numerator / beat.Denominator)) : previous;

    private static void Warn(DiagnosticCollector diagnostics, string key, int tick, params object?[] args) =>
        diagnostics.Report(new TimedDiagnostic(Severity.Warning, Msg.Create(key, args), tick));
}
