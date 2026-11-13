using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PenguinTools.Workflow;

public sealed class OptionConversionCache
{
    public const int AudioCurrentVersion = 4;
    public const int ImageCurrentVersion = 7;

    public OptionConversionCachePartition Audio { get; set; } = CreatePartition(AudioCurrentVersion);
    public OptionConversionCachePartition Image { get; set; } = CreatePartition(ImageCurrentVersion);

    private readonly ConcurrentDictionary<string, string> _verifiedOutputs = new(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, string>? VerifiedInputs { get; private set; }

    /// <summary>Supplies request-local input hashes; revalidate these dependencies before publishing the build.</summary>
    public void SetVerifiedInputs(IReadOnlyDictionary<string, string> inputs)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, hash) in inputs)
        {
            normalized[Path.GetFullPath(path)] = hash;
        }
        VerifiedInputs = normalized;
    }

    /// <summary>Returns the digest verified while writing a private candidate that the caller has not modified.</summary>
    public string? VerifiedOutputHash(string path) => _verifiedOutputs.GetValueOrDefault(Path.GetFullPath(path));

    internal void RememberOutputs(IReadOnlyList<OptionConversionArtifact> outputs, IReadOnlyDictionary<string, string> hashes)
    {
        foreach (var output in outputs)
        {
            _verifiedOutputs[Path.GetFullPath(output.Path)] = hashes[output.Name];
        }
    }

    internal string? SourceDirectory { get; private init; }
    internal string? TargetDirectory { get; private init; }

    /// <summary>Copies cache state for an independent output tree, reusing verified artifacts from a completed build.</summary>
    public OptionConversionCache ForSnapshot(string sourceDirectory, string targetDirectory) => new()
    {
        Audio = ClonePartition(Audio),
        Image = ClonePartition(Image),
        SourceDirectory = Path.GetFullPath(sourceDirectory),
        TargetDirectory = Path.GetFullPath(targetDirectory)
    };

    private static OptionConversionCachePartition ClonePartition(OptionConversionCachePartition partition) => new()
    {
        Version = partition.Version,
        Entries = partition.Entries.ToDictionary(pair => pair.Key, pair => new OptionConversionCacheEntry
        {
            RecipeHash = pair.Value.RecipeHash,
            Inputs = new(pair.Value.Inputs, StringComparer.OrdinalIgnoreCase),
            Outputs = new(pair.Value.Outputs, StringComparer.OrdinalIgnoreCase)
        }, StringComparer.OrdinalIgnoreCase)
    };

    private static OptionConversionCachePartition CreatePartition(int version)
    {
        return new OptionConversionCachePartition { Version = version };
    }

    public OptionConversionCacheEntry? GetEntry(string key)
    {
        return TryResolve(key, out var partition, out var currentVersion)
            ? partition.TryGetEntry(currentVersion, key)
            : null;
    }

    public void SetEntry(string key, OptionConversionCacheEntry entry)
    {
        if (!TryResolve(key, out var partition, out var currentVersion))
        {
            throw new ArgumentException($"Unsupported conversion cache key: {key}", nameof(key));
        }

        partition.SetEntry(currentVersion, key, entry);
    }

    private bool TryResolve(string key, out OptionConversionCachePartition partition, out int currentVersion)
    {
        if (key.StartsWith("audio:", StringComparison.OrdinalIgnoreCase))
        {
            partition = Audio;
            currentVersion = AudioCurrentVersion;
            return true;
        }

        if (key.StartsWith("jacket:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("stage:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("wall:", StringComparison.OrdinalIgnoreCase))
        {
            partition = Image;
            currentVersion = ImageCurrentVersion;
            return true;
        }

        partition = null!;
        currentVersion = 0;
        return false;
    }
}

public sealed class OptionConversionCachePartition
{
    private readonly Lock _sync = new();

    public int Version { get; set; }

    public Dictionary<string, OptionConversionCacheEntry> Entries
    {
        get;
        set => field = value is null
            ? new Dictionary<string, OptionConversionCacheEntry>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, OptionConversionCacheEntry>(value, StringComparer.OrdinalIgnoreCase);
    } = new(StringComparer.OrdinalIgnoreCase);

    internal OptionConversionCacheEntry? TryGetEntry(int currentVersion, string key)
    {
        lock (_sync)
        {
            return Version == currentVersion && Entries.TryGetValue(key, out var entry) ? entry : null;
        }
    }

    internal void SetEntry(int currentVersion, string key, OptionConversionCacheEntry entry)
    {
        lock (_sync)
        {
            if (Version != currentVersion)
            {
                Version = currentVersion;
                Entries.Clear();
            }

            Entries[key] = entry;
        }
    }
}

public sealed class OptionConversionCacheEntry
{
    public string RecipeHash { get; set; } = string.Empty;

    public Dictionary<string, string> Inputs
    {
        get;
        set => field = value is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
    } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Outputs
    {
        get;
        set => field = value is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
    } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record OptionConversionCacheState(
    string RecipeHash,
    IReadOnlyDictionary<string, string> Inputs);

internal sealed record OptionConversionArtifact(string Name, string Path);

internal static class OptionConversionCacheValidator
{
    public static async Task<OptionConversionCacheState?> CreateStateAsync(
        IReadOnlyDictionary<string, string?> recipeFields,
        IReadOnlyDictionary<string, string> inputFiles,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? verifiedInputs = null)
    {
        var inputHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, path) in inputFiles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!File.Exists(path))
            {
                return null;
            }

