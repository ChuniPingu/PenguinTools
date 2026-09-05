using System.Diagnostics;
using PenguinTools.Chart.Parser;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class MediaValidationTests
{
    [Fact]
    public async Task Cancellation_DoesNotInvalidateMediaOrReportWarning()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var diagnostics = new DiagnosticCollector();
        var invalidated = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaValidation.ReportAsync(
            Task.FromCanceled<ProcessCommandResult>(cancellation.Token), "jacket.png", MsgKeys.Error_Invalid_jk_image,
            () => invalidated = true, diagnostics));

        Assert.False(invalidated);
        Assert.Empty(diagnostics.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedValidation_InvalidatesOnceAndPreservesCause(bool throws)
    {
        var failure = new ProcessCommandResult(new ProcessStartInfo { FileName = "mua_img" }, 1, "", "bad image");
        var exception = new IOException("cannot read image");
        var task = throws ? Task.FromException<ProcessCommandResult>(exception) : Task.FromResult(failure);
        var diagnostics = new DiagnosticCollector();
        var calls = 0;

        await MediaValidation.ReportAsync(task, "jacket.png", MsgKeys.Error_Invalid_jk_image, () => calls++, diagnostics);

        Assert.Equal(1, calls);
        var warning = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Equal("jacket.png", warning.Path);
        Assert.Equal(MsgKeys.Error_Invalid_jk_image, warning.Message.Key);
        Assert.Same(throws ? (object)exception : failure, warning.Target);
    }

    [Fact]
    public async Task FailedInvalidation_IsNotRetriedOrHidden()
    {
        var failure = new ProcessCommandResult(new ProcessStartInfo { FileName = "mua_img" }, 1, "", "bad image");
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => MediaValidation.ReportAsync(
            Task.FromResult(failure), "jacket.png", MsgKeys.Error_Invalid_jk_image,
            () =>
            {
                calls++;
                throw new InvalidOperationException("invalid parser state");
            }, new DiagnosticCollector()));

        Assert.Equal(1, calls);
    }
}
