using System.Text;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core.IO;
using Xunit;
using C2sChart = PenguinTools.Chart.Models.c2s.Chart;
using UmgrChart = PenguinTools.Chart.Models.umgr.Chart;

namespace PenguinTools.Tests.Parser;

public class ChartWriterAtomicOutputTests
{
    [Theory]
    [InlineData(".c2s")]
    [InlineData(".ugc")]
    public async Task CancelledWrite_PreservesExistingOutput(string extension)
    {
        var path = TestTempPaths.Create(extension);
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                if (extension == ".c2s")
                {
                    await new C2SChartWriter(new C2SWriteRequest(path, new C2sChart()))
                        .WriteAsync(cancellation.Token);
                }
                else
                {
                    await new UgcChartWriter(path, new UmgrChart()).WriteAsync(cancellation.Token);
                }
            });

            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            AssertNoTemporaryOutput(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task C2sEncodingFailure_PreservesExistingOutput()
    {
        var path = TestTempPaths.Create(".c2s");
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", TestContext.Current.CancellationToken);
            var chart = new C2sChart();
            chart.Meta.Designer = new string('a', 8192) + '\uD800';

            await Assert.ThrowsAsync<EncoderFallbackException>(() =>
                new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(TestContext.Current.CancellationToken));

            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            AssertNoTemporaryOutput(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CancelledAtomicWrite_AfterWriting_PreservesExistingOutput()
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                AtomicFile.WriteAsync(path, async (stream, token) =>
                {
                    await stream.WriteAsync("replacement chart"u8.ToArray(), token);
                    await stream.FlushAsync(token);
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }, cancellation.Token));

            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            AssertNoTemporaryOutput(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertNoTemporaryOutput(string path) =>
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
}
