using System.Diagnostics;
using PenguinTools.Core;
using PenguinTools.Media;

namespace PenguinTools.Tests;

internal sealed class TestMediaTool : IMediaTool
{
    public static readonly TestMediaTool Instance = new();

    public Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset,
        CancellationToken ct = default)
    {
        return Task.FromResult(Ok());
    }

    public Task<ProcessCommandResult> CheckAudioValidAsync(string src, CancellationToken ct = default)
    {
        return Task.FromResult(Ok());
    }

    public Task<ProcessCommandResult> CheckImageValidAsync(string src, CancellationToken ct = default)
    {
        return Task.FromResult(Ok());
    }

    public Task ConvertJacketAsync(string src, string dst, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task ConvertStageAsync(string bg, string stDst, string nfDst, string?[]? fxPaths,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task ExtractDdsAsync(string src, string dst, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task ConvertCriAsync(
        string wav,
        string acb,
        string awb,
        string name,
        long previewStartMs,
        long previewStopMs,
        ulong hcaKey,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    private static ProcessCommandResult Ok()
    {
        return new ProcessCommandResult(new ProcessStartInfo { FileName = "null" }, (int)InterExitCode.Success, "", "");
    }
}