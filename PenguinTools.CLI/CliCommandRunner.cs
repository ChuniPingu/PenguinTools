using System.CommandLine;
using System.Text.Json.Serialization.Metadata;
using PenguinTools.Application;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.CLI;

internal static class CliCommandRunner
{
    internal static async Task<int> RunAsync<T>(
        string operation,
        Func<IPenguinToolsApplication, CancellationToken, Task<OperationResult<T>>> action,
        Func<T, MessageDescriptor?> successMessage,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        ParseResult? parseResult = null)
    {
        try
        {
            using var application = PenguinToolsApplication.CreateDefault(
                GlobalCliOptions.CreateApplicationOptions(parseResult ?? EmptyParseResult()));
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
            CliOutput.WriteFailure(operation, ResolveFailureMessage(ex), CliExitCodes.Failure);
            return CliExitCodes.Failure;
        }
    }

    internal static async Task<int> RunWithProgressAsync<T>(
        string operation,
        Func<IPenguinToolsApplication, IProgress<ProgressReport>?, CancellationToken, Task<OperationResult<T>>> action,
        Func<T, MessageDescriptor?> successMessage,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        bool suppressProgress = false,
        ParseResult? parseResult = null)
    {
        try
        {
            using var application = PenguinToolsApplication.CreateDefault(
                GlobalCliOptions.CreateApplicationOptions(parseResult ?? EmptyParseResult()));
            IProgress<ProgressReport>? progress = suppressProgress ? null : new CliProgressReporter(operation);
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
            CliOutput.WriteFailure(operation, ResolveFailureMessage(ex), CliExitCodes.Failure);
            return CliExitCodes.Failure;
        }
    }

    private static ParseResult EmptyParseResult()
    {
        return RootCommands.BuildRootCommand().Parse([]);
    }

    private static MessageDescriptor ResolveFailureMessage(Exception exception)
    {
        return exception is DiagnosticException diagnosticException
            ? diagnosticException.Descriptor
            : Msg.Unhandled(exception.Message);
    }

    private static int WriteResult<T>(
        string operation,
        OperationResult<T> result,
        Func<T, MessageDescriptor?> successMessage,
        JsonTypeInfo<T> typeInfo)
    {
        var exitCode = result.Succeeded ? CliExitCodes.Success : CliExitCodes.Failure;
        var message = result.Value is { } value && result.Succeeded ? successMessage(value) : null;
        CliOutput.Write(operation, result, message, typeInfo, exitCode);
        return exitCode;
    }
}
