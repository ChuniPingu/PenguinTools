using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PenguinTools.Media;

/// <summary>Starts native tools and captures their output while preserving cancellation.</summary>
public static class NativeProcess
{
    public static ProcessStartInfo CreateStartInfo(string executablePath, IEnumerable<string> arguments,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(executablePath);
        Configure(startInfo);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    public static async Task<ProcessCommandResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(startInfo);
        Configure(startInfo);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ProcessCommandResult(startInfo, (int)InterExitCode.Failure, string.Empty,
                    $"Executable did not start: {startInfo.FileName}");
            }
        }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested();
            return new ProcessCommandResult(startInfo, (int)InterExitCode.Failure, string.Empty,
                $"Could not start executable '{startInfo.FileName}': {ex.Message}");
        }

        // Uncancelled reads drain both pipes after a cancelled child has been terminated.
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await StopAsync(process).ConfigureAwait(false);
            try
            {
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A failed pipe read is reported below; both tasks have been observed.
            }
            ct.ThrowIfCancellationRequested();
            return new ProcessCommandResult(startInfo, (int)InterExitCode.Failure,
                stdout.IsCompletedSuccessfully ? stdout.Result : string.Empty,
                string.Join(Environment.NewLine, stderr.IsCompletedSuccessfully ? stderr.Result : string.Empty, ex.Message));
        }

        ct.ThrowIfCancellationRequested();
        return new ProcessCommandResult(startInfo, process.ExitCode, await stdout, await stderr);
    }

    private static void Configure(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception && process.HasExited)
        {
            // The child can exit between HasExited and Kill.
        }
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
