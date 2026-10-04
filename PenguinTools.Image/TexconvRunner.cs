using System.Diagnostics;

namespace PenguinTools.Image;

internal interface ITexconvRunner
{
    Task RunAsync(IReadOnlyList<string> arguments, CancellationToken ct);
}

internal sealed class TexconvRunner(string executablePath) : ITexconvRunner
{
    public async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var command = $"{executablePath} {string.Join(' ', arguments)}";
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new TexconvException(command, -1, string.Empty, ex.Message);
        }

        // Drain without cancellation so both pipes can finish after the child is killed.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new TexconvException(command, process.ExitCode, await stdout, await stderr);
    }
}
