using System.Globalization;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart;

/// <summary>Resolves input conventions from the document, independently of the exporting application's version.</summary>
internal static class MargreteInput
{
    internal const string DialectPrefix = "PenguinTools dialect:";

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
