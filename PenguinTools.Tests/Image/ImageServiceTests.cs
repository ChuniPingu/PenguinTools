using System.Buffers.Binary;
using NetVips;
using PenguinTools.Image;
using Xunit;
using VImage = NetVips.Image;

namespace PenguinTools.Tests.Image;

public sealed class ImageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "penguintools-image-tests", "透明 images " + Guid.NewGuid().ToString("N"));
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public ImageServiceTests()
    {
        Directory.CreateDirectory(_root);
        RasterPipeline.Initialize();
    }

    [Fact]
    public async Task Jacket_UsesLegacyBc1AndLinearBlackComposite_AndDecodeUsesOneProcess()
    {
        var runner = new CountingRunner();
        var service = Service(runner);
        var source = Fixture("透明 source.png", [255, 0, 0, 128]);
        var dds = Path.Combine(_root, "jacket.dds");
        await service.ConvertJacketAsync(source, dds, Ct);
        Assert.Single(runner.Calls);
        var bytes = await File.ReadAllBytesAsync(dds, Ct);
        var header = DdsContainer.Read(bytes);
        Assert.Equal((300, 300, 1, "DXT1", 45_128), (header.Width, header.Height, header.MipLevels, header.Format, bytes.Length));
        var png = Path.Combine(_root, "decoded.png");
        await service.DecodeDdsAsync(dds, png, Ct);
        Assert.Equal(2, runner.Calls.Count);
        using var decoded = VImage.NewFromFile(png);
        var pixel = decoded.Getpoint(100, 100);
        Assert.InRange(pixel[0], 180, 195); // 50% linear red -> about 188 sRGB, not 128.
        Assert.InRange(pixel[1], 0, 5);
        Assert.InRange(pixel[2], 0, 5);
        Assert.Equal(255, pixel[3]);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_root, "work")));
    }

    [Fact]
    public async Task Stage_PreservesContainerAndNotesField_AndMissingEffectsStayTransparent()
    {
        var runner = new CountingRunner();
        var service = Service(runner);
        var source = Fixture("background.png", [0, 255, 0, 255]);
        var stage = Path.Combine(_root, "stage.afb");
        var notes = Path.Combine(_root, "notes.afb");
        await service.ConvertStageAsync(source, stage, notes, backgroundOffset: 160, ct: Ct);
        Assert.Equal(2, runner.Calls.Count);
        var original = ImageService.ReadTemplate("st_dummy.afb");
        var output = await File.ReadAllBytesAsync(stage, Ct);
        Assert.Equal(original.Length, output.Length);
        var chunks = DdsContainer.Locate(output);
        Assert.Equal(2, chunks.Count);
        Assert.Equal((1920, 1080, "DXT1"), (chunks[0].Width, chunks[0].Height, chunks[0].Format));
        Assert.Equal((512, 512, "DXT5"), (chunks[1].Width, chunks[1].Height, chunks[1].Format));
        var cursor = 0;
        foreach (var chunk in chunks)
        {
            Assert.Equal(original.AsSpan(cursor, chunk.Offset - cursor).ToArray(), output.AsSpan(cursor, chunk.Offset - cursor).ToArray());
            cursor = chunk.Offset + chunk.Length;
        }
        Assert.Equal(original[cursor..], output[cursor..]);
        Assert.Equal(ImageService.ReadTemplate("nf_dummy.afb"), await File.ReadAllBytesAsync(notes, Ct));
        var extracted = await service.ExtractDdsAsync(stage, Path.Combine(_root, "extract"), Ct);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("stage_0001.dds", Path.GetFileName(extracted[0]));
        var bgPng = Path.Combine(_root, "bg.png");
        await service.DecodeDdsAsync(extracted[0], bgPng, Ct);
        using var bg = VImage.NewFromFile(bgPng);
        Assert.InRange(bg.Getpoint(100, 100)[1], 250, 255);
        Assert.Equal([0d, 0d, 0d, 255d], bg.Getpoint(100, 1079));
        var fxPng = Path.Combine(_root, "fx.png");
        await service.DecodeDdsAsync(extracted[1], fxPng, Ct);
        using var fx = VImage.NewFromFile(fxPng);
        using var alpha = fx.ExtractBand(3);
        Assert.Equal(0, alpha.Max());
    }

    [Fact]
    public async Task Effects_KeepSlotOrderAndPartialAlpha()
    {
        var service = Service(new CountingRunner());
        var red = Fixture("red.png", [255, 0, 0, 128]);
        var blue = Fixture("blue.png", [0, 0, 255, 255]);
        var stage = Path.Combine(_root, "stage.afb");
        await service.ConvertStageAsync(blue, stage, null, [red, null, null, blue], 0, ct: Ct);
        var extracted = await service.ExtractDdsAsync(stage, Path.Combine(_root, "extract"), Ct);
        var png = Path.Combine(_root, "atlas.png");
        await service.DecodeDdsAsync(extracted[1], png, Ct);
        using var image = VImage.NewFromFile(png);
        var first = image.Getpoint(100, 100);
        Assert.InRange(first[0], 245, 255);
        Assert.InRange(first[3], 125, 131);
        Assert.Equal(0, image.Getpoint(400, 100)[3]);
        Assert.Equal(0, image.Getpoint(100, 400)[3]);
        var fourth = image.Getpoint(400, 400);
        Assert.InRange(fourth[2], 245, 255);
        Assert.Equal(255, fourth[3]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void BackgroundOffset_HandlesBothDirectionsAndExtremeValues(int offset)
    {
        byte[] pixels = [10, 0, 0, 255, 20, 0, 0, 255, 30, 0, 0, 255];
        using var source = VImage.NewFromMemory(pixels, 1, 3, 4, Enums.BandFormat.Uchar);
        using var shifted = RasterPipeline.OffsetBackground(source, offset);
        if (offset == 1)
        {
            Assert.Equal(20, shifted.Getpoint(0, 0)[0]);
            Assert.Equal([0d, 0d, 0d, 255d], shifted.Getpoint(0, 2));
        }
        else if (offset == -1)
        {
            Assert.Equal(20, shifted.Getpoint(0, 2)[0]);
            Assert.Equal([0d, 0d, 0d, 255d], shifted.Getpoint(0, 0));
        }
        else Assert.Equal([0d, 0d, 0d, 255d], shifted.Getpoint(0, 1));
    }

    [Fact]
    public async Task Validation_UsesContentAndFullyDecodes_WithoutTexconv()
    {
        var runner = new CountingRunner();
        var service = Service(runner);
        var png = Fixture("image.unknown", [1, 2, 3, 255]);
        await service.ValidateAsync(png, Ct);
        var corrupt = Path.Combine(_root, "corrupt.png");
        var bytes = await File.ReadAllBytesAsync(png, Ct);
        await File.WriteAllBytesAsync(corrupt, bytes[..(bytes.Length / 2)], Ct);
        await Assert.ThrowsAnyAsync<Exception>(() => service.ValidateAsync(corrupt, Ct));
        var svg = Path.Combine(_root, "vector.png");
        await File.WriteAllTextAsync(svg, "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10'/></svg>", Ct);
        await Assert.ThrowsAnyAsync<Exception>(() => service.ValidateAsync(svg, Ct));
        await File.WriteAllTextAsync(corrupt, "not an image", Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ValidateAsync(corrupt, Ct));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void HighBitDepthGrayscale_IsScaledToSrgb()
    {
        ushort[] pixels = [32768, 32768, 32768, 32768];
        using var raw = VImage.NewFromMemory(pixels, 2, 2, 1, Enums.BandFormat.Ushort);
        using var gray = raw.Copy(interpretation: Enums.Interpretation.Grey16);
        var path = Path.Combine(_root, "gray.png");
        gray.Pngsave(path);
        using var resized = RasterPipeline.Resize(path, 4, 4, true, Ct);
        var pixel = resized.Getpoint(1, 1);
        Assert.InRange(pixel[0], 125, 131);
        Assert.Equal(pixel[0], pixel[1]);
        Assert.Equal(pixel[1], pixel[2]);
        Assert.Equal(255, pixel[3]);
    }

    [Fact]
    public void MultipageTiff_UsesFirstPage()
    {
        using var red = Solid([255, 0, 0, 255]);
        using var blue = Solid([0, 0, 255, 255]);
        using var pages = red.Join(blue, Enums.Direction.Vertical);
        var path = Path.Combine(_root, "pages.tif");
        pages.Tiffsave(path, pageHeight: red.Height);
        using var resized = RasterPipeline.Resize(path, 8, 8, true, Ct);
        Assert.Equal([255d, 0d, 0d, 255d], resized.Getpoint(4, 7));
    }

    [Fact]
    public async Task Extraction_SkipsMagicInsidePixels_AndRejectsTruncatedData()
    {
        var runner = new CountingRunner();
        var service = Service(runner);
        var data = ImageService.ReadTemplate("st_dummy.afb");
        var chunks = DdsContainer.Locate(data);
        "DDS POF0"u8.CopyTo(data.AsSpan(chunks[0].Offset + 256));
        var source = Path.Combine(_root, "magic.afb");
        await File.WriteAllBytesAsync(source, data, Ct);
        var paths = await service.ExtractDdsAsync(source, Path.Combine(_root, "extracted"), Ct);
        Assert.Equal(2, paths.Count);
        Assert.Equal(data.AsSpan(chunks[0].Offset, chunks[0].Length).ToArray(), await File.ReadAllBytesAsync(paths[0], Ct));
        Assert.Throws<InvalidDataException>(() => DdsContainer.Locate(data[..(chunks[1].Offset + 150)]));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task FailureAndCancellation_LeaveExistingDestinationAndCleanWorkspace()
    {
        var runner = new FailingRunner();
        var service = Service(runner);
        var input = Fixture("source.png", [30, 20, 10, 255]);
        var destination = Path.Combine(_root, "keep.dds");
        await File.WriteAllTextAsync(destination, "existing", Ct);
        var error = await Assert.ThrowsAsync<TexconvException>(() => service.ConvertJacketAsync(input, destination, Ct));
        Assert.Equal("failure details", error.StandardError);
        Assert.Equal("existing", await File.ReadAllTextAsync(destination, Ct));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_root, "work")));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConvertJacketAsync(input, destination, cancelled.Token));
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public void Stage_RejectsIncompatibleSlotSize()
    {
        var template = ImageService.ReadTemplate("st_dummy.afb");
        var chunks = DdsContainer.Locate(template);
        var bg = template.AsSpan(chunks[0].Offset, chunks[0].Length).ToArray();
        var fx = template.AsSpan(chunks[1].Offset, chunks[1].Length).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bg.AsSpan(16), 1916);
        Assert.Throws<InvalidDataException>(() => DdsContainer.ReplaceStage(template, bg, fx));
    }

    [Fact]
    public async Task Scheduler_BoundsJobsAndReleasesSlotAfterCancellation()
    {
        var scheduler = new ImageOperationScheduler(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        async Task<bool> Job(CancellationToken token)
        {
            if (Interlocked.Increment(ref started) == 2) twoStarted.SetResult();
            await release.Task.WaitAsync(token);
            return true;
        }
        var first = scheduler.RunAsync(Job, Ct);
        var second = scheduler.RunAsync(Job, Ct);
        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        using var cancelled = new CancellationTokenSource();
        var third = scheduler.RunAsync(Job, cancelled.Token);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        Assert.Equal(2, started);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(await scheduler.RunAsync(_ => Task.FromResult(true), Ct));
    }

    private ImageService Service(ITexconvRunner runner) => new(runner, Path.Combine(_root, "work"));

    [Fact]
    public async Task CancellationDuringConversion_CleansWorkspaceAndKeepsDestination()
    {
        var runner = new WaitingRunner();
        var service = Service(runner);
        var input = Fixture("source.png", [10, 20, 30, 255]);
        var destination = Path.Combine(_root, "keep.dds");
        await File.WriteAllTextAsync(destination, "existing", Ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = service.ConvertJacketAsync(input, destination, cancellation.Token);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal("existing", await File.ReadAllTextAsync(destination, Ct));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_root, "work")));
    }

    [Fact]
    public void Filtering_PremultipliesAlphaAndDoesNotBleedHiddenColors()
    {
        using var red = Solid([255, 0, 0, 255]);
        using var hiddenBlue = Solid([0, 0, 255, 0]);
        using var halves = red.Join(hiddenBlue, Enums.Direction.Horizontal);
        var source = Path.Combine(_root, "edge.png");
        halves.Pngsave(source);
        using var filtered = RasterPipeline.Resize(source, 1, 1, false, Ct);
        var pixel = filtered.Getpoint(0, 0);
        Assert.InRange(pixel[0], 250, 255);
        Assert.Equal(0, pixel[2]);
        Assert.InRange(pixel[3], 110, 145);
    }

    [Fact]
    public async Task Decode_SelectsFirstCubemapSurfaceWithoutResizing()
    {
        var runner = new CountingRunner();
        var service = Service(runner);
        var path = Path.Combine(_root, "cube.dds");
        await service.ConvertJacketAsync(Fixture("red.png", [255, 0, 0, 255]), path, Ct);
        var face = await File.ReadAllBytesAsync(path, Ct);
        var cube = new byte[128 + 6 * (face.Length - 128)];
        face.CopyTo(cube, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(cube.AsSpan(108), 0x1008); // TEXTURE | COMPLEX.
        BinaryPrimitives.WriteUInt32LittleEndian(cube.AsSpan(112), 0xfe00); // All six cube faces.
        await File.WriteAllBytesAsync(path, cube, Ct);
        Assert.Equal(cube.Length, DdsContainer.Read(cube).Length);
        var png = Path.Combine(_root, "first.png");
        await service.DecodeDdsAsync(path, png, Ct);
        using var image = VImage.NewFromFile(png);
        Assert.Equal((300, 300, 4), (image.Width, image.Height, image.Bands));
        Assert.InRange(image.Getpoint(100, 100)[0], 250, 255);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task StageOutputs_RestorePreviousFilesIfLaterCommitFails()
    {
        var stage = Path.Combine(_root, "stage.afb");
        var notes = Path.Combine(_root, "notes.afb");
        await File.WriteAllTextAsync(stage, "old stage", Ct);
        await File.WriteAllTextAsync(notes, "old notes", Ct);
        using (File.Open(notes, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => StagedOutputs.WriteAsync(
                [(stage, new byte[] { 1, 2 }), (notes, new byte[] { 3, 4 })], Ct));
        }
        Assert.Equal("old stage", await File.ReadAllTextAsync(stage, Ct));
        Assert.Equal("old notes", await File.ReadAllTextAsync(notes, Ct));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.bak"));
    }

    [Fact]
    public async Task StageOutputs_ArePreparedBeforeReplacingExistingFiles()
    {
        var stage = Path.Combine(_root, "stage.afb");
        var invalidNotes = Path.Combine(_root, "directory.afb");
        await File.WriteAllTextAsync(stage, "existing", Ct);
        Directory.CreateDirectory(invalidNotes);
        await Assert.ThrowsAsync<IOException>(() => StagedOutputs.WriteAsync(
            [(stage, new byte[] { 1, 2 }), (invalidNotes, new byte[] { 3, 4 })], Ct));
        Assert.Equal("existing", await File.ReadAllTextAsync(stage, Ct));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public void EmbeddedProfile_IsConvertedBackToSrgb()
    {
        using var source = Solid([180, 75, 25, 255]);
        using var p3 = source.IccTransform("p3", inputProfile: "srgb");
        var path = Path.Combine(_root, "profile.png");
        p3.Pngsave(path);
        using var loaded = VImage.NewFromFile(path);
        Assert.NotEqual(0, loaded.GetTypeOf("icc-profile-data"));
        using var converted = RasterPipeline.Resize(path, 8, 8, true, Ct);
        var pixel = converted.Getpoint(4, 4);
        Assert.InRange(pixel[0], 177, 183);
        Assert.InRange(pixel[1], 72, 78);
        Assert.InRange(pixel[2], 22, 28);
    }

    [Fact]
    public void ExifOrientation_IsAppliedBeforeStretching()
    {
        using var red = Solid([255, 0, 0, 255]);
        using var blue = Solid([0, 0, 255, 255]);
        using var horizontal = red.Join(blue, Enums.Direction.Horizontal);
        var jpeg = horizontal.JpegsaveBuffer(q: 100, subsampleMode: Enums.ForeignSubsample.Off);
        byte[] exif = [0x45, 0x78, 0x69, 0x66, 0, 0, 0x49, 0x49, 0x2a, 0, 8, 0, 0, 0, 1, 0,
            0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        var path = Path.Combine(_root, "rotate.jpg");
        using (var file = File.Create(path))
        {
            file.Write(jpeg.AsSpan(0, 2));
            file.Write([0xff, 0xe1, 0, (byte)(exif.Length + 2)]);
            file.Write(exif);
            file.Write(jpeg.AsSpan(2));
        }
        using var result = RasterPipeline.Resize(path, 12, 32, true, Ct);
        Assert.InRange(result.Getpoint(6, 4)[0], 245, 255);
        Assert.InRange(result.Getpoint(6, 28)[2], 245, 255);
    }

    [Fact]
    public async Task TexconvCancellation_TerminatesRunningChild()
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var marker = Path.Combine(_root, "pid.txt");
        var runner = new TexconvRunner(executable);
        using var cancellation = new CancellationTokenSource();
        var task = runner.RunAsync(["-NoProfile", "-NonInteractive", "-Command",
            $"[IO.File]::WriteAllText('{marker.Replace("'", "''")}', [string]$PID); Start-Sleep -Seconds 30"], cancellation.Token);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(marker)) await Task.Delay(25, deadline.Token);
            var pid = int.Parse(await File.ReadAllTextAsync(marker, Ct));
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10), Ct));
            Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(pid));
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await task; } catch (OperationCanceledException) { }
        }
    }

    private string Fixture(string name, double[] color)
    {
        var path = Path.Combine(_root, name);
        using var image = Solid(color);
        image.Pngsave(path);
        return path;
    }

    private static VImage Solid(double[] color)
    {
        using var black = VImage.Black(16, 12, bands: 4);
        using var colored = black.NewFromImage(color);
        return colored.Copy(interpretation: Enums.Interpretation.Srgb);
    }

    private sealed class CountingRunner : ITexconvRunner
    {
        private readonly TexconvRunner _inner = new(Path.Combine(AppContext.BaseDirectory, "assets", "texconv", "texconv.exe"));
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls.Add(arguments);
            return _inner.RunAsync(arguments, ct);
        }
    }

    private sealed class FailingRunner : ITexconvRunner
    {
        public int Calls { get; private set; }
        public Task RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            throw new TexconvException("texconv", 1, "output details", "failure details");
        }
    }

    private sealed class WaitingRunner : ITexconvRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Started.SetResult();
            return Task.Delay(Timeout.Infinite, ct);
        }
    }

    public void Dispose() => Directory.Delete(_root, true);
}
