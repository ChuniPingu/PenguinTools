namespace PenguinTools.Image;

internal static class StagedOutputs
{
    // Prepare every output before committing any of them, including validation of destination directories.
    public static async Task WriteAsync(IReadOnlyList<(string Path, ReadOnlyMemory<byte> Data)> outputs, CancellationToken ct)
    {
        var destinations = outputs.Select(output => Path.GetFullPath(output.Path)).ToArray();
        if (destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Length)
        {
            throw new ArgumentException("Image output paths must be different.");
        }

        var staged = new List<(string Temporary, string Destination)>();
        var committed = new List<(string Destination, string? Backup)>();
        try
        {
            for (var i = 0; i < outputs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var destination = destinations[i];
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (Directory.Exists(destination))
                {
                    throw new IOException($"Output is a directory: {destination}");
                }

                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((temporary, destination));
                await using var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, FileOptions.Asynchronous);
                await file.WriteAsync(outputs[i].Data, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            // Do not introduce a cancellation point between commits of one stage's related files.
            foreach (var (temporary, destination) in staged)
            {
                string? backup = null;
                if (File.Exists(destination))
                {
                    backup = destination + "." + Guid.NewGuid().ToString("N") + ".bak";
                    File.Replace(temporary, destination, backup);
                }
                else
                {
                    File.Move(temporary, destination);
                }

                committed.Add((destination, backup));
            }
        }
        catch (Exception failure)
        {
            List<Exception> errors = [failure];
            for (var i = committed.Count - 1; i >= 0; i--)
            {
                var (destination, backup) = committed[i];
                try
                {
                    if (backup is null)
                    {
                        File.Delete(destination);
                    }
                    else
                    {
                        File.Replace(backup, destination, null);
                    }
                }
                catch (Exception rollbackFailure)
                {
                    // Retain a backup if restoring it fails; never discard the user's previous file.
                    errors.Add(new IOException($"Could not restore {destination}; the previous file remains at {backup}.", rollbackFailure));
                }
            }
            if (errors.Count > 1)
            {
                throw new AggregateException("Image output commit and rollback failed.", errors);
            }

            throw;
        }
        finally
        {
            foreach (var (temporary, _) in staged)
            {
                File.Delete(temporary);
            }
        }
        foreach (var (_, backup) in committed)
        {
            if (backup is not null)
            {
                File.Delete(backup);
            }
        }
    }
}
