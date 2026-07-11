using System.Text.Json.Serialization.Metadata;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class CliCommandRunner
{
    internal static async Task<int> RunAsync<T>(
        string operation,
        Func<IPenguinToolsApplication, CancellationToken, Task<OperationResult<T>>> action,
        Func<T, MessageDescriptor> successMessage,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            using var application = PenguinToolsApplication.CreateDefault(CreateApplicationOptions());
            var result = await action(application, cancellationToken);
            return WriteResult(operation, result, successMessage, typeInfo);
        }
        catch (OperationCanceledException)
        {
            CliOutput.WriteFailure(operation, Msg.Key(MsgKeys.Cli_Msg_operation_cancelled), CliExitCodes.Cancelled);
            return CliExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            CliOutput.WriteFailure(operation, Msg.Unhandled(ex.Message), CliExitCodes.Failure);
            return CliExitCodes.Failure;
        }
    }

    internal static async Task<int> RunWithProgressAsync<T>(
        string operation,
        Func<IPenguinToolsApplication, IProgress<ProgressReport>, CancellationToken, Task<OperationResult<T>>> action,
        Func<T, MessageDescriptor> successMessage,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            using var application = PenguinToolsApplication.CreateDefault(CreateApplicationOptions());
            var progress = new CliProgressReporter(operation);
            var result = await action(application, progress, cancellationToken);
            return WriteResult(operation, result, successMessage, typeInfo);
        }
        catch (OperationCanceledException)
        {
            CliOutput.WriteFailure(operation, Msg.Key(MsgKeys.Cli_Msg_operation_cancelled), CliExitCodes.Cancelled);
            return CliExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            CliOutput.WriteFailure(operation, Msg.Unhandled(ex.Message), CliExitCodes.Failure);
            return CliExitCodes.Failure;
        }
    }

    private static int WriteResult<T>(
        string operation,
        OperationResult<T> result,
        Func<T, MessageDescriptor> successMessage,
        JsonTypeInfo<T> typeInfo)
    {
        var exitCode = result.Succeeded ? CliExitCodes.Success : CliExitCodes.Failure;
        var message = result.Value is { } value && result.Succeeded ? successMessage(value) : null;
        CliOutput.Write(operation, result, message, typeInfo, exitCode);
        return exitCode;
    }

    private static PenguinToolsApplicationOptions CreateApplicationOptions()
    {
        return new PenguinToolsApplicationOptions();
    }
}