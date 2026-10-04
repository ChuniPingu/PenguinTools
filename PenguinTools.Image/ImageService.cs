using PenguinTools.Core.IO;

namespace PenguinTools.Image;

/// <summary>In-process raster and AFB operations, using the supplied texconv executable for DDS codecs.</summary>
public sealed class ImageService
{
    private readonly ITexconvRunner _texconv;
    private readonly string _temporaryRoot;
    private readonly ImageOperationScheduler _scheduler = ImageOperationScheduler.Shared;

    public ImageService(string texconvPath, string temporaryWorkDirectory)
        : this(new TexconvRunner(Path.GetFullPath(texconvPath)), temporaryWorkDirectory)
    {
    }

    internal ImageService(ITexconvRunner texconv, string temporaryWorkDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryWorkDirectory);
        _texconv = texconv;
        _temporaryRoot = Path.GetFullPath(temporaryWorkDirectory);
    }

    public Task ValidateAsync(string source, CancellationToken ct = default) => RunAsync(token =>
    {
        RasterPipeline.Validate(source, token);
        return Task.CompletedTask;
    }, ct);

    public Task ConvertJacketAsync(string source, string destination, CancellationToken ct = default) =>
        WithWorkspaceAsync(async (workspace, token) =>
        {
            using var image = RasterPipeline.Resize(source, 300, 300, true, token);
            var dds = await EncodeAsync(image, "jacket", "BC1_UNORM", workspace, token).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(dds, token).ConfigureAwait(false);
            var header = DdsContainer.Read(bytes);
            if (header.Width != 300 || header.Height != 300 || header.Format != "DXT1" || header.MipLevels != 1 || bytes.Length != 45_128)
            {
                throw new InvalidDataException("texconv produced an incompatible jacket DDS.");
            }

            await PublishBytesAsync(destination, bytes, token).ConfigureAwait(false);
        }, ct);

    public Task ConvertStageAsync(string background, string destination, string? notesFieldDestination,
        IReadOnlyList<string?>? effects = null, int backgroundOffset = 160, string? template = null,
        CancellationToken ct = default) => WithWorkspaceAsync(async (workspace, token) =>
    {
        var templateBytes = template is null ? ReadTemplate("st_dummy.afb") : await File.ReadAllBytesAsync(template, token).ConfigureAwait(false);
        // Validate before starting expensive image work or writing outputs.
        var chunks = DdsContainer.Locate(templateBytes);
        if (chunks.Count < 2)
        {
            throw new InvalidDataException("A stage template requires two DDS textures.");
        }

        using var resized = RasterPipeline.Resize(background, 1920, 1080, true, token);
        using var shifted = RasterPipeline.OffsetBackground(resized, backgroundOffset);
        var backgroundDds = await EncodeAsync(shifted, "background", "BC1_UNORM", workspace, token).ConfigureAwait(false);
        using var atlas = RasterPipeline.EffectAtlas(effects, token);
        var effectsDds = await EncodeAsync(atlas, "effects", "BC3_UNORM", workspace, token).ConfigureAwait(false);
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
    }, ct);

    public Task<IReadOnlyList<string>> ExtractDdsAsync(string source, string outputDirectory, CancellationToken ct = default) =>
        _scheduler.RunAsync<IReadOnlyList<string>>(async token =>
        {
            var data = await File.ReadAllBytesAsync(source, token).ConfigureAwait(false);
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
                await PublishBytesAsync(destination, data.AsMemory(chunk.Offset, chunk.Length), token).ConfigureAwait(false);
                paths.Add(destination);
            }
            return paths;
        }, ct);

    public Task DecodeDdsAsync(string source, string destination, CancellationToken ct = default) =>
        WithWorkspaceAsync(async (workspace, token) =>
        {
            // A stable name also lets texconv sniff DDS input regardless of the caller's extension.
            var input = Path.Combine(workspace, "decode.dds");
            await CopyAsync(source, input, token).ConfigureAwait(false);
            await _texconv.RunAsync(["-nologo", "-y", "-nogpu", "-m", "1", "-ft", "png", "-f", "R8G8B8A8_UNORM",
                "--ignore-srgb", "-o", workspace, "--", input], token).ConfigureAwait(false);
            var png = Path.Combine(workspace, "decode.png");
            await AtomicFile.WriteAsync(destination, (stream, cancellation) => CopyToAsync(png, stream, cancellation), token).ConfigureAwait(false);
        }, ct);

    private async Task<string> EncodeAsync(NetVips.Image image, string name, string format, string workspace, CancellationToken ct)
    {
        var source = Path.Combine(workspace, name + ".tga");
        await RasterPipeline.WriteTgaAsync(image, source, ct).ConfigureAwait(false);
        await _texconv.RunAsync(["-nologo", "-y", "-nogpu", "-dx9", "-m", "1", "-f", format,
            "--ignore-srgb", "--tga-zero-alpha", "-o", workspace, "--", source], ct).ConfigureAwait(false);
        return Path.Combine(workspace, name + ".dds");
    }

    private Task<bool> WithWorkspaceAsync(Func<string, CancellationToken, Task> action, CancellationToken ct) => RunAsync(async token =>
    {
        var workspace = Path.Combine(_temporaryRoot, "image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            await action(workspace, token).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }, ct);

    private Task<bool> RunAsync(Func<CancellationToken, Task> action, CancellationToken ct) =>
        _scheduler.RunAsync(async token =>
        {
            await action(token).ConfigureAwait(false);
            return true;
        }, ct);

    internal static byte[] ReadTemplate(string name)
    {
        using var resource = typeof(ImageService).Assembly.GetManifestResourceStream($"PenguinTools.Image.Assets.{name}")
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
