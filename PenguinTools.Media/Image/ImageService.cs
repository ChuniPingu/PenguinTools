using System.Diagnostics;
using System.Globalization;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.IO;

namespace PenguinTools.Media;

/// <summary>Raster and DDS operations using supplied native tools, with managed AFB handling.</summary>
public sealed class ImageService : IImageService
{
    private const string InputArgument = "--input";

    private readonly string _texconvPath;
    private readonly string _imageToolsPath;
    private readonly string _temporaryRoot;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<ProcessCommandResult>> _runProcess;

    public ImageService(string texconvPath, string imageToolsPath, string temporaryWorkDirectory)
        : this(texconvPath, imageToolsPath, temporaryWorkDirectory, NativeProcess.RunAsync)
    {
    }

    internal ImageService(string texconvPath, string imageToolsPath, string temporaryWorkDirectory,
        Func<ProcessStartInfo, CancellationToken, Task<ProcessCommandResult>> runProcess)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryWorkDirectory);
        ArgumentNullException.ThrowIfNull(runProcess);
        _texconvPath = Path.GetFullPath(texconvPath);
        _imageToolsPath = Path.GetFullPath(imageToolsPath);
        _temporaryRoot = Path.GetFullPath(temporaryWorkDirectory);
        _runProcess = runProcess;
    }

    public async Task<MediaValidationResult> CheckImageValidAsync(string source, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            await RunToolAsync(_imageToolsPath, ["validate", InputArgument, source], ct).ConfigureAwait(false);
            return MediaValidationResult.Valid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return MediaValidationResult.Invalid(ex is DiagnosticException { Target: { } target } ? target : ex);
        }
    }

    public Task ConvertJacketAsync(string source, string destination, CancellationToken ct = default) =>
        WithWorkspaceAsync(async (workspace, token) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            var image = Path.Combine(workspace, "jacket.tga");
            await RunToolAsync(_imageToolsPath, ["resize", InputArgument, source, "--output", image,
                "--width", "300", "--height", "300", "--opaque"], token).ConfigureAwait(false);
            var dds = await EncodeAsync(image, "BC1_UNORM", workspace, token).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(dds, token).ConfigureAwait(false);
            var header = DdsContainer.Read(bytes);
            if (header.Width != 300 || header.Height != 300 || header.Format != "DXT1" || header.MipLevels != 1 || bytes.Length != 45_128)
            {
                throw new InvalidDataException("texconv produced an incompatible jacket DDS.");
            }

            await PublishBytesAsync(destination, bytes, token).ConfigureAwait(false);
        }, MsgKeys.Error_Invalid_jk_image, ct);

    public Task ConvertWallAsync(string source, string destination, WallWidth width, CancellationToken ct = default) =>
        WithWorkspaceAsync(async (workspace, token) =>
        {
            var (pixelsWide, pixelsHigh) = GetWallDimensions(width);
            var image = Path.Combine(workspace, "wall.tga");
            await RunToolAsync(_imageToolsPath, ["resize", InputArgument, source, "--output", image,
                "--width", pixelsWide.ToString(CultureInfo.InvariantCulture),
                "--height", pixelsHigh.ToString(CultureInfo.InvariantCulture)], token).ConfigureAwait(false);
            var dds = await EncodeAsync(image, "BC1_UNORM", workspace, token).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(dds, token).ConfigureAwait(false);
            var header = DdsContainer.Read(bytes);
            if (header.Width != pixelsWide || header.Height != pixelsHigh || header.Format != "DXT1" ||
                header.MipLevels != 1 || bytes.Length != 128 + pixelsWide * pixelsHigh / 2)
            {
                throw new InvalidDataException("texconv produced an incompatible wall DDS.");
            }
            await PublishBytesAsync(destination, bytes, token).ConfigureAwait(false);
        }, MsgKeys.Error_Invalid_wall_image, ct);

    private static (int Width, int Height) GetWallDimensions(WallWidth width) => width switch
    {
        WallWidth.Normal => (640, 480),
        WallWidth.Wide => (2048, 512),
        _ => throw new ArgumentOutOfRangeException(nameof(width), width, null)
    };

    public Task ConvertStageAsync(string background, string destination, string? notesFieldDestination,
        IReadOnlyList<string?>? effects = null, int backgroundOffset = 160, string? templatePath = null,
        CancellationToken ct = default) => WithWorkspaceAsync(async (workspace, token) =>
    {
        var templateBytes = templatePath is null ? ReadTemplate("st_dummy.afb") : await File.ReadAllBytesAsync(templatePath, token).ConfigureAwait(false);
        // Validate before starting expensive image work or writing outputs.
        var chunks = DdsContainer.Locate(templateBytes);
        if (chunks.Count < 2)
        {
            throw new InvalidDataException("A stage template requires two DDS textures.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(background);
        var backgroundImage = Path.Combine(workspace, "background.tga");
        var effectsImage = Path.Combine(workspace, "effects.tga");
        List<string> arguments = ["stage", "--background", background, "--background-output", backgroundImage,
            "--effects-output", effectsImage, "--offset", backgroundOffset.ToString(CultureInfo.InvariantCulture)];
        for (var index = 0; index < Math.Min(4, effects?.Count ?? 0); index++)
        {
            if (!string.IsNullOrWhiteSpace(effects![index]))
            {
                arguments.Add($"--effect{index}");
                arguments.Add(effects[index]!);
            }
        }
        await RunToolAsync(_imageToolsPath, arguments, token).ConfigureAwait(false);
        var backgroundDds = await EncodeAsync(backgroundImage, "BC1_UNORM", workspace, token).ConfigureAwait(false);
        var effectsDds = await EncodeAsync(effectsImage, "BC3_UNORM", workspace, token).ConfigureAwait(false);
        var output = DdsContainer.ReplaceStage(templateBytes,
            await File.ReadAllBytesAsync(backgroundDds, token).ConfigureAwait(false),
            await File.ReadAllBytesAsync(effectsDds, token).ConfigureAwait(false));
        var notesField = notesFieldDestination is null ? null : ReadTemplate("nf_dummy.afb");
        var outputs = new List<(string Path, ReadOnlyMemory<byte> Data)> { (destination, output) };
        if (notesField is not null)
        {
            outputs.Add((notesFieldDestination!, notesField));
        }

        await StagedOutputs.WriteAsync(outputs, token).ConfigureAwait(false);
    }, MsgKeys.Error_Invalid_bg_image, ct);

    public async Task<IReadOnlyList<string>> ExtractDdsAsync(string source, string outputDirectory, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var data = await File.ReadAllBytesAsync(source, ct).ConfigureAwait(false);
            var chunks = DdsContainer.Locate(data);
            var stem = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrWhiteSpace(stem))
            {
                stem = "chunk";
            }

            var paths = new List<string>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var destination = Path.Combine(outputDirectory, $"{stem}_{i + 1:D4}.dds");
                await PublishBytesAsync(destination, data.AsMemory(chunk.Offset, chunk.Length), ct).ConfigureAwait(false);
                paths.Add(destination);
            }
            return paths;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DiagnosticException(MsgKeys.Error_Invalid_bg_image,
                ex is DiagnosticException { Target: { } target } ? target : ex);
        }
    }

    public async Task<DdsDecodeResult> DecodeDdsAsync(string source, string destination, CancellationToken ct = default)
    {
        await WithWorkspaceAsync(async (workspace, token) =>
        {
            // A stable name also lets texconv sniff DDS input regardless of the caller's extension.
            var input = Path.Combine(workspace, "decode.dds");
            await CopyAsync(source, input, token).ConfigureAwait(false);
            // Legacy game DDS stores sRGB values without color-space metadata. Treat both
            // ends as sRGB to preserve those values and avoid writing a linear PNG gamma tag.
            await WritePngAsync(input, destination, workspace, token).ConfigureAwait(false);
        }, MsgKeys.Error_Invalid_bg_image, ct).ConfigureAwait(false);
        return new DdsDecodeResult(source, destination);
    }

    public Task ConvertRasterToPngAsync(string source, string destination, CancellationToken ct = default) =>
        WithWorkspaceAsync(async (workspace, token) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            var input = Path.Combine(workspace, "decode.tga");
            await RunToolAsync(_imageToolsPath, ["convert", InputArgument, source, "--output", input], token).ConfigureAwait(false);
            await WritePngAsync(input, destination, workspace, token).ConfigureAwait(false);
        }, MsgKeys.Error_Invalid_jk_image, ct);

    private async Task WritePngAsync(string input, string destination, string workspace, CancellationToken ct)
    {
        await RunToolAsync(_texconvPath, ["-nologo", "-y", "-nogpu", "-m", "1", "-ft", "png", "-f", "R8G8B8A8_UNORM_SRGB",
            "-srgb", "--tga-zero-alpha", "-o", workspace, "--", input], ct).ConfigureAwait(false);
        var png = Path.Combine(workspace, "decode.png");
        await AtomicFile.WriteAsync(destination, (stream, cancellation) => CopyToAsync(png, stream, cancellation), ct).ConfigureAwait(false);
    }

    private async Task RunToolAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = NativeProcess.CreateStartInfo(executablePath, arguments);
        var result = await _runProcess(startInfo, ct).ConfigureAwait(false);
        if (result.ExitCode != InterExitCode.Success)
        {
            throw new DiagnosticException(MsgKeys.Error_Command_failed, result);
        }
    }

    private async Task<string> EncodeAsync(string source, string format, string workspace, CancellationToken ct)
    {
        await RunToolAsync(_texconvPath, ["-nologo", "-y", "-nogpu", "-dx9", "-m", "1", "-f", format,
            "--ignore-srgb", "--tga-zero-alpha", "-o", workspace, "--", source], ct).ConfigureAwait(false);
        return Path.Combine(workspace, Path.GetFileNameWithoutExtension(source) + ".dds");
    }

    private async Task WithWorkspaceAsync(Func<string, CancellationToken, Task> action, string messageKey,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var workspace = Path.Combine(_temporaryRoot, "image-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            try
            {
                await action(workspace, ct).ConfigureAwait(false);
            }
            finally
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DiagnosticException(messageKey, ex is DiagnosticException { Target: { } target } ? target : ex);
        }
    }

    internal static byte[] ReadTemplate(string name)
    {
        using var resource = typeof(ImageService).Assembly.GetManifestResourceStream($"PenguinTools.Media.Image.Assets.{name}")
                             ?? throw new InvalidOperationException($"Missing image template: {name}");
        using var data = new MemoryStream();
        resource.CopyTo(data);
        return data.ToArray();
    }

    private static Task PublishBytesAsync(string destination, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
        AtomicFile.WriteAsync(destination, (stream, token) => stream.WriteAsync(bytes, token).AsTask(), ct);

    private static async Task CopyAsync(string source, string destination, CancellationToken ct)
    {
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
        await CopyToAsync(source, output, ct).ConfigureAwait(false);
    }

    private static async Task CopyToAsync(string source, Stream output, CancellationToken ct)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, ct).ConfigureAwait(false);
    }
}
