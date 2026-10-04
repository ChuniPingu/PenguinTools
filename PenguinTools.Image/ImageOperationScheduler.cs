namespace PenguinTools.Image;

internal sealed class ImageOperationScheduler(int concurrency)
{
    internal static int DefaultConcurrency { get; } = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
    internal static ImageOperationScheduler Shared { get; } = new(DefaultConcurrency);
    private readonly SemaphoreSlim _slots = new(concurrency);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await _slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // libvips evaluation is synchronous. Run it on a bounded worker, not the calling UI thread.
            return await Task.Run(() => action(ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }
}
