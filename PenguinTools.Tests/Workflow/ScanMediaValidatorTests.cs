using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Workflow;

public sealed class ScanMediaValidatorTests
{
    [Fact]
    public async Task ConcurrentAliases_ScaleWithUniqueFilesAndKeepAudioAndImageSeparate()
    {
        using var files = new TestFiles();
        var paths = Enumerable.Range(0, 8).Select(i => files.Write($"media-{i}.bin", "media")).ToArray();
        var pending = new TaskCompletionSource<MediaValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CountingMediaValidator { Audio = (_, _) => pending.Task };
        var ct = TestContext.Current.CancellationToken;
        var tool = new ScanMediaValidator(inner, ct);
        var joined = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, 512)
            .Select(i => Task.Run(async () =>
            {
                var path = paths[i % paths.Length];
                var alias = Path.Combine(files.Directory, ".", Path.GetFileName(path));
                var validation = tool.CheckAudioValidAsync(i / paths.Length % 2 == 0 ? path : alias, ct);
                if (Interlocked.Increment(ref joined) == 512)
                {
                    allStarted.SetResult();
                }

                return await validation;
            }, ct))
            .ToArray();

        await allStarted.Task.WaitAsync(ct);
        Assert.Equal(paths.Length, inner.AudioCalls);
        Assert.All(calls, call => Assert.False(call.IsCompleted));
        Assert.True((await tool.CheckImageValidAsync(paths[0], ct)).IsSuccess);
        Assert.Equal(1, inner.ImageCalls);

        pending.SetResult(MediaValidationResult.Valid);
        Assert.All(await Task.WhenAll(calls), result => Assert.True(result.IsSuccess));
        Assert.Equal(paths.Length, inner.AudioCalls);
    }

    [Fact]
    public async Task ChangedFileAndNewScan_TriggerFreshValidation()
    {
        using var files = new TestFiles();
        var path = files.Write("media.bin", "first");
        var inner = new CountingMediaValidator();
        var ct = TestContext.Current.CancellationToken;
        var tool = new ScanMediaValidator(inner, ct);
        await tool.CheckAudioValidAsync(path, ct);
        await tool.CheckAudioValidAsync(path, ct);
        Assert.Equal(1, inner.AudioCalls);

        File.WriteAllText(path, "longer replacement");
        await tool.CheckAudioValidAsync(path, ct);
        Assert.Equal(2, inner.AudioCalls);

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
        await tool.CheckAudioValidAsync(path, ct);
        Assert.Equal(3, inner.AudioCalls);

        await new ScanMediaValidator(inner, ct).CheckAudioValidAsync(path, ct);
        Assert.Equal(4, inner.AudioCalls);
    }

    [Fact]
    public async Task PathCase_UsesPlatformFileComparison()
    {
        using var files = new TestFiles();
        var path = files.Write("media.bin", "first");
        var other = Path.Combine(files.Directory, "MEDIA.bin");
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(other, "different");
        }

        var inner = new CountingMediaValidator();
        var ct = TestContext.Current.CancellationToken;
        var tool = new ScanMediaValidator(inner, ct);
        await tool.CheckAudioValidAsync(path, ct);
        await tool.CheckAudioValidAsync(other, ct);
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, inner.AudioCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\0")]
    public async Task InvalidPath_PreservesUnderlyingValidationResults(string? path)
    {
        var expected = MediaValidationResult.Invalid(new ArgumentException("media implementation failure"));
        var inner = new CountingMediaValidator
        {
            Audio = (_, _) => Task.FromResult(expected),
            Image = (_, _) => Task.FromResult(expected)
        };
        var ct = TestContext.Current.CancellationToken;
        var tool = new ScanMediaValidator(inner, ct);

        Assert.Same(expected, await tool.CheckAudioValidAsync(path!, ct));
        Assert.Same(expected, await tool.CheckImageValidAsync(path!, ct));
        Assert.Equal(1, inner.AudioCalls);
        Assert.Equal(1, inner.ImageCalls);
    }

    [Fact]
    public async Task MissingFile_IsRevalidatedAfterItAppears()
    {
        using var files = new TestFiles();
        var path = Path.Combine(files.Directory, "later.bin");
        var inner = new CountingMediaValidator
        {
            Audio = (source, _) => Task.FromResult(File.Exists(source)
                ? MediaValidationResult.Valid
                : MediaValidationResult.Invalid("missing"))
        };
        var ct = TestContext.Current.CancellationToken;
        var tool = new ScanMediaValidator(inner, ct);
        Assert.False((await tool.CheckAudioValidAsync(path, ct)).IsSuccess);
        files.Write("later.bin", "created");
        Assert.True((await tool.CheckAudioValidAsync(path, ct)).IsSuccess);
        Assert.Equal(2, inner.AudioCalls);
    }

    [Fact]
    public async Task CancellingOneWaiter_DoesNotCancelAnotherChart()
    {
        using var files = new TestFiles();
        using var waiterCancellation = new CancellationTokenSource();
        var path = files.Write("media.bin", "media");
        var pending = new TaskCompletionSource<MediaValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CountingMediaValidator { Audio = (_, _) => pending.Task };
        var tool = new ScanMediaValidator(inner, TestContext.Current.CancellationToken);
        var cancelled = tool.CheckAudioValidAsync(path, waiterCancellation.Token);
        var other = tool.CheckAudioValidAsync(path, TestContext.Current.CancellationToken);

        await waiterCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(other.IsCompleted);
        pending.SetResult(MediaValidationResult.Valid);
        Assert.True((await other).IsSuccess);
        Assert.Equal(1, inner.AudioCalls);
    }

    [Fact]
    public async Task ScanCancellation_ReachesDecoderAndDoesNotPoisonNextScan()
    {
        using var files = new TestFiles();
        using var scanCancellation = new CancellationTokenSource();
        var path = files.Write("media.bin", "media");
        var inner = new CountingMediaValidator
        {
            Audio = async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return MediaValidationResult.Valid;
            }
        };
        var tool = new ScanMediaValidator(inner, scanCancellation.Token);
        // Parsers call validation without a token; the scan must still stop the decoder.
