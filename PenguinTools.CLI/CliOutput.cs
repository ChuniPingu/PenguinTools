using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.CLI;

internal static class CliExitCodes
{
    internal const int Success = 0;
    internal const int Failure = 1;
    internal const int SyntaxError = 2;
    internal const int Cancelled = 130;
}

internal sealed record CliProgressEvent(
    string Type,
    string Operation,
    string? Item = null,
    string? Label = null,
    int? Completed = null,
    int? Total = null,
    double? Percent = null);

internal sealed record CliResponse(
    string Type,
    int SchemaVersion,
    string Operation,
    bool Success,
    int ExitCode,
    MessageDescriptor? Message,
    JsonElement? Data,
    CliDiagnosticPayload[] Diagnostics);

internal sealed record CliDiagnosticPayload(
    string Severity,
    MessageDescriptor Message,
    string? Path = null,
    int? Line = null,
    int? Time = null,
    TickPosition? TimePosition = null);

internal static class CliOutput
{
    internal const string ProgressType = "progress";
    internal const string ResultType = "result";
    private const int SchemaVersion = 3;

    internal static void WriteProgress(string operation, ProgressReport report)
    {
        var payload = new CliProgressEvent(
            ProgressType,
            operation,
            report.Item,
            report.Label,
            report.Completed,
            report.Total,
            report.Percent);
        Console.Out.WriteLine(SerializeProgress(payload));
    }

    internal static void Write<T>(string operation, OperationResult<T> result, MessageDescriptor? message,
        JsonTypeInfo<T> typeInfo, int exitCode)
    {
        JsonElement? element = result.Value is null
            ? null
            : JsonSerializer.SerializeToElement(result.Value, typeInfo);
        WriteJson(new CliResponse(ResultType, SchemaVersion, operation, result.Succeeded, exitCode,
            message is null ? null : CliDiagnostics.SanitizeMessage(message), element,
            CliDiagnostics.ToPayload(result.Diagnostics)));
    }

    internal static void WriteFailure(string operation, MessageDescriptor message, int exitCode)
    {
        var diagnostics = CliDiagnostics.SnapshotFromMessage(message);
        WriteJson(new CliResponse(ResultType, SchemaVersion, operation, false, exitCode,
            CliDiagnostics.SanitizeMessage(message), null,
            CliDiagnostics.ToPayload(diagnostics)));
    }

    internal static void WriteParseErrors(IEnumerable<string> errors)
    {
        var detail = string.Join("; ", errors);
        if (string.IsNullOrWhiteSpace(detail))
            detail = "unknown error";
        WriteFailure("parse", Msg.Create(MsgKeys.Cli_Msg_command_line_parsing_failed, detail),
            CliExitCodes.SyntaxError);
    }

    internal static string Serialize(CliResponse response)
    {
        return JsonSerializer.Serialize(response, CliJsonSerializerContext.Default.CliResponse);
    }

    internal static string SerializeProgress(CliProgressEvent payload)
    {
        return JsonSerializer.Serialize(payload, CliJsonSerializerContext.Default.CliProgressEvent);
    }

    private static void WriteJson(CliResponse response)
    {
        Console.Out.WriteLine(Serialize(response));
    }
}