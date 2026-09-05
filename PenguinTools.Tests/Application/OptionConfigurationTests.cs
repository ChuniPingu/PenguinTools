using PenguinTools.Application;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed class OptionConfigurationTests
{
    [Fact]
    public async Task CancelledLoad_DoesNotBecomeInvalidConfigWarning()
    {
        var directory = TestTempPaths.Create(".dir");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "options.json"), "{}",
                TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                OptionConfiguration.LoadForScanAsync(directory, cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MalformedConfig_ReturnsWarningWithPath()
    {
        var directory = TestTempPaths.Create(".dir");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "options.json");
            await File.WriteAllTextAsync(path, "{invalid}", TestContext.Current.CancellationToken);

            var result = await OptionConfiguration.LoadForScanAsync(directory, TestContext.Current.CancellationToken);

            Assert.Equal(path, result.ConfigPath);
            Assert.Null(result.Document);
            var warning = Assert.Single(result.Diagnostics.Diagnostics);
            Assert.Equal(Severity.Warning, warning.Severity);
            Assert.Equal(MsgKeys.Warn_Config_invalid, warning.Message.Key);
            Assert.Equal(path, warning.Path);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
