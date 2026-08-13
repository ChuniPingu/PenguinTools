using System.Globalization;
using System.Text;

namespace PenguinTools.Core.Metadata;

/// <summary>
/// Persists C2S round-trip state as <c>#meta</c> lines in <see cref="Meta.Comment"/>,
/// the same channel already used for stage/genre/date overrides.
/// </summary>
public static class C2sRoundTripComment
{
    public const string JudgeTag = "c2sjudge";
    public const string JudgeProxyTag = "c2sjudgeproxy";
    public const string MeterTag = "c2smeter";
    public const string SlpTag = "c2sslp";
    public const string SlaTag = "c2ssla";
    public const string SlpEditTag = "c2sslpedit";
    public const string SlaEditTag = "c2sslaedit";

    private const string NullProxyToken = "x";

    private static readonly HashSet<string> TagNames =
    [
        JudgeTag,
        JudgeProxyTag,
        MeterTag,
        SlpTag,
        SlaTag,
        SlpEditTag,
        SlaEditTag
    ];

    public static bool IsRoundTripTag(string name) => TagNames.Contains(name);

    public static string Apply(Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var body = Strip(meta.Comment);
        var tags = FormatTags(meta);

        if (tags.Length == 0)
            return body;

        if (string.IsNullOrEmpty(body))
            return tags;

        return body.TrimEnd('\r', '\n') + "\n" + tags;
    }

    public static string Strip(string comment)
    {
        if (string.IsNullOrEmpty(comment))
            return string.Empty;

        var lines = comment.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var kept = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            if (IsRoundTripMetaLine(line))
                continue;

            kept.Add(line);
        }

        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            kept.RemoveAt(kept.Count - 1);

        return string.Join('\n', kept);
    }

    public static bool TryHandle(Meta meta, string name, string[] args)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(args);

        switch (name)
        {
            case JudgeTag:
                ParseJudge(meta, args);
                return true;
            case JudgeProxyTag:
                ParseJudgeProxy(meta, args);
                return true;
            case MeterTag:
                ParseMeter(meta, args);
                return true;
            case SlpTag:
                meta.C2sSlpSnapshot = JoinArgs(args);
                return true;
            case SlaTag:
                meta.C2sSlaSnapshot = JoinArgs(args);
                return true;
            case SlpEditTag:
                meta.C2sSlpEditKey = JoinArgs(args);
                return true;
            case SlaEditTag:
                meta.C2sSlaEditKey = JoinArgs(args);
                return true;
            default:
                return false;
        }
    }

    private static string FormatTags(Meta meta)
    {
        var sb = new StringBuilder();

        if (meta.TryGetC2sJudgeSummary(out var tap, out var hld, out var sld, out var air, out var flk, out var all))
            Append(sb, JudgeTag, $"{tap} {hld} {sld} {air} {flk} {all}");

        if (meta.C2sJudgeHldProxyBaseline is >= 0 ||
            meta.C2sJudgeSldProxyBaseline is >= 0 ||
            meta.C2sJudgeAirProxyBaseline is >= 0)
        {
            Append(sb, JudgeProxyTag,
                $"{FormatProxy(meta.C2sJudgeHldProxyBaseline)} " +
                $"{FormatProxy(meta.C2sJudgeSldProxyBaseline)} " +
                $"{FormatProxy(meta.C2sJudgeAirProxyBaseline)}");
        }

        if (meta.C2sMeterDefDenominator is >= 0 and var denominator &&
            meta.C2sMeterDefNumerator is >= 0 and var numerator)
            Append(sb, MeterTag, $"{denominator} {numerator}");

        if (meta.C2sSlpSnapshot is not null)
            Append(sb, SlpTag, meta.C2sSlpSnapshot);

        if (meta.C2sSlaSnapshot is not null)
            Append(sb, SlaTag, meta.C2sSlaSnapshot);

        if (meta.C2sSlpEditKey is not null)
            Append(sb, SlpEditTag, meta.C2sSlpEditKey);

        if (meta.C2sSlaEditKey is not null)
            Append(sb, SlaEditTag, meta.C2sSlaEditKey);

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void Append(StringBuilder sb, string tag, string args)
    {
        sb.Append("#meta ");
        sb.Append(tag);
        if (args.Length > 0)
        {
            sb.Append(' ');
            sb.Append(args);
        }

        sb.Append('\n');
    }

    private static void ParseJudge(Meta meta, string[] args)
    {
        if (args.Length != 6)
            return;

        var values = new int[6];
        for (var i = 0; i < 6; i++)
        {
            if (!int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]) ||
                values[i] < 0)
                return;
        }

        if ((long)values[0] + values[1] + values[2] + values[3] + values[4] != values[5])
            return;

        meta.C2sJudgeTap = values[0];
        meta.C2sJudgeHld = values[1];
        meta.C2sJudgeSld = values[2];
        meta.C2sJudgeAir = values[3];
        meta.C2sJudgeFlk = values[4];
        meta.C2sJudgeAll = values[5];
    }

    private static void ParseJudgeProxy(Meta meta, string[] args)
    {
        if (args.Length != 3)
            return;

        if (!TryParseProxy(args[0], out var hld) ||
            !TryParseProxy(args[1], out var sld) ||
            !TryParseProxy(args[2], out var air))
            return;

        meta.C2sJudgeHldProxyBaseline = hld;
        meta.C2sJudgeSldProxyBaseline = sld;
        meta.C2sJudgeAirProxyBaseline = air;
    }

    private static void ParseMeter(Meta meta, string[] args)
    {
        if (args.Length != 2)
            return;

        if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator) ||
            !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator) ||
            denominator < 0 ||
            numerator < 0)
            return;

        meta.C2sMeterDefDenominator = denominator;
        meta.C2sMeterDefNumerator = numerator;
    }

    private static string FormatProxy(int? value) =>
        value is >= 0 and var parsed
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : NullProxyToken;

    private static bool TryParseProxy(string token, out int? value)
    {
        if (token.Equals(NullProxyToken, StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= 0)
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static string JoinArgs(string[] args) => string.Join(' ', args);

    private static bool IsRoundTripMetaLine(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('#'))
            return false;

        var parts = trimmed[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
               parts[0].Equals("meta", StringComparison.Ordinal) &&
               TagNames.Contains(parts[1]);
    }
}
