using System.Security.Cryptography;
using System.Text.Json;
using PenguinTools.Workflow;

namespace PenguinTools.Application;

internal sealed record PersistentMediaArtifact(string Path, string Hash);
internal sealed record PersistentMediaCheckpoint(int Version, string Identity, string Generation,
    OptionConversionCache Cache, IReadOnlyList<PersistentMediaArtifact> Artifacts);

/// <summary>Stores optional media reuse state in a directory exclusively leased by the caller.</summary>
internal sealed class PersistentMediaCache(string directory, string identity)
{
    private const int Version = 1;
    private const string CheckpointName = "checkpoint.json";
    private const string OwnerName = ".penguintools-media";
    private readonly string _directory = Path.GetFullPath(directory);

    internal static PersistentMediaCache? FromRequest(IncrementalBuildRequest request)
    {
        if (request.CacheDirectory is null && request.CacheIdentity is null)
        {
            return null;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CacheIdentity);
        if (!Path.IsPathFullyQualified(request.CacheDirectory))
        {
            throw new ArgumentException("The media cache directory must be absolute.", nameof(request));
        }
        return new(request.CacheDirectory, request.CacheIdentity);
    }

    internal async Task<OptionConversionCache?> RestoreAsync(string target, CancellationToken ct)
    {
        var checkpoint = await TryReadAsync(ct);
        return checkpoint?.Cache.ForSnapshot(Path.Combine(_directory, checkpoint.Generation), target);
    }

    private async Task<PersistentMediaCheckpoint?> TryReadAsync(CancellationToken ct)
    {
        try
        {
            return await ReadAsync(ct);
        }
        catch (Exception ex) when (IsCacheError(ex))
        {
            return null;
        }
    }

    internal async Task StoreAsync(OptionConversionCache cache, string source,
        IReadOnlyList<PersistentMediaArtifact> artifacts, CancellationToken ct)
    {
        string? generation = null;
        string? temporary = null;
        var created = false;
        try
        {
            if (!SafePath(_directory))
            {
                return;
            }
            Directory.CreateDirectory(_directory);
            var preceding = await TryReadAsync(ct);
            if (preceding is not null && SameCache(preceding.Cache, cache) &&
                preceding.Artifacts.SequenceEqual(artifacts) && await ArtifactsMatchAsync(preceding, ct))
            {
                return;
            }

            generation = "g-" + Guid.NewGuid().ToString("N");
            var root = Path.Combine(_directory, generation);
            if (Directory.Exists(root) || File.Exists(root))
            {
                return;
            }
            Directory.CreateDirectory(root);
            created = true;
            await File.WriteAllTextAsync(Path.Combine(root, OwnerName), generation, ct);
            foreach (var artifact in artifacts)
            {
                var destination = Resolve(root, artifact.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = File.OpenRead(Resolve(source, artifact.Path));
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
            }
            var checkpoint = new PersistentMediaCheckpoint(Version, identity, generation, cache, artifacts);
            temporary = Path.Combine(_directory, "c-" + Guid.NewGuid().ToString("N") + ".json");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, ApplicationJsonContext.Default.PersistentMediaCheckpoint, ct);
            }
            ct.ThrowIfCancellationRequested();
            var destinationCheckpoint = Path.Combine(_directory, CheckpointName);
            if (!SafePath(destinationCheckpoint))
            {
                return;
            }
            File.Move(temporary, destinationCheckpoint, overwrite: true);
            temporary = null;
            generation = null;
            if (preceding is not null)
            {
                DeleteGeneration(preceding.Generation, preceding.Artifacts);
            }
        }
        catch (Exception ex) when (IsCacheError(ex)) { }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (temporary is not null)
            {
                TryDeleteFile(temporary);
            }
            if (created && generation is not null)
            {
                DeleteGeneration(generation, artifacts);
            }
        }
    }

    private async Task<PersistentMediaCheckpoint?> ReadAsync(CancellationToken ct)
    {
        var path = Path.Combine(_directory, CheckpointName);
        if (!SafePath(path) || !File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024)
        {
            return null;
        }
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync(stream, ApplicationJsonContext.Default.PersistentMediaCheckpoint, ct);
        if (value is null || value.Version != Version || value.Identity != identity || !ValidGeneration(value.Generation) ||
            value.Cache?.Audio?.Entries is null || value.Cache.Image?.Entries is null || value.Artifacts is null ||
            value.Artifacts.Count > 10000 || value.Artifacts.Any(item => item is null || !ValidHash(item.Hash)))
        {
            return null;
        }
        var root = Path.Combine(_directory, value.Generation);
        if (!SafeTree(root) || !HasOwner(root, value.Generation))
        {
            return null;
        }
        foreach (var artifact in value.Artifacts)
        {
            _ = Resolve(root, artifact.Path);
        }
        foreach (var entry in value.Cache.Audio.Entries.Values.Concat(value.Cache.Image.Entries.Values))
        {
            if (entry is null || !ValidHash(entry.RecipeHash) || entry.Inputs is null || entry.Outputs is null ||
                entry.Inputs.Values.Concat(entry.Outputs.Values).Any(hash => !ValidHash(hash)))
            {
                return null;
            }
        }
        return value;
    }

    private async Task<bool> ArtifactsMatchAsync(PersistentMediaCheckpoint checkpoint, CancellationToken ct)
    {
        foreach (var artifact in checkpoint.Artifacts)
        {
            var path = Resolve(Path.Combine(_directory, checkpoint.Generation), artifact.Path);
            if (!File.Exists(path))
            {
                return false;
            }
            await using var stream = File.OpenRead(path);
            if (Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)) != artifact.Hash)
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameCache(OptionConversionCache left, OptionConversionCache right) =>
        JsonSerializer.Serialize(left, ApplicationJsonContext.Default.OptionConversionCache) ==
        JsonSerializer.Serialize(right, ApplicationJsonContext.Default.OptionConversionCache);

    private void DeleteGeneration(string generation, IReadOnlyList<PersistentMediaArtifact> artifacts)
    {
        try
        {
            if (!ValidGeneration(generation))
            {
                return;
            }
            var root = Path.Combine(_directory, generation);
            if (!SafeTree(root) || !HasOwner(root, generation))
            {
                return;
            }
            var expected = artifacts.Select(item => Resolve(root, item.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            expected.Add(Path.Combine(root, OwnerName));
            if (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(file => !expected.Contains(file)))
            {
                return;
            }
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (IsCacheError(ex)) { }
    }

    private static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Split(['/', '\\']).Any(part => part is "" or "." or ".." || part.Contains(':')))
        {
            throw new InvalidDataException("Invalid cached artifact path.");
        }
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !SafePath(path))
        {
            throw new InvalidDataException("Cached artifact is outside its generation.");
        }
        return path;
    }

    private static bool SafeTree(string root)
    {
        if (!Directory.Exists(root) || !SafePath(root))
        {
            return false;
        }
        var pending = new Stack<string>();
        pending.Push(root);
        var count = 0;
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if (++count > 10000 || (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
        return true;
    }

    private static bool SafePath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return true;
    }

    private static bool HasOwner(string root, string generation) =>
        File.Exists(Path.Combine(root, OwnerName)) && new FileInfo(Path.Combine(root, OwnerName)).Length == generation.Length &&
        File.ReadAllText(Path.Combine(root, OwnerName)) == generation;

    private static bool ValidGeneration(string? value) => value is { Length: 34 } &&
        value.StartsWith("g-", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(2), "N", out _);

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    private static bool IsCacheError(Exception exception) => exception is IOException or InvalidDataException or
        UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (SafePath(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsCacheError(ex)) { }
    }
}
