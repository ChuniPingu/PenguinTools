using System.Globalization;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

public sealed record EditorNoteProjection(string Key, int Lane, int Width, decimal? Height, Color? Color, bool? NoLine);
public sealed record EditorBeatProjection(int Bar, int Tick, int Numerator, int Denominator);
public sealed record EditorPathPoint(int Tick, int Timeline, int Lane, int Width, decimal? Height,
    Joint? Joint, bool NoLine, bool IsCurveControl, bool HasEffectOverride, ExEffect? SegmentEffect);
public sealed record EditorPathProjection(string Key, List<EditorPathPoint> Points);

/// <summary>Creates the official editor view while retaining checked round-trip information.</summary>
internal static class EditorProjection
{
    internal static U.Chart Create(U.Chart source, bool ugc, DiagnosticCollector diagnostics)
    {
        var chart = ChartCopy.Create(source);
        var format = ugc ? "UGC v9" : "MGXC v4";
        var extras = chart.Extras;
        extras.EditorNoteProjections.Clear();
        extras.EditorBeatProjections.Clear();
        extras.EditorPathProjections.Clear();
        // Check the editable source before normalization or curve expansion changes
        // its view. A newly written checksum must not validate an older snapshot.
        var airKey = extras.AirModelKey ?? chart.Meta.C2sAirEditKey;
        if (airKey is not null && airKey != C2sRoundTripKeys.FormatAirEditKey(chart))
        {
            chart.Meta.C2sAirSnapshot = null;
            chart.Meta.C2sAirEditKey = null;
        }
        var slaKey = extras.SlaModelKey ?? chart.Meta.C2sSlaEditKey;
        if (slaKey is not null && slaKey != C2sRoundTripKeys.FormatSlaEditKey(chart))
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
        extras.EncodingDialect = ChartDialect.Original;
        extras.EditorFormatVersion = ugc ? 9 : 4;
        NormalizeMeters(chart, diagnostics, format);
        diagnostics.TimeCalculator = chart.GetCalculator();
        diagnostics.BackfillTimeCalculator();
        var notes = AllNotes(chart).ToArray();
        var paths = chart.Notes.Children.Where(n => n is U.Slide or U.AirSlide or U.AirCrash)
            .ToDictionary(n => n, n => n.Children.Select(CapturePoint).ToList());
        var changedPaths = new HashSet<U.Note>();
        chart.Meta.ExtendedField |= notes.Any(n => !CurveExpansion.IsControl(n) && (n.Lane < 0 || n.Lane + (long)n.Width > 16));
        // Capture attributes before changing shared pair coordinates.
        var before = notes.ToDictionary(n => n, Capture);
        for (var i = 0; i < notes.Length; i++)
        {
            var n = notes[i];
            var clippedControl = ugc && n is U.SlideJoint { Joint: Joint.C, IsCurveControl: false }
                or U.AirSlideJoint { Joint: Joint.C, IsCurveControl: false };
            n.Width = Math.Clamp(n.Width, clippedControl ? 0 : 1, 16);
            var expanded = chart.Meta.ExtendedField || CurveExpansion.IsControl(n);
            n.Lane = Math.Clamp(n.Lane, expanded ? -16 : 0, (expanded ? 32 : 16) - n.Width);
            if (GetHeight(n) is { } height)
            {
                var maxHeight = ugc ? 1295 : ushort.MaxValue;
                SetHeight(n, Math.Clamp(decimal.Round(height, 0, MidpointRounding.AwayFromZero), 0, maxHeight));
            }
            if (GetColor(n) is not (null or Color.DEF or Color.PNK))
            {
                SetColor(n, Color.DEF);
            }
            if (n is U.Slide slide)
            {
                foreach (var joint in slide.Children.OfType<U.SlideJoint>())
                {
                    joint.NoLine = slide.NoLine;
                }
            }
        }
        chart.Notes.Sort();
        // Retain unchanged colliding notes too, so FIFO restoration cannot apply a
        // changed note's attributes to an earlier overlapping note.
        foreach (var group in AllNotes(chart).GroupBy(Key))
        {
            var snapshots = group.Select(n => (Note: n, Before: before[n] with { Key = group.Key }, After: Capture(n))).ToArray();
            if (!snapshots.Any(p => p.Before != p.After))
            {
                continue;
            }

            extras.EditorNoteProjections.AddRange(snapshots.Select(p => p.Before));
            // Line flags and height encoding limits are normalized silently.
            foreach (var p in snapshots.Where(p => (p.Before with { NoLine = p.After.NoLine, Height = p.After.Height }) != p.After))
            {
                Warn(diagnostics, format, p.Note.GetType().Name, Describe(p.Before), Describe(p.After), p.Note.Tick.Original);
            }
        }
        if (ugc)
        {
            // Flattened curves no longer contain handles to match by note key.
            // Retain their full editable path when any part needed approximation.
            foreach (var root in paths.Keys.Where(n => n.Children.Any(CurveExpansion.IsControl)))
            {
                if (new[] { root }.Concat(root.Children).Any(n => (before[n] with { Key = Key(n) }) != Capture(n)))
                {
                    changedPaths.Add(root);
                }
            }
            foreach (var crash in chart.Notes.Children.OfType<U.AirCrash>())
            {
                var removed = false;
                foreach (var group in crash.Children.GroupBy(p => p.Tick.Original).Where(g => g.Count() > 1).ToArray())
                {
                    Warn(diagnostics, format, "AIR-CRUSH path", group.Count(), 1, group.Key);
                    foreach (var point in group.SkipLast(1))
                    {
                        crash.RemoveChild(point);
                    }

                    removed = true;
                }
                if (removed)
                {
                    changedPaths.Add(crash);
                }
            }
            foreach (var group in paths.Keys.GroupBy(Key).Where(g => g.Any(changedPaths.Contains)))
            {
                // Include unchanged overlapping paths to preserve FIFO identity.
                extras.EditorPathProjections.AddRange(group.Select(n => new EditorPathProjection(group.Key, paths[n])));
            }
        }
        return chart;
    }

