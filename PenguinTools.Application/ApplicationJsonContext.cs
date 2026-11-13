using System.Text.Json.Serialization;
using PenguinTools.Workflow;

namespace PenguinTools.Application;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(OptionDocument))]
[JsonSerializable(typeof(OptionBuildOverrides))]
internal partial class ApplicationJsonContext : JsonSerializerContext;
