using System.Collections.Concurrent;
using System.Security.Cryptography;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Workflow;

/// <summary>Reuses C2S only when the caller has verified identical chart input and conversion dependencies.</summary>
public sealed class OptionChartConversionCache
{
    private sealed record CachedChart(string Path, string Hash, DiagnosticSnapshot Diagnostics);
    private readonly ConcurrentDictionary<string, CachedChart> _charts = new(StringComparer.OrdinalIgnoreCase);

    public OptionChartConversionCache Fork()
    {
        var copy = new OptionChartConversionCache();
        foreach (var item in _charts)
        {
            copy._charts[item.Key] = item.Value;
        }
        return copy;
    }

    internal async Task<DiagnosticSnapshot?> TryCopyAsync(string input, string output, CancellationToken ct)
    {
        if (!_charts.TryGetValue(input, out var chart) || !File.Exists(chart.Path))
        {
            return null;
        }
        await using var source = File.OpenRead(chart.Path);
        if (Convert.ToHexString(await SHA256.HashDataAsync(source, ct)) != chart.Hash)
        {
            return null;
        }
        source.Position = 0;
        await using var destination = File.Create(output);
        await source.CopyToAsync(destination, ct);
        _charts[input] = chart with { Path = output };
        return chart.Diagnostics;
    }

    internal async Task StoreAsync(string input, string output, DiagnosticSnapshot diagnostics, CancellationToken ct)
    {
        await using var stream = File.OpenRead(output);
        _charts[input] = new(output, Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)), diagnostics);
    }
}
