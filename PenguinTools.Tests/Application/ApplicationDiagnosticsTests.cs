using System.Diagnostics;
using PenguinTools.Application;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed class ApplicationDiagnosticsTests
{
    [Fact]
    public void TexconvFailure_PreservesSubprocessDiagnostics()
    {
        var failure = new PenguinTools.Image.TexconvException("texconv.exe -- input.tga", 7, "encoder output", "encoder failure");
        var result = ApplicationDiagnostics.FromException<string>(new DiagnosticException(MsgKeys.Error_Invalid_jk_image, failure));
        var target = DiagnosticTargetSerializer.ToJsonElement(Assert.Single(result.Diagnostics.Diagnostics).Target)!.Value;
        Assert.Equal(7, target.GetProperty("exitCode").GetInt32());
        Assert.Equal("encoder output", target.GetProperty("standardOutput").GetString());
        Assert.Equal("encoder failure", target.GetProperty("standardError").GetString());
        Assert.Contains("texconv.exe", target.GetProperty("command").GetString());
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
