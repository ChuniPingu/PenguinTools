using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PenguinTools.Workflow;

public sealed class OptionConversionCache
{
    public const int AudioCurrentVersion = 3;
    public const int ImageCurrentVersion = 3;

    public OptionConversionCachePartition Audio { get; set; } = CreatePartition(AudioCurrentVersion);
    public OptionConversionCachePartition Image { get; set; } = CreatePartition(ImageCurrentVersion);

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
            throw new ArgumentException($"Unsupported conversion cache key: {key}", nameof(key));

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
            || key.StartsWith("stage:", StringComparison.OrdinalIgnoreCase))
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
        CancellationToken ct)
    {
        var inputHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, path) in inputFiles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!File.Exists(path)) return null;

            inputHashes[name] = await HashFileAsync(path, ct);
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
        if (cache is null || state is null) return false;
        var entry = cache.GetEntry(key);
        if (entry is null) return false;
        if (!string.Equals(entry.RecipeHash, state.RecipeHash, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Matches(entry.Inputs, state.Inputs)) return false;

        var outputHashes = await HashExistingOutputsAsync(outputs, ct);
        return outputHashes is not null && Matches(entry.Outputs, outputHashes);
    }

    public static async Task StoreAsync(
        OptionConversionCache? cache,
        string key,
        OptionConversionCacheState? state,
        IReadOnlyList<OptionConversionArtifact> outputs,
        CancellationToken ct)
    {
        if (cache is null || state is null) return;

        var outputHashes = await HashExistingOutputsAsync(outputs, ct);
        if (outputHashes is null) return;

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
                "jacket" or "stage" => OptionConversionCache.ImageCurrentVersion,
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
            if (!File.Exists(output.Path)) return null;

            outputHashes[output.Name] = await HashFileAsync(output.Path, ct);
        }

        return outputHashes;
    }

    private static bool Matches(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        if (expected.Count != actual.Count) return false;

        foreach (var (key, value) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue)) return false;
            if (!string.Equals(value, actualValue, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
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
