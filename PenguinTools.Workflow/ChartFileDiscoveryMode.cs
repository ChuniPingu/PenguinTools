using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenguinTools.Workflow;

public enum ChartFileFormat
{
    [Description("mgxc")] Mgxc = 0,

    [Description("ugc")] Ugc = 1,

    [Description("sus")] Sus = 2
}

public static class ChartFileDiscoveryFormats
{
    public static IReadOnlyList<ChartFileFormat> Default { get; } =
        [ChartFileFormat.Mgxc, ChartFileFormat.Ugc, ChartFileFormat.Sus];

    public static IReadOnlyList<ChartFileFormat> Normalize(IEnumerable<ChartFileFormat>? formats)
    {
        List<ChartFileFormat> ordered = [];
        HashSet<ChartFileFormat> seen = [];

        if (formats is not null)
            foreach (var format in formats)
                if (seen.Add(format))
                    ordered.Add(format);

        return ordered.Count == 0 ? [.. Default] : ordered;
    }

    public static string Format(IEnumerable<ChartFileFormat>? formats)
    {
        return string.Join(", ", Normalize(formats).Select(ToToken));
    }

    public static string ToToken(ChartFileFormat format)
    {
        return format switch
        {
            ChartFileFormat.Mgxc => "mgxc",
            ChartFileFormat.Ugc => "ugc",
            ChartFileFormat.Sus => "sus",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    public static string GetGlob(ChartFileFormat format)
    {
        return $"*{GetExtension(format)}";
    }

    public static string GetExtension(ChartFileFormat format)
    {
        return format switch
        {
            ChartFileFormat.Mgxc => ".mgxc",
            ChartFileFormat.Ugc => ".ugc",
            ChartFileFormat.Sus => ".sus",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    public static IReadOnlyList<ChartFileFormat> ParseOrDefault(string? text)
    {
        return TryParse(text, out var formats, out _) ? formats : [.. Default];
    }

    public static bool TryParse(string? text, out IReadOnlyList<ChartFileFormat> formats, out MessageDescriptor? error)
    {
        formats = [];

        if (string.IsNullOrWhiteSpace(text))
        {
            error = Msg.Key(MsgKeys.Error_Chart_format_specify_at_least_one);
            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.StartsWith('['))
        {
            if (!trimmed.EndsWith(']'))
            {
                error = Msg.Key(MsgKeys.Error_Chart_format_brackets_mismatch);
                return false;
            }

            trimmed = trimmed[1..^1];
        }

        var tokens = trimmed.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = Msg.Key(MsgKeys.Error_Chart_format_specify_at_least_one);
            return false;
        }

        List<ChartFileFormat> ordered = [];
        foreach (var token in tokens)
        {
            if (!TryParseToken(token, out var format))
            {
                error = Msg.Create(MsgKeys.Error_Chart_format_unsupported, token);
                return false;
            }

            ordered.Add(format);
        }

        formats = Normalize(ordered);
        error = null;
        return true;
    }

    public static bool TryParseToken(string? token, out ChartFileFormat format)
    {
        var normalized = token?.Trim();
        if (string.Equals(normalized, "mgxc", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, ".mgxc", StringComparison.OrdinalIgnoreCase))
        {
            format = ChartFileFormat.Mgxc;
            return true;
        }

        if (string.Equals(normalized, "ugc", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, ".ugc", StringComparison.OrdinalIgnoreCase))
        {
            format = ChartFileFormat.Ugc;
            return true;
        }

        if (string.Equals(normalized, "sus", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, ".sus", StringComparison.OrdinalIgnoreCase))
        {
            format = ChartFileFormat.Sus;
            return true;
        }

        format = default;
        return false;
    }
}

public sealed class ChartFileDiscoveryJsonConverter : JsonConverter<List<ChartFileFormat>>
{
    public override List<ChartFileFormat> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [.. ChartFileDiscoveryFormats.Default];

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            List<ChartFileFormat> formats = [];

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return [.. ChartFileDiscoveryFormats.Normalize(formats)];

                if (reader.TokenType == JsonTokenType.String)
                {
                    var token = reader.GetString();
                    if (!ChartFileDiscoveryFormats.TryParseToken(token, out var format))
                    {
                        throw new JsonException(
                            Msg.Create(MsgKeys.Error_Chart_format_unsupported, token).Key);
                    }

                    formats.Add(format);
                    continue;
                }

                throw new JsonException(Msg.Key(MsgKeys.Error_Chart_discovery_must_be_array).Key);
            }

            throw new JsonException(Msg.Key(MsgKeys.Error_Chart_discovery_array_incomplete).Key);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (ChartFileDiscoveryFormats.TryParse(text, out var formats, out var error))
                return [.. formats];

            throw new JsonException(error?.Key);
        }

        throw new JsonException(Msg.Key(MsgKeys.Error_Chart_discovery_must_be_string_or_array).Key);
    }

    public override void Write(Utf8JsonWriter writer, List<ChartFileFormat> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var format in ChartFileDiscoveryFormats.Normalize(value))
            writer.WriteStringValue(ChartFileDiscoveryFormats.ToToken(format));

        writer.WriteEndArray();
    }
}