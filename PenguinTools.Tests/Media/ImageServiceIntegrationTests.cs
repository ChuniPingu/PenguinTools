using System.Diagnostics;
using System.Globalization;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Media;

public sealed class ImageServiceIntegrationTests : IDisposable
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAXSURBVBhXY/j///9/BgYQZmD4zwDkAABdtAn36FUC7wAAAABJRU5ErkJggg==";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("penguintools-images-透明-");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string ImageToolsPath => Path.Combine(AppContext.BaseDirectory, "assets", "image-tools", "image-tools.exe");
    private static string TexconvPath => Path.Combine(AppContext.BaseDirectory, "assets", "texconv", "texconv.exe");
    private string Workspace => Path.Combine(_root.FullName, "work");

    [Fact]
    public async Task Jacket_UsesOneImageJobAndProducesLegacyDds()
    {
        var process = new RecordingProcess();
        var service = Service(process);
        var destination = Path.Combine(_root.FullName, "结果 jacket.dds");
        await service.ConvertJacketAsync(Fixture(), destination, Ct);
        Assert.Equal("resize", Assert.Single(process.CallsFor("image-tools.exe")).ArgumentList[0]);
        Assert.Single(process.CallsFor("texconv.exe"));
        var bytes = await File.ReadAllBytesAsync(destination, Ct);
        var header = DdsContainer.Read(bytes);
        Assert.Equal((300, 300, 1, "DXT1", 45_128), (header.Width, header.Height, header.MipLevels, header.Format, bytes.Length));
        Assert.Empty(Directory.EnumerateDirectories(Workspace));
    }

    [Theory]
    [InlineData(Core.Metadata.WallWidth.Normal, 640, 480)]
    [InlineData(Core.Metadata.WallWidth.Wide, 2048, 512)]
    public async Task Wall_ProducesStretchedSingleMipDxt1(Core.Metadata.WallWidth width, int pixelsWide, int pixelsHigh)
    {
        var process = new RecordingProcess();
        var destination = Path.Combine(_root.FullName, "wall.dds");
        await Service(process).ConvertWallAsync(Fixture(), destination, width, Ct);
        Assert.Equal("resize", Assert.Single(process.CallsFor("image-tools.exe")).ArgumentList[0]);
        Assert.Single(process.CallsFor("texconv.exe"));
        var bytes = await File.ReadAllBytesAsync(destination, Ct);
        var header = DdsContainer.Read(bytes);
        Assert.Equal((pixelsWide, pixelsHigh, 1, "DXT1", 128 + pixelsWide * pixelsHigh / 2),
            (header.Width, header.Height, header.MipLevels, header.Format, bytes.Length));
        Assert.Empty(Directory.EnumerateDirectories(Workspace));
    }

    [Fact]
    public async Task Stage_UsesOneImageJobAndPreservesContainer()
    {
        var process = new RecordingProcess();
        var service = Service(process);
        var source = Fixture();
        var stage = Path.Combine(_root.FullName, "stage.afb");
        var notes = Path.Combine(_root.FullName, "notes.afb");
        await service.ConvertStageAsync(source, stage, notes, [source, null, null, source], -160, ct: Ct);
        var arguments = Assert.Single(process.CallsFor("image-tools.exe")).ArgumentList;
        Assert.Equal("stage", arguments[0]);
        Assert.Contains("--effect0", arguments);
        Assert.Contains("--effect3", arguments);
        Assert.DoesNotContain("--effect1", arguments);
        Assert.Equal(2, process.CallsFor("texconv.exe").Count());
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
        Assert.Empty(Directory.EnumerateDirectories(Workspace));
    }

    [Fact]
    public async Task Validation_UsesExplicitToolPathAndImageContent()
    {
        var service = new ImageService(TexconvPath, ImageToolsPath, Workspace);
        Assert.True((await service.CheckImageValidAsync(Fixture("透明 source.unknown"), Ct)).IsSuccess);
    }

    [Fact]
    public async Task InvalidImage_PreservesDiagnosticsAndExistingDestination()
    {
        var process = new RecordingProcess();
        var service = Service(process);
        var source = Path.Combine(_root.FullName, "broken 透明.png");
        var destination = Path.Combine(_root.FullName, "keep.dds");
        await File.WriteAllTextAsync(source, "not an image", Ct);
        await File.WriteAllTextAsync(destination, "existing", Ct);
        var error = await Assert.ThrowsAsync<DiagnosticException>(() => service.ConvertJacketAsync(source, destination, Ct));
        Assert.Equal(MsgKeys.Error_Invalid_jk_image, error.Descriptor.Key);
        var result = Assert.IsType<ProcessCommandResult>(error.Target);
        Assert.NotEqual(InterExitCode.Success, result.ExitCode);
        Assert.NotEmpty(result.StandardError);
        Assert.Contains(source, result.Command, StringComparison.Ordinal);
        Assert.Equal("existing", await File.ReadAllTextAsync(destination, Ct));
        Assert.Empty(process.CallsFor("texconv.exe"));
        Assert.Empty(Directory.EnumerateDirectories(Workspace));
    }

    [Fact]
    public async Task MissingExecutable_PreservesStartupDiagnostics()
    {
        var executable = Path.Combine(_root.FullName, "missing image-tools.exe");
        var source = Fixture();
        var service = new ImageService(TexconvPath, executable, Workspace);
        var validation = await service.CheckImageValidAsync(source, Ct);
        Assert.True(validation.IsFailure);
        var result = Assert.IsType<ProcessCommandResult>(validation.Failure);
        Assert.Equal(InterExitCode.Failure, result.ExitCode);
        Assert.Contains(executable, result.Command, StringComparison.Ordinal);
        Assert.Contains(source, result.Command, StringComparison.Ordinal);
        Assert.NotEmpty(result.StandardError);
    }

    [Theory]
    [InlineData(false, MsgKeys.Error_Invalid_jk_image)]
    [InlineData(true, MsgKeys.Error_Invalid_bg_image)]
    public async Task NativeExitCodeTwo_IsRejectedAndPreservesDestination(bool stage, string messageKey)
    {
        var process = new RecordingProcess(exitCode: 2);
        var service = Service(process);
        var source = Fixture();
        var destination = Path.Combine(_root.FullName, "keep.dds");
        await File.WriteAllTextAsync(destination, "existing", Ct);

        var error = await Assert.ThrowsAsync<DiagnosticException>(() => stage
            ? service.ConvertStageAsync(source, destination, null, ct: Ct)
            : service.ConvertJacketAsync(source, destination, Ct));

        Assert.Equal(messageKey, error.Descriptor.Key);
        var result = Assert.IsType<ProcessCommandResult>(error.Target);
        Assert.Equal(2, (int)result.ExitCode);
        Assert.Equal("tool output", result.StandardOutput);
        Assert.Equal("tool error", result.StandardError);
        Assert.Contains(source, result.Command, StringComparison.Ordinal);
        Assert.Single(process.Calls);
        Assert.Equal("existing", await File.ReadAllTextAsync(destination, Ct));
        Assert.Empty(Directory.EnumerateDirectories(Workspace));
    }

    [Theory]
    [InlineData("image-tools.exe")]
    [InlineData("texconv.exe")]
    public async Task Cancellation_KeepsDestinationAndCleansWorkspace(string blockedExecutable)
    {
        var process = new RecordingProcess(blockedExecutable);
        var service = Service(process);
        var destination = Path.Combine(_root.FullName, "keep.dds");
        await File.WriteAllTextAsync(destination, "existing", Ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var task = service.ConvertJacketAsync(Fixture(), destination, cancellation.Token);
        try
        {
            await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal("existing", await File.ReadAllTextAsync(destination, Ct));
            Assert.Empty(Directory.EnumerateDirectories(Workspace));
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            { await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task NativeProcessCancellation_TerminatesChild()
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var marker = Path.Combine(_root.FullName, "pid.txt");
        var quotedMarker = marker.Replace("'", "''");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        string[] arguments = ["-NoProfile", "-NonInteractive", "-Command",
            $"[IO.File]::WriteAllText('{quotedMarker}.tmp', [string]$PID); [IO.File]::Move('{quotedMarker}.tmp', '{quotedMarker}'); Start-Sleep -Seconds 30"];
        var start = NativeProcess.CreateStartInfo(executable, arguments);
        var task = NativeProcess.RunAsync(start, cancellation.Token);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(marker))
            {
                await Task.Delay(25, deadline.Token);
            }
            var pid = int.Parse(await File.ReadAllTextAsync(marker, Ct), CultureInfo.InvariantCulture);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10), Ct));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            { await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
            catch (OperationCanceledException) { }
        }
    }

    private ImageService Service(RecordingProcess process) => new(TexconvPath, ImageToolsPath, Workspace, process.RunAsync);

    private string Fixture(string name = "透明 source.png")
    {
        var path = Path.Combine(_root.FullName, name);
        File.WriteAllBytes(path, Convert.FromBase64String(Png));
        return path;
    }

    private sealed class RecordingProcess(string? blockedExecutable = null, int? exitCode = null)
    {
        public List<ProcessStartInfo> Calls { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEnumerable<ProcessStartInfo> CallsFor(string executable) =>
            Calls.Where(start => string.Equals(Path.GetFileName(start.FileName), executable, StringComparison.OrdinalIgnoreCase));

        public async Task<ProcessCommandResult> RunAsync(ProcessStartInfo start, CancellationToken ct)
        {
            Calls.Add(start);
            if (string.Equals(Path.GetFileName(start.FileName), blockedExecutable, StringComparison.OrdinalIgnoreCase))
            {
                Started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }

            return exitCode is { } code
                ? new ProcessCommandResult(start, code, "tool output", "tool error")
                : await NativeProcess.RunAsync(start, ct);
        }
    }

    public void Dispose() => _root.Delete(recursive: true);
}
