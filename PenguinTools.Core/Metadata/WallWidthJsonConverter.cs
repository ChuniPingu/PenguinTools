using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenguinTools.Core.Metadata;

public sealed class WallWidthJsonConverter : JsonConverter<WallWidth>
{
    public override WallWidth Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString() switch
        {
            "normal" => WallWidth.Normal,
            "wide" => WallWidth.Wide,
            _ => throw new JsonException("Invalid wall width.")
        } : throw new JsonException("Wall width must be a string.");

    public override void Write(Utf8JsonWriter writer, WallWidth value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            WallWidth.Normal => "normal",
            WallWidth.Wide => "wide",
            _ => throw new JsonException("Invalid wall width.")
        });
}
