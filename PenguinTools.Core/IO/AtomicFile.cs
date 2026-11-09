namespace PenguinTools.Core.IO;

public static class AtomicFile
{
    public static async Task WriteAsync(
        string path,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeAsync);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Path must include a directory.", nameof(path));
        }

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await writeAsync(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            await ReplaceAsync(tempPath, fullPath, cancellationToken);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task ReplaceAsync(string tempPath, string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
                return;
            }
            catch (IOException ex) when (OperatingSystem.IsWindows()
                                       && (ex.HResult & 0xffff) is 32 or 33 or 1175
                                       && attempt < 15)
            {
                // On error 1175, both files retain their original names. Allow
                // temporary failures to clear, with at most 750 ms of delay.
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
