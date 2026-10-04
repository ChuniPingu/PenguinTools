namespace PenguinTools.CRI;

// Codec callbacks run synchronously, so cancellation stops the active codec loop.
internal sealed class CancellationProgress(CancellationToken cancellationToken) : IProgress<double>
{
    public void Report(double value)
    {
        cancellationToken.ThrowIfCancellationRequested();
    }
}
