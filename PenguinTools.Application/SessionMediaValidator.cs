using System.Collections.Concurrent;
using System.Security.Cryptography;
using PenguinTools.Media;

namespace PenguinTools.Application;

/// <summary>Caches successful validation by content for one application's fixed media services.</summary>
internal sealed class SessionMediaValidator(IMediaValidator inner) : IMediaValidator, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<(string Kind, string Path), string> _validated = [];
    private readonly ConcurrentBag<Task> _pending = [];
    private readonly Dictionary<string, string> _observed = new(StringComparer.Ordinal);
    private bool _allValid = true;

    public void Dispose() => _gate.Dispose();

    internal void BeginRequest(bool invalidate = false)
    {
        if (invalidate)
        {
            _validated.Clear();
        }
        _observed.Clear();
        _allValid = true;
    }

    internal IReadOnlyDictionary<string, string>? CaptureDependencies() =>
        _allValid ? new Dictionary<string, string>(_observed, StringComparer.Ordinal) : null;

    internal static async Task<bool> DependenciesMatchAsync(IReadOnlyDictionary<string, string> dependencies, CancellationToken ct)
    {
        foreach (var (path, expected) in dependencies)
        {
            if (await TryHashAsync(path, ct) != expected)
            {
                return false;
            }
        }
        return true;
    }

    public Task<MediaValidationResult> CheckAudioValidAsync(string source, CancellationToken ct = default) =>
        Track(CheckAsync("audio", source, inner.CheckAudioValidAsync, ct));

    public Task<MediaValidationResult> CheckImageValidAsync(string source, CancellationToken ct = default) =>
        Track(CheckAsync("image", source, inner.CheckImageValidAsync, ct));

    private Task<MediaValidationResult> Track(Task<MediaValidationResult> task)
    {
        _pending.Add(task);
        return task;
    }

    internal async Task CompletePendingAsync()
    {
        // A parser can reject a header after queuing media checks. Keep those checks
        // within the request lifetime; their diagnostics belong to the parser.
        while (_pending.TryTake(out var task))
        {
            try
            {
                await task;
            }
            catch (Exception) when (task.IsFaulted || task.IsCanceled) { }
        }
    }

    private async Task<MediaValidationResult> CheckAsync(string kind, string source,
        Func<string, CancellationToken, Task<MediaValidationResult>> validate, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var hash = await TryHashAsync(source, ct);
            var key = (kind, source);
            if (hash is not null && _validated.TryGetValue(key, out var previous) && previous == hash)
            {
                _observed[source] = hash;
                return MediaValidationResult.Valid;
            }
            _validated.Remove(key);
            var result = await validate(source, ct);
            // A file changed during validation cannot certify the bytes of a later request.
            if (result.IsSuccess && hash is not null && hash == await TryHashAsync(source, ct))
            {
                if (_validated.Count >= 128)
                {
                    _validated.Clear();
                }
                _validated[key] = hash;
                _observed[source] = hash;
            }
            else
            {
                _allValid = false;
            }
            return result;
        }
        catch
        {
            _allValid = false;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<string?> TryHashAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
