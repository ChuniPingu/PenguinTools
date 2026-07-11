using PenguinTools.Application;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;
using System.Diagnostics;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed class ApplicationDiagnosticsTests
{
    [Fact]
    public void FromException_PreservesDiagnosticExceptionMessageKey()
    {
        var commandResult = new ProcessCommandResult(
            new ProcessStartInfo { FileName = "mua_wav.exe" },
            (int)InterExitCode.Failure,
            string.Empty,
            "native decoder error");
        var exception = new DiagnosticException(MsgKeys.Error_Invalid_audio, commandResult);

        var result = ApplicationDiagnostics.FromException<string>(exception);

        Assert.False(result.Succeeded);
        Assert.Equal(MsgKeys.Error_Invalid_audio, result.Diagnostics.Diagnostics.Single().Message.Key);
    }
}
