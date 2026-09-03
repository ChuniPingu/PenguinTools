using System.Diagnostics;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Infrastructure;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Infrastructure;

public sealed class MuaMediaToolTests
{
    [Fact]
    public void CalculateGainDb_UsesGameTarget_WhenItFitsPeakCeiling()
    {
        var stats = new FfmpegLoudnessStats(-6.21, 2.11, 8.3);

        var gain = MuaMediaTool.CalculateGainDb(stats);

        Assert.InRange(gain, -2.291, -2.289);
    }

    [Fact]
    public void CalculateGainDb_LimitsGain_WhenPeakCeilingWouldBeExceeded()
    {
        var stats = new FfmpegLoudnessStats(-12.0, -0.1, 4.0);

        var gain = MuaMediaTool.CalculateGainDb(stats);

        Assert.InRange(gain, 0.099, 0.101);
    }

    [Fact]
    public void TryReadLoudnessStats_AcceptsFfmpegStringNumbers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"penguintools-loudness-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                                    {
                                      "input_i": "-8.50",
                                      "input_tp": "0.86",
                                      "input_lra": "4.15"
                                    }
                                    """);

            var success = MuaMediaTool.TryReadLoudnessStats(path, out var stats, out var error);

            Assert.True(success, error);
            Assert.Equal(-8.5, stats.InputIntegratedLufs);
            Assert.Equal(0.86, stats.InputTruePeakDbtp);
            Assert.Equal(4.15, stats.InputLoudnessRangeLu);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CheckAudioValidAsync_ReturnsFailure_WhenExecutableIsMissing()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "penguintools-mua-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var tool = new MuaMediaTool(workDir);
            var result = await tool.CheckAudioValidAsync(
                Path.Combine(workDir, "missing.wav"),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(InterExitCode.Failure, result.ExitCode);
            Assert.Equal(string.Empty, result.StandardError);
        }
        finally
        {
            Directory.Delete(workDir, true);
        }
    }

    [Fact]
    public void ThrowIfFailed_UsesProvidedMessageKey()
    {
        var result = new ProcessCommandResult(
            new ProcessStartInfo { FileName = "ffmpeg.exe" },
            (int)InterExitCode.Failure,
            string.Empty,
            "native decoder error");

        var exception = Assert.Throws<DiagnosticException>(() => result.ThrowIfFailed(MsgKeys.Error_Invalid_audio));
        Assert.Equal(MsgKeys.Error_Invalid_audio, exception.Descriptor.Key);
    }
}
