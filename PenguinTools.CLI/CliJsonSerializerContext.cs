using System.Text.Json.Serialization;
using PenguinTools.Application;

namespace PenguinTools.CLI;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliResponse))]
[JsonSerializable(typeof(CliProgressEvent))]
[JsonSerializable(typeof(MessageDescriptor))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(CliDiagnosticPayload[]))]
[JsonSerializable(typeof(ChartInspectResult))]
[JsonSerializable(typeof(ChartConvertResult))]
[JsonSerializable(typeof(OptionScanResult))]
[JsonSerializable(typeof(OptionBuildResult))]
[JsonSerializable(typeof(MusicBuildResult))]
[JsonSerializable(typeof(MusicExtractResult))]
[JsonSerializable(typeof(JacketConvertResult))]
[JsonSerializable(typeof(AudioConvertResult))]
[JsonSerializable(typeof(CriAudioExtractResult))]
[JsonSerializable(typeof(StageBuildResult))]
[JsonSerializable(typeof(AfbExtractResult))]
[JsonSerializable(typeof(AssetCollectResult))]
[JsonSerializable(typeof(ApplicationInfo))]
internal sealed partial class CliJsonSerializerContext : JsonSerializerContext;
