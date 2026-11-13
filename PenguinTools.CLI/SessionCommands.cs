using System.CommandLine;
using System.Text;
using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.CLI;

internal sealed record SessionGreeting(bool Ready = true, int Protocol = 1)
{
    public string[] Capabilities { get; } = ["persistentMediaCache"];
}
internal sealed record SessionRequest(long Id, string Method, string? DocumentId = null,
    string? BaseDirectory = null, string? Format = null, string? Content = null,
    string? InputPath = null, string? OutputDirectory = null, OptionBuildOverrides? Overrides = null,
    string? CacheDirectory = null, string? CacheIdentity = null);
internal sealed record SessionResponse(long? Id, bool Success, IncrementalBuildResult? Result,
    CliDiagnosticPayload[] Diagnostics);

internal static class SessionCommands
{
    internal static Command BuildSessionCommand()
    {
        var command = new Command("session", "Build incremental option snapshots over a persistent JSONL stream.");
        command.SetAction(async (parseResult, ct) =>
        {
            try
            {
                using var application = PenguinToolsApplication.CreateDefault(GlobalCliOptions.CreateApplicationOptions(parseResult));
                using var session = application.CreateBuildSession();
                using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true));
                await using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                return await RunAsync(session, input, output, ct);
            }
            catch (OperationCanceledException)
            {
                return CliExitCodes.Cancelled;
            }
            catch (Exception ex)
            {
                CliOutput.WriteFailure("session", ex is DiagnosticException diagnostic ? diagnostic.Descriptor : Msg.Unhandled(ex.Message),
                    CliExitCodes.Failure);
                return CliExitCodes.Failure;
            }
        });
        return command;
    }

    internal static async Task<int> RunAsync(IncrementalBuildSession session, TextReader input, TextWriter output, CancellationToken ct)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(new SessionGreeting(), CliJsonSerializerContext.Default.SessionGreeting).AsMemory(), ct);
        await output.FlushAsync(ct);
        while (await input.ReadLineAsync(ct) is { } line)
        {
            long? id = null;
            SessionResponse response;
            var close = false;
            try
            {
                if (line.Length > 32 * 1024 * 1024)
                {
                    throw new JsonException("Session request exceeds 32 Mi characters.");
                }
                var request = JsonSerializer.Deserialize(line, CliJsonSerializerContext.Default.SessionRequest)
                    ?? throw new JsonException("Session request must be an object.");
                id = request.Id;
                if (request.Method == "close")
                {
                    close = true;
                    response = new(id, true, null, []);
                }
                else if (request.Method == "prepare")
                {
                    if (!Enum.TryParse<ChartFormat>(request.Format, true, out var format))
                    {
                        throw new JsonException("Chart format must be ugc, mgxc, or sus.");
                    }
                    var result = await session.PrepareAsync(new IncrementalBuildRequest(
                        request.DocumentId ?? "", request.BaseDirectory ?? "", format, request.OutputDirectory ?? "",
                        request.Content, request.InputPath, request.Overrides, request.CacheDirectory, request.CacheIdentity), ct);
                    response = new(id, result.Succeeded, result.Value, CliDiagnostics.ToPayload(result.Diagnostics));
                }
                else
                {
                    throw new JsonException("Session method must be prepare or close.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var diagnostics = new DiagnosticCollector();
                diagnostics.Report(ex);
                response = new(id, false, null, CliDiagnostics.ToPayload(diagnostics.Diagnostics));
            }
            await output.WriteLineAsync(JsonSerializer.Serialize(response, CliJsonSerializerContext.Default.SessionResponse).AsMemory(), ct);
            await output.FlushAsync(ct);
            if (close)
            {
                break;
            }
        }
        return CliExitCodes.Success;
    }
}
