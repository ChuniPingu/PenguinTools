using System.Text.Json.Serialization;
using PenguinTools.Media;

namespace PenguinTools.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CriExtractResult))]
internal sealed partial class InfrastructureJsonContext : JsonSerializerContext;
