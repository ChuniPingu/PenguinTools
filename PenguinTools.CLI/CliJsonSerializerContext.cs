using System.Text.Json.Serialization;
using PenguinTools.Application;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.CLI;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliResponse))]
[JsonSerializable(typeof(CliProgressEvent))]
[JsonSerializable(typeof(MessageDescriptor))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(CliDiagnosticPayload[]))]
[JsonSerializable(typeof(TickPosition))]
[JsonSerializable(typeof(ChartInspectResult))]
[JsonSerializable(typeof(ChartConvertResult))]
[JsonSerializable(typeof(OptionScanConfig))]
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
[JsonSerializable(typeof(ApplicationEntry))]
[JsonSerializable(typeof(Entry))]
[JsonSerializable(typeof(ApplicationInfo))]
internal sealed partial class CliJsonSerializerContext : JsonSerializerContext;
