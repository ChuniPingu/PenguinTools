using System.Globalization;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

/// <summary>Resolves input conventions from the document, independently of the exporting application's version.</summary>
internal static class MargreteInput
{
    internal const string DialectPrefix = "PenguinTools dialect:";
    internal const string ExtendedEditorMarker = "PenguinTools editor: extended";

    internal static EditorTarget MgxcTarget(U.Chart chart, int version, bool hasExtensions) =>
        hasExtensions || NeedsExtensions(chart, version <= 2)
            ? EditorTarget.Extended : version <= 2 ? EditorTarget.Legacy : EditorTarget.Standard;

    internal static EditorTarget UgcTarget(U.Chart chart, int version, IEnumerable<string> lines,
        bool fixedCoordinates)
    {
        var extended = fixedCoordinates;
        var originalCreator = false;
        var extendedCreator = false;
        foreach (var line in lines)
        {
            if (!line.Trim().StartsWith('\''))
            {
                continue;
            }
            var comment = line.Trim()[1..].Trim();
            extended |= comment == ExtendedEditorMarker;
            const string creator = "Created with Margrete v";
            if (comment.StartsWith(creator, StringComparison.Ordinal) &&
                Version.TryParse(comment[creator.Length..].Split(' ', '\t', '-', '+')[0], out var exporter))
            {
                originalCreator |= exporter.Major == 1;
                extendedCreator |= exporter.Major == 2 && exporter.Minor >= 3;
            }
        }
        if (originalCreator && extendedCreator)
        {
            throw Invalid("conflicting creators", "one source editor capability declaration");
        }
        // A native editor save is stronger evidence than an inherited tool comment.
        var legacyRotation = version <= 8 && chart.Notes.Children.OfType<U.AirCrash>()
            .Any(n => n.Attr is AirLadderAttr.AxisY or AirLadderAttr.AxisZ);
        return extendedCreator || (!originalCreator && extended) || legacyRotation || NeedsExtensions(chart, version <= 8)
            ? EditorTarget.Extended : version <= 8 ? EditorTarget.Legacy : EditorTarget.Standard;
    }

    internal static bool NeedsExtensions(U.Chart chart, bool legacy)
    {
        if (chart.Events.Children.OfType<U.BeatEvent>().Any(b => b.Numerator == 0) ||
            chart.Extras.ClickCount is { } count && count != chart.Meta.BgmInitialNumerator ||
            chart.Extras.SegmentSlideLines && chart.Notes.Children.OfType<U.Slide>()
                .Any(s => SlideSegmentMetadata.IsMixed(SlideSegmentMetadata.Bits(s))))
        {
            return true;
        }
        return chart.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children)).Any(n =>
            n.Lane < (legacy ? 0 : -16) || n.Lane + (long)n.Width > (legacy ? 16 : 32) ||
            n.Width > 16 || legacy && (n.Width == 0 || n is U.Slide { NoLine: true } or U.SlideJoint { NoLine: true }) ||
            n is U.Air { Color: not (Color.DEF or Color.PNK) }
                or U.AirHold { Color: not (Color.DEF or Color.PNK) }
                or U.AirSlide { Color: not (Color.DEF or Color.PNK) }
                or U.AirSlide { Height: < 0 or > ushort.MaxValue }
                or U.AirSlideJoint { Height: < 0 or > ushort.MaxValue }
                or U.AirCrash { HasExplicitJudgements: true }
                or U.AirCrash { Height: < 0 or > ushort.MaxValue }
                or U.AirCrashJoint { Height: < 0 or > ushort.MaxValue });
    }

    internal static bool FixedUgcCoordinates(IEnumerable<string> lines, int formatVersion)
    {
        bool? explicitFixed = null;
        var legacyExporter = false;
        var officialExporter = false;
        var origin = false;
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.StartsWith("@COPYRIGHT\t", StringComparison.OrdinalIgnoreCase))
            {
                origin |= ChartExtras.HasFixedZeroOrigin(text["@COPYRIGHT\t".Length..]);
            }
            if (!text.StartsWith('\''))
            {
                continue;
            }
            var comment = text[1..].Trim();
            if (comment.StartsWith(DialectPrefix, StringComparison.Ordinal))
            {
                var value = comment[DialectPrefix.Length..].Trim();
                bool current = value switch
                {
                    "original" => false,
                    "extended" => true,
                    _ => throw Invalid(value, "original or extended coordinate marker")
                };
                if (explicitFixed.HasValue && explicitFixed.Value != current)
                {
                    throw Invalid(comment, "consistent coordinate markers");
                }
                explicitFixed = current;
            }
            else if (comment.StartsWith("MGR_ORIGIN=", StringComparison.Ordinal))
            {
                origin |= ChartExtras.HasFixedZeroOrigin(comment);
            }
            else if (comment.StartsWith("Created with Margrete v", StringComparison.Ordinal))
            {
                var token = comment["Created with Margrete v".Length..].Split(' ', '\t', '-', '+')[0];
                if (Version.TryParse(token, out var version))
                {
                    // These exporters wrote fixed-width UGC v8 pairs. Later v9
                    // exports use musical bars, even when the origin is fixed.
                    legacyExporter |= version.Major == 2 && version.Minor == 3 && version.Build is >= 0 and <= 3;
                    officialExporter |= version.Major == 1;
                }
            }
        }
        if (legacyExporter && officialExporter)
        {
            throw Invalid("conflicting creators", "one source coordinate convention");
        }
        if (formatVersion >= 9)
        {
            if (explicitFixed == true)
            {
                throw Invalid("UGC v9 with extended coordinate marker", "musical-bar UGC v9");
            }
            return false;
        }
        return explicitFixed ?? (legacyExporter || (!officialExporter && origin));
    }

    internal static int? ClickCount(string? copyright)
    {
        int? result = null;
        foreach (var token in (copyright ?? "").Split(';').Select(t => t.Trim()))
        {
            const string prefix = "MGR_CLKCNT=";
            if (!token.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            if (!int.TryParse(token.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                (result.HasValue && result.Value != count))
            {
                throw Invalid(token, "one nonnegative metronome count");
            }
            result = count;
        }
        return result;
    }

    internal static int? LegacyLane(char value) => value switch
    {
        >= '\uE000' and <= '\uE0FF' => -(value - 0xE000 + 1),
        >= '\uE100' and <= '\uE1FF' => 36 + value - 0xE100,
        >= '\uE200' and <= '\uE8FF' => -(257 + value - 0xE200),
        >= '\uE900' and <= '\uF8FF' => 292 + value - 0xE900,
        _ => null
    };

    internal static DiagnosticException Invalid(object value, string expected) =>
        new(Msg.Create(MsgKeys.Error_Invalid_Header, value, expected));
}
