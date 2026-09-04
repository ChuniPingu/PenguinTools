using System.Globalization;

namespace PenguinTools.Core.Metadata;

/// <summary>
/// Persists converter-owned C2S round-trip state as <c>#meta</c> lines in MGXC
/// bookmarks. User-controlled comment content remains in
/// <see cref="Meta.Comment"/>.
/// </summary>
public static class C2sRoundTripComment
{
    private const string JudgeTag = "c2sjudge";
    private const string JudgeProxyTag = "c2sjudgeproxy";
    private const string MeterTag = "c2smeter";
    private const string SlpTag = "c2sslp";
    private const string SlaTag = "c2ssla";
    private const string AirTag = "c2sair";
    private const string SlpEditTag = "c2sslpedit";
    private const string SlaEditTag = "c2sslaedit";
    private const string AirEditTag = "c2sairedit";

    private const string NullProxyToken = "x";
    private const string MetaPrefix = "#meta ";

    private static readonly HashSet<string> TagNames =
    [
        JudgeTag,
        JudgeProxyTag,
        MeterTag,
        SlpTag,
        SlaTag,
        AirTag,
        SlpEditTag,
        SlaEditTag,
        AirEditTag
    ];

    public static bool IsRoundTripLine(string line) =>
        TryParseRoundTripLine(line, out _, out _);

    public static IReadOnlyList<string> FormatBookmarks(Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var bookmarks = new List<string>();

        if (meta.TryGetC2sJudgeSummary(out var tap, out var hld, out var sld, out var air, out var flk, out var all))
            AppendBookmark(bookmarks, JudgeTag, $"{tap} {hld} {sld} {air} {flk} {all}");

        if (meta.C2sJudgeHldProxyBaseline is >= 0 ||
            meta.C2sJudgeSldProxyBaseline is >= 0 ||
            meta.C2sJudgeAirProxyBaseline is >= 0)
        {
            AppendBookmark(
                bookmarks,
                JudgeProxyTag,
                $"{FormatProxy(meta.C2sJudgeHldProxyBaseline)} " +
                $"{FormatProxy(meta.C2sJudgeSldProxyBaseline)} " +
                $"{FormatProxy(meta.C2sJudgeAirProxyBaseline)}");
        }

        if (meta.C2sMeterDefDenominator is >= 0 and var denominator &&
            meta.C2sMeterDefNumerator is >= 0 and var numerator)
            AppendBookmark(bookmarks, MeterTag, $"{denominator} {numerator}");

        AppendBookmark(bookmarks, SlpEditTag, meta.C2sSlpEditKey);
        AppendBookmark(bookmarks, SlaEditTag, meta.C2sSlaEditKey);
        AppendBookmark(bookmarks, AirEditTag, meta.C2sAirEditKey);
        AppendBookmark(bookmarks, SlpTag, meta.C2sSlpSnapshot);
        AppendBookmark(bookmarks, SlaTag, meta.C2sSlaSnapshot);
        AppendBookmark(bookmarks, AirTag, meta.C2sAirSnapshot);

        return bookmarks;
    }

    public static void Absorb(Meta meta, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(lines);

        foreach (var line in lines)
        {
            if (!TryParseRoundTripLine(line, out var tag, out var payload))
                continue;

            var args = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Handle(meta, tag, args);
        }
    }

    public static string Strip(string comment)
    {
        if (string.IsNullOrEmpty(comment))
            return string.Empty;

        var lines = comment.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var kept = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (IsRoundTripLine(trimmed))
            {
                continue;
            }

            kept.Add(line);
        }

        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            kept.RemoveAt(kept.Count - 1);

        return string.Join('\n', kept);
    }

    private static void Handle(Meta meta, string name, string[] args)
    {
        switch (name)
        {
            case JudgeTag:
                ParseJudge(meta, args);
                break;
            case JudgeProxyTag:
                ParseJudgeProxy(meta, args);
                break;
            case MeterTag:
                ParseMeter(meta, args);
                break;
            case SlpTag:
                meta.C2sSlpSnapshot = JoinArgs(args);
                break;
            case SlaTag:
                meta.C2sSlaSnapshot = JoinArgs(args);
                break;
            case AirTag:
                meta.C2sAirSnapshot = JoinArgs(args);
                break;
            case SlpEditTag:
                meta.C2sSlpEditKey = JoinArgs(args);
                break;
            case SlaEditTag:
                meta.C2sSlaEditKey = JoinArgs(args);
                break;
            case AirEditTag:
                meta.C2sAirEditKey = JoinArgs(args);
                break;
        }
    }

    private static void AppendBookmark(List<string> bookmarks, string tag, string? args)
    {
        if (args is null)
            return;

        bookmarks.Add(args.Length == 0
            ? MetaPrefix + tag
            : MetaPrefix + tag + " " + args);
    }

    private static bool TryParseRoundTripLine(string line, out string tag, out string payload)
    {
        tag = string.Empty;
        payload = string.Empty;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(MetaPrefix, StringComparison.Ordinal))
            return false;

        var rest = trimmed[MetaPrefix.Length..];
        var space = rest.IndexOf(' ');
        var name = space < 0 ? rest : rest[..space];
        if (!TagNames.Contains(name))
            return false;

        tag = name;
        payload = space < 0 ? string.Empty : rest[(space + 1)..];
        return true;
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
}
