using System.Diagnostics;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Media;

public class ProcessCommandResult
{
    public ProcessCommandResult(ProcessStartInfo startInfo, int exitCode, string stdout, string stderr)
    {
        ExitCode = (InterExitCode)exitCode;
        StandardOutput = stdout.Trim();
        StandardError = stderr.Trim();
        Command = $"{startInfo.FileName} {string.Join(" ", startInfo.ArgumentList)}";
    }

    public InterExitCode ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public string Command { get; }

    public bool IsSuccess => ExitCode is InterExitCode.Success or InterExitCode.NoOperation;
    public bool IsNoOperation => ExitCode == InterExitCode.NoOperation;
    public bool IsFailure => !IsSuccess;

    public void ThrowIfFailed()
    {
        ThrowIfFailed(MsgKeys.Error_Command_failed);
    }

    public void ThrowIfFailed(string messageKey)
    {
        if (!IsFailure) return;

        throw new DiagnosticException(messageKey, this);
    }
}