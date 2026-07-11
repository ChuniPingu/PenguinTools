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
            new ProcessStartInfo { FileName = "mua_wav.exe" },
            (int)InterExitCode.Failure,
            string.Empty,
            "native decoder error");

        var exception = Assert.Throws<DiagnosticException>(() => result.ThrowIfFailed(MsgKeys.Error_Invalid_audio));
        Assert.Equal(MsgKeys.Error_Invalid_audio, exception.Descriptor.Key);
    }
}
