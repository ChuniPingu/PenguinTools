using System.Diagnostics;
using PenguinTools.Application;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed class ApplicationDiagnosticsTests
{
    [Theory]
    [InlineData("image-tools.exe", 1, "decoder output", "decoder failure")]
    [InlineData("texconv.exe", 7, "encoder output", "encoder failure")]
    public void ImageCommandFailure_PreservesSubprocessDiagnostics(string executable, int exitCode,
        string standardOutput, string standardError)
    {
        var start = NativeProcess.CreateStartInfo(executable, ["--input", "透明 source.png"]);
        var failure = new ProcessCommandResult(start, exitCode, standardOutput, standardError);
        var result = ApplicationDiagnostics.FromException<string>(new DiagnosticException(MsgKeys.Error_Invalid_jk_image, failure));
        var diagnostic = Assert.Single(result.Diagnostics.Diagnostics);
        Assert.Equal(MsgKeys.Error_Invalid_jk_image, diagnostic.Message.Key);
        var target = DiagnosticTargetSerializer.ToJsonElement(diagnostic.Target)!.Value;
        Assert.Equal(exitCode, target.GetProperty("exitCode").GetInt32());
        Assert.Equal(standardOutput, target.GetProperty("standardOutput").GetString());
        Assert.Equal(standardError, target.GetProperty("standardError").GetString());
        Assert.Equal(failure.Command, target.GetProperty("command").GetString());
    }

    [Fact]
    public void FromException_PreservesDiagnosticExceptionMessageKey()
    {
        var commandResult = new ProcessCommandResult(
            new ProcessStartInfo { FileName = "ffmpeg.exe" },
            (int)InterExitCode.Failure,
            string.Empty,
            "native decoder error");
        var exception = new DiagnosticException(MsgKeys.Error_Invalid_audio, commandResult);

        var result = ApplicationDiagnostics.FromException<string>(exception);

        Assert.False(result.Succeeded);
        Assert.Equal(MsgKeys.Error_Invalid_audio, result.Diagnostics.Diagnostics.Single().Message.Key);

        var target = DiagnosticTargetSerializer.ToJsonElement(result.Diagnostics.Diagnostics.Single().Target);
        Assert.True(target.HasValue);
        Assert.Equal("native decoder error", target.Value.GetProperty("standardError").GetString());
        Assert.Contains("ffmpeg.exe", target.Value.GetProperty("command").GetString());
    }
}
