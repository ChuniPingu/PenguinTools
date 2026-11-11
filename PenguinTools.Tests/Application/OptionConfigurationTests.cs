using PenguinTools.Application;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Workflow;
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
    public async Task NegativeEventId_WarnsOnScanWhileSignedResourceIdsCanBeSaved()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-ids-");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var path = Path.Combine(root.FullName, "options.json");
            const string original = "{\"ultimaEventId\":-1}";
            await File.WriteAllTextAsync(path, original, ct);
            var scanned = await OptionConfiguration.LoadForScanAsync(root.FullName, ct);
            Assert.Null(scanned.Document);
            var warning = Assert.Single(scanned.Diagnostics.Diagnostics);
            Assert.Equal(MsgKeys.Error_Event_id_invalid, warning.Message.Key);
            Assert.Equal(Severity.Warning, warning.Severity);
            Assert.Equal(path, warning.Path);

            var error = await Assert.ThrowsAsync<DiagnosticException>(() =>
                OptionConfiguration.SaveAsync(path, new OptionDocument { WeEventId = -1 }, ct));
            Assert.Equal(MsgKeys.Error_Event_id_invalid, error.Descriptor.Key);
            Assert.Equal(original, await File.ReadAllTextAsync(path, ct));

            await OptionConfiguration.SaveAsync(path, new OptionDocument
            {
                SelectedReleaseTagId = -2,
                CustomReleaseTagId = -3,
                SelectedGenreId = -4,
                CustomGenreId = -5
            }, ct);
            var saved = await OptionConfiguration.LoadForScanAsync(root.FullName, ct);
            Assert.NotNull(saved.Document);
            Assert.Empty(saved.Diagnostics.Diagnostics);
            Assert.Equal(-2, saved.Document.SelectedReleaseTagId);
            Assert.Equal(-3, saved.Document.CustomReleaseTagId);
            Assert.Equal(-4, saved.Document.SelectedGenreId);
            Assert.Equal(-5, saved.Document.CustomGenreId);
        }
        finally { root.Delete(true); }
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