            inputHashes[name] = verifiedInputs is not null && verifiedInputs.TryGetValue(Path.GetFullPath(path), out var hash)
                ? hash : await HashFileAsync(path, ct);
        }

        var recipeHash = HashRecipe(recipeFields, RecipeCacheVersion(recipeFields));
        return new OptionConversionCacheState(recipeHash, inputHashes);
    }

    public static async Task<bool> IsHitAsync(
        OptionConversionCache? cache,
        string key,
        OptionConversionCacheState? state,
        IReadOnlyList<OptionConversionArtifact> outputs,
        CancellationToken ct)
    {
        if (cache is null || state is null)
        {
            return false;
        }

        var entry = cache.GetEntry(key);
        if (entry is null)
        {
            return false;
        }

        if (!string.Equals(entry.RecipeHash, state.RecipeHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Matches(entry.Inputs, state.Inputs))
        {
            return false;
        }

        var outputHashes = await HashExistingOutputsAsync(outputs, ct);
        if (outputHashes is not null && Matches(entry.Outputs, outputHashes))
        {
            cache.RememberOutputs(outputs, outputHashes);
            return true;
        }

        if (cache.SourceDirectory is null || cache.TargetDirectory is null)
        {
            return false;
        }

        var sources = new List<OptionConversionArtifact>();
        foreach (var output in outputs)
        {
            var relative = Path.GetRelativePath(cache.TargetDirectory, output.Path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }
            sources.Add(new(output.Name, Path.Combine(cache.SourceDirectory, relative)));
        }
        if (entry.Outputs.Count != outputs.Count || outputs.Any(output => !entry.Outputs.ContainsKey(output.Name)) ||
            sources.Any(source => !File.Exists(source.Path)))
        {
            return false;
        }

        var copied = new List<(string Temporary, string Destination)>();
        var complete = false;
        try
        {
            var copiedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < outputs.Count; index++)
            {
                var output = outputs[index];
                Directory.CreateDirectory(Path.GetDirectoryName(output.Path)!);
                var temporary = output.Path + ".cache-" + Guid.NewGuid().ToString("N");
                copied.Add((temporary, output.Path));
                var hash = await CopyAndHashAsync(sources[index].Path, temporary, ct);
                if (!string.Equals(hash, entry.Outputs[output.Name], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                copiedHashes[output.Name] = hash;
            }
            foreach (var (temporary, destination) in copied)
            {
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
            }
            cache.RememberOutputs(outputs, copiedHashes);
            complete = true;
            return true;
        }
        finally
        {
            if (!complete)
            {
                foreach (var (temporary, _) in copied)
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    public static async Task StoreAsync(
        OptionConversionCache? cache,
        string key,
        OptionConversionCacheState? state,
        IReadOnlyList<OptionConversionArtifact> outputs,
        CancellationToken ct)
    {
        if (cache is null || state is null)
        {
            return;
        }

        var outputHashes = await HashExistingOutputsAsync(outputs, ct);
        if (outputHashes is null)
        {
            return;
        }

        cache.RememberOutputs(outputs, outputHashes);
        cache.SetEntry(key, new OptionConversionCacheEntry
        {
            RecipeHash = state.RecipeHash,
            Inputs = new Dictionary<string, string>(state.Inputs, StringComparer.OrdinalIgnoreCase),
            Outputs = outputHashes
        });
    }

    public static string FormatInvariant<T>(T value) where T : IFormattable
    {
        return value.ToString(null, CultureInfo.InvariantCulture);
    }

    private static int RecipeCacheVersion(IReadOnlyDictionary<string, string?> recipeFields)
    {
        return recipeFields.TryGetValue("kind", out var kind)
            ? kind switch
            {
                "audio" => OptionConversionCache.AudioCurrentVersion,
                "jacket" or "stage" or "wall" => OptionConversionCache.ImageCurrentVersion,
                _ => throw new ArgumentException($"Unsupported recipe kind: {kind}", nameof(recipeFields))
            }
            : throw new ArgumentException("Recipe kind is required.", nameof(recipeFields));
    }

    private static async Task<Dictionary<string, string>?> HashExistingOutputsAsync(
        IReadOnlyList<OptionConversionArtifact> outputs,
        CancellationToken ct)
    {
        var outputHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var output in outputs.OrderBy(o => o.Name, StringComparer.Ordinal))
        {
            if (!File.Exists(output.Path))
            {
                return null;
            }

            outputHashes[output.Name] = await HashFileAsync(output.Path, ct);
        }

        return outputHashes;
    }

    private static bool Matches(
        Dictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        foreach (var (key, value) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue))
            {
                return false;
            }

            if (!string.Equals(value, actualValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<string> CopyAndHashAsync(string sourcePath, string targetPath, CancellationToken ct)
    {
        await using var source = File.OpenRead(sourcePath);
        await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(), ct)) != 0)
            {
                hash.AppendData(buffer.AsSpan(0, count));
                await target.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static string HashRecipe(IReadOnlyDictionary<string, string?> fields, int cacheVersion)
    {
        var sb = new StringBuilder();
        sb.Append(nameof(OptionConversionCache)).Append('=').Append(cacheVersion).Append('\n');

        foreach (var (key, value) in fields.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(key.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(key).Append('=');
            var normalized = value ?? string.Empty;
            sb.Append(normalized.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(normalized);
            sb.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