#pragma warning disable xUnit1051 // Exercise the parser's default-token call explicitly.
        var validation = tool.CheckAudioValidAsync(path);
#pragma warning restore xUnit1051
        await scanCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validation);

        inner.Audio = (_, _) => Task.FromResult(MediaValidationResult.Valid);
        Assert.True((await new ScanMediaValidator(inner, TestContext.Current.CancellationToken)
            .CheckAudioValidAsync(path, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(2, inner.AudioCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scan_SharedFailureStillReportsEveryChart_AndNextScanRevalidates(bool throws)
    {
        using var files = new TestFiles();
        files.Write("shared.ogg", "synthetic audio");
        files.Write("first.ugc", ChartText(1001));
        files.Write("second.ugc", ChartText(1002));
        var failure = new IOException("synthetic decoder failure");
        var inner = new CountingMediaValidator
        {
            Audio = (_, _) => throws
                ? Task.FromException<MediaValidationResult>(failure)
                : Task.FromResult(MediaValidationResult.Invalid(failure))
        };
        var ct = TestContext.Current.CancellationToken;

        var scanned = await ChartScanner.ScanDirectoryAsync(TestAssets.Load(), inner, files.Directory,
            [ChartFileFormat.Ugc], new OptionExportProcessContext(new DiagnosticCollector(), 8, files.Directory, ct));

        Assert.True(scanned.Succeeded);
        Assert.Equal(2, scanned.Value.Count);
        Assert.Equal(1, inner.AudioCalls);
        var warnings = scanned.Diagnostics.Diagnostics.Where(diagnostic => diagnostic.Message.Key == MsgKeys.Error_Invalid_audio).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, warning => Assert.Equal(Severity.Warning, warning.Severity));
        Assert.All(scanned.Value, book => Assert.Equal(string.Empty, book.BookMeta.BgmFilePath));

        inner.Audio = (_, _) => Task.FromResult(MediaValidationResult.Valid);
        var rescanned = await ChartScanner.ScanDirectoryAsync(TestAssets.Load(), inner, files.Directory,
            [ChartFileFormat.Ugc], new OptionExportProcessContext(new DiagnosticCollector(), 8, files.Directory, ct));

        Assert.True(rescanned.Succeeded);
        Assert.Equal(2, inner.AudioCalls);
        Assert.DoesNotContain(rescanned.Diagnostics.Diagnostics, diagnostic => diagnostic.Message.Key == MsgKeys.Error_Invalid_audio);
        Assert.All(rescanned.Value, book => Assert.Equal("shared.ogg", book.BookMeta.BgmFilePath));
    }

    private static string ChartText(int id) =>
        $"@VER\t8\n@TICKS\t480\n@TITLE\tSynthetic\n@DIFF\t3\n@SONGID\t{id}\n" +
        "@BGM\tshared.ogg\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n";

    private sealed class TestFiles : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));

        internal string Write(string name, string content)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, true);
            }
        }
    }

    private sealed class CountingMediaValidator : IMediaValidator
    {
        private int _audioCalls;
        private int _imageCalls;
        internal int AudioCalls => Volatile.Read(ref _audioCalls);
        internal int ImageCalls => Volatile.Read(ref _imageCalls);
        internal Func<string, CancellationToken, Task<MediaValidationResult>> Audio { get; set; } =
            (_, _) => Task.FromResult(MediaValidationResult.Valid);
        internal Func<string, CancellationToken, Task<MediaValidationResult>> Image { get; set; } =
            (_, _) => Task.FromResult(MediaValidationResult.Valid);

        public Task<MediaValidationResult> CheckAudioValidAsync(string src, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _audioCalls);
            return Audio(src, ct);
        }

        public Task<MediaValidationResult> CheckImageValidAsync(string src, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _imageCalls);
            return Image(src, ct);
        }
    }
}
