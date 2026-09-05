using System.Text.Json.Serialization;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Workflow;

namespace PenguinTools.Application;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NoteDiagnosticTarget))]
[JsonSerializable(typeof(NotePairDiagnosticTarget))]
[JsonSerializable(typeof(ChartDiagnosticTarget))]
[JsonSerializable(typeof(ChartDiagnosticTarget[]))]
[JsonSerializable(typeof(TickPosition))]
[JsonSerializable(typeof(ExEffect))]
[JsonSerializable(typeof(Joint))]
[JsonSerializable(typeof(AirDirection))]
[JsonSerializable(typeof(Color))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(CommandDiagnosticTarget))]
internal partial class DiagnosticTargetJsonContext : JsonSerializerContext;