    private static IEnumerable<U.Note> AllNotes(U.Chart chart) =>
        chart.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children));

    internal static void Restore(U.Chart chart, IReadOnlySet<U.Note>? eligible = null, bool restoreEvents = false)
    {
        var extras = chart.Extras;
        if (!extras.BinarySnapshotValid && eligible is null)
        {
            return;
        }
        var saved = extras.EditorNoteProjections.GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => new Queue<EditorNoteProjection>(g));
        // Resolve keys first: restoring a ground note also changes its paired AIR coordinates.
        var matches = AllNotes(chart).Select(n => (Note: n, Key: Key(n))).ToArray();
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

            note.Lane = p.Lane;
            note.Width = p.Width;
            if (p.Height is { } height)
            {
                SetHeight(note, height);
            }

            if (p.Color is { } color)
            {
                SetColor(note, color);
            }

            if (p.NoLine is { } noLine)
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
        var paths = extras.EditorPathProjections.GroupBy(p => p.Key)
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
                note.AppendChild(child);
            }
        }
        if (!extras.BinarySnapshotValid && !restoreEvents)
        {
            return;
        }

        if (extras.EditorBeatProjections.Count > 0)
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

    private static string Describe(EditorNoteProjection p) => string.Create(CultureInfo.InvariantCulture,
        $"x={p.Lane}, w={p.Width}, h={p.Height}, color={p.Color}");

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
            case U.AirSlide a:
                a.Height = height;
                break;
            case U.AirSlideJoint a:
                a.Height = height;
                break;
            case U.AirCrash a:
                a.Height = height;
                break;
            case U.AirCrashJoint a:
                a.Height = height;
                break;
        }
    }

    private static Color? GetColor(U.Note n) => n switch
    { U.Air a => a.Color, U.AirSlide a => a.Color, U.AirHold a => a.Color, _ => null };

    private static void SetColor(U.Note n, Color color)
    {
        switch (n)
        {
            case U.Air a:
                a.Color = color;
                break;
            case U.AirSlide a:
                a.Color = color;
                break;
            case U.AirHold a:
                a.Color = color;
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
            sourceBar = beat.Bar;
            sourceLength = beat.Denominator > 0 && beat.Numerator >= 0
                ? checked((int)(1920L * beat.Numerator / beat.Denominator)) : sourceLength;
            var previous = accepted[^1];
            var length = 1920L * previous.Numerator / previous.Denominator;
            var delta = sourceTick - previous.Tick.Original;
            if (beat.Numerator <= 0 || beat.Denominator <= 0 || sourceLength <= 0 || delta < 0 || delta % length != 0)
            {
                changed = true;
                Warn(diagnostics, format, "BEAT", $"{beat.Numerator}/{beat.Denominator}",
                    $"{previous.Numerator}/{previous.Denominator}", checked((int)sourceTick));
                continue;
            }
            var bar = checked(previous.Bar + (int)(delta / length));
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
        if (changed)
        {
            chart.Extras.EditorBeatProjections = original;
        }
    }

    private static void Warn(DiagnosticCollector diagnostics, string format, string feature, object before, object after, int tick) =>
        diagnostics.Report(new TimedDiagnostic(Severity.Warning,
            Msg.Create(MsgKeys.Warn_Editor_approximation, format, feature, before, after), tick));
}
