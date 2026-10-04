using System.Buffers.Binary;
using NetVips;
using VImage = NetVips.Image;

namespace PenguinTools.Image;

internal static class RasterPipeline
{
    private static readonly Lazy<bool> Initialized = new(() =>
    {
        if (!ModuleInitializer.VipsInitialized)
            throw new InvalidOperationException("The bundled libvips runtime could not be initialized.");
        NetVips.NetVips.Concurrency = Math.Max(1, Environment.ProcessorCount / ImageOperationScheduler.DefaultConcurrency);
        // Retaining operation graphs also retains open input files and decoded pixels between jobs.
        Cache.Max = 0;
        return true;
    });

    internal static void Initialize() => _ = Initialized.Value;

    public static void Validate(string source, CancellationToken ct)
    {
        Initialize();
        using var image = Load(source);
        Evaluate(image, () => image.Avg(), ct); // A streaming sink forces decoding, without a full managed pixel buffer.
    }

    public static VImage Resize(string source, int width, int height, bool opaque, CancellationToken ct)
    {
        Initialize();
        ct.ThrowIfCancellationRequested();
        using var images = new ImageScope();
        var loaded = images.Own(Load(source));
        var oriented = images.Own(loaded.Autorot());
        var srgb = images.Own(oriented.GetTypeOf("icc-profile-data") != 0
            ? oriented.IccTransform("srgb", embedded: true)
            : oriented.Colourspace(Enums.Interpretation.Srgb));
        var linear = images.Own(srgb.Colourspace(Enums.Interpretation.Scrgb));
        // scRGB uses 0..1 for both colors and alpha, including alpha converted from 8/16-bit input.
        // Black compositing commutes with linear filtering. Drop alpha before resizing opaque outputs.
        var filterInput = linear.HasAlpha()
            ? images.Own(opaque ? linear.Flatten(background: [0, 0, 0], maxAlpha: 1) : linear.Premultiply(maxAlpha: 1))
            : linear;
        var resized = images.Own(filterInput.Resize((double)width / filterInput.Width,
            kernel: Enums.Kernel.Lanczos3, vscale: (double)height / filterInput.Height));
        var straight = !opaque && linear.HasAlpha() ? images.Own(resized.Unpremultiply(maxAlpha: 1)) : resized;
        var output = images.Own(straight.Colourspace(Enums.Interpretation.Srgb));
        var withAlpha = output.HasAlpha() ? output : images.Own(output.AddAlpha());
        if (withAlpha.Width != width || withAlpha.Height != height)
            throw new InvalidDataException("Image resize produced unexpected dimensions.");
        return withAlpha.Cast(Enums.BandFormat.Uchar);
    }

    public static VImage OffsetBackground(VImage image, int offset)
    {
        if ((long)offset >= image.Height || (long)offset <= -image.Height)
            return image.NewFromImage([0, 0, 0, 255]);
        if (offset == 0) return image.Copy();
        var shift = Math.Abs(offset);
        using var cropped = image.Crop(0, offset > 0 ? shift : 0, image.Width, image.Height - shift);
        return cropped.Embed(0, offset > 0 ? 0 : shift, image.Width, image.Height,
            extend: Enums.Extend.Background, background: [0, 0, 0, 255]);
    }

    public static VImage EffectAtlas(IReadOnlyList<string?>? paths, CancellationToken ct)
    {
        Initialize();
        using var images = new ImageScope();
        var tiles = new VImage[4];
        for (var i = 0; i < tiles.Length; i++)
        {
            var path = paths is not null && i < paths.Count ? paths[i] : null;
            tiles[i] = images.Own(string.IsNullOrWhiteSpace(path)
                ? VImage.Black(256, 256, bands: 4)
                : Resize(path, 256, 256, false, ct));
        }
        return VImage.Arrayjoin(tiles, across: 2);
    }

    public static async Task WriteTgaAsync(VImage image, string destination, CancellationToken ct)
    {
        if (image.Bands != 4 || image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
            throw new InvalidDataException("TGA handoff requires a four-channel image with 16-bit dimensions.");
        // A single-input channel transform keeps the lazy decode/resize graph from branching.
        // All channel processing stays in libvips; only final-size BGRA pixels cross into .NET.
        using var matrix = VImage.NewFromArray(new double[,]
        {
            { 0, 0, 1, 0 }, { 0, 1, 0, 0 }, { 1, 0, 0, 0 }, { 0, 0, 0, 1 }
        });
        using var reordered = image.Recomb(matrix);
        using var bgra = reordered.Cast(Enums.BandFormat.Uchar);
        var pixels = Evaluate(bgra, () => bgra.WriteToMemory<byte>(), ct);
        var header = new byte[18];
        header[2] = 2; // Uncompressed true-color.
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), checked((ushort)image.Width));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), checked((ushort)image.Height));
        header[16] = 32;
        header[17] = 0x28; // Top-left origin, eight alpha bits.
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await output.WriteAsync(header, ct).ConfigureAwait(false);
        await output.WriteAsync(pixels, ct).ConfigureAwait(false);
    }

    private static VImage Load(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var loader = VImage.FindLoad(source)
                     ?? throw new InvalidDataException($"Unsupported or invalid raster image: {source}");
        var codec = loader.StartsWith("VipsForeignLoad", StringComparison.OrdinalIgnoreCase)
            ? loader["VipsForeignLoad".Length..] : loader;
        string[] rasterLoaders = ["jpeg", "png", "webp", "tiff", "gif", "heif", "jxl", "ppm", "rad", "jp2k", "bmp", "fits", "nifti", "vips"];
        if (!rasterLoaders.Any(name => codec.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Unsupported raster image: {source}");
        // These loaders default to page/frame zero and a single page. Do not request n=-1.
        return VImage.NewFromFile(source, access: Enums.Access.Sequential, failOn: Enums.FailOn.Warning, revalidate: true);
    }

    private static T Evaluate<T>(VImage image, Func<T> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        image.SetProgress(true);
        using var registration = ct.Register(() => image.SetKill(true));
        try
        {
            var result = action();
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (VipsException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
    }

    private sealed class ImageScope : IDisposable
    {
        private readonly List<VImage> _images = [];
        public VImage Own(VImage image)
        {
            _images.Add(image);
            return image;
        }

        public void Dispose()
        {
            for (var i = _images.Count - 1; i >= 0; i--) _images[i].Dispose();
        }
    }
}
