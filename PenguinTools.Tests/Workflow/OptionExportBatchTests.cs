using PenguinTools.Core.Diagnostic;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Workflow;

public sealed class OptionExportBatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledBatch_DoesNotStartItems(bool parallel)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var context = new OptionExportProcessContext(new DiagnosticCollector(), cancellation.Token, 1,
            Path.GetTempPath());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OptionExportBatch.ProcessItemsAsync(
            new[] { "one.ugc", "two.ugc" },
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            item => Path.Combine(context.WorkingDirectory, item), context, parallel));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CancellationDuringItem_StopsSequentialBatch()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var context = new OptionExportProcessContext(new DiagnosticCollector(), cancellation.Token, 1,
            Path.GetTempPath());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OptionExportBatch.ProcessItemsAsync(
            new[] { "one.ugc", "two.ugc" },
            (_, _) =>
            {
                calls++;
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            item => Path.Combine(context.WorkingDirectory, item), context));

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ItemFailures_PreserveDiagnosticsAndContinueBatch(bool parallel)
    {
        var calls = 0;
        var context = new OptionExportProcessContext(new DiagnosticCollector(),
            TestContext.Current.CancellationToken, 2, Path.GetTempPath());

        var result = await OptionExportBatch.ProcessItemsAsync(
            new[] { "one.ugc", "two.ugc" },
            (item, _) =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidDataException(item);
            },
            item => Path.Combine(context.WorkingDirectory, item), context, parallel);

        Assert.Equal(2, calls);
        Assert.Equal(new[] { "one.ugc", "two.ugc" }, result.Diagnostics.Select(d => d.Path).Order().ToArray());
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.Equal(Severity.Error, diagnostic.Severity);
            Assert.Equal(diagnostic.Path, Assert.IsType<InvalidDataException>(diagnostic.RelatedException).Message);
        });
    }
}
