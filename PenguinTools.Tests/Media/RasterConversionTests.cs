using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Media;

public sealed class RasterConversionTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("penguintools-raster-");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private string Workspace => Path.Combine(_root.FullName, "work");

    [Theory]
    [InlineData(100000u, false, 188, 1)]
    [InlineData(60000u, false, 153, 1)]
    [InlineData(45455u, true, 128, 1)]
    [InlineData(100000u, false, 188, 0)]
    public async Task RasterConversion_DecodesGammaAndPreservesAlpha(uint gamma, bool srgb, byte expected, byte alpha)
    {
        var source = Path.Combine(_root.FullName, "source.unknown");
        var destination = Path.Combine(_root.FullName, "result.png");
        await File.WriteAllBytesAsync(source, RasterFixtures.Png(128, 128, 128, alpha, gamma, srgb), Ct);
        await RasterFixtures.Service(Workspace).ConvertRasterToPngAsync(source, destination, Ct);
        var pixels = await RasterFixtures.ReadPngPixelsAsync(destination, 2, 3, Ct);
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            Assert.Equal(new byte[] { expected, expected, expected, alpha }, pixels.AsSpan(offset, 4).ToArray());
        }
        Assert.Empty(Directory.EnumerateDirectories(Workspace));
    }

    [Fact]
    public async Task JacketConversion_DecodesGammaBeforeCompression()
    {
        var source = Path.Combine(_root.FullName, "linear.png");
        var dds = Path.Combine(_root.FullName, "jacket.dds");
        var png = Path.Combine(_root.FullName, "decoded.png");
        await File.WriteAllBytesAsync(source, RasterFixtures.Png(128, 128, 128, 255, 100000), Ct);
        var service = RasterFixtures.Service(Workspace);
        await service.ConvertJacketAsync(source, dds, Ct);
        await service.DecodeDdsAsync(dds, png, Ct);
        var pixels = await RasterFixtures.ReadPngPixelsAsync(png, 300, 300, Ct);
        Assert.InRange(pixels[0], 184, 192);
        Assert.InRange(pixels[1], 184, 192);
        Assert.InRange(pixels[2], 184, 192);
        Assert.Equal(255, pixels[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StageConversion_PreservesLowAndMissingEffectAlpha(byte alpha)
    {
        var source = Path.Combine(_root.FullName, "effect.png");
        var stage = Path.Combine(_root.FullName, "stage.afb");
        var png = Path.Combine(_root.FullName, "effect-decoded.png");
        await File.WriteAllBytesAsync(source, RasterFixtures.Png(255, 0, 0, alpha, srgb: true), Ct);
        var service = RasterFixtures.Service(Workspace);
        await service.ConvertStageAsync(source, stage, null, [source], ct: Ct);
        var chunks = await service.ExtractDdsAsync(stage, Path.Combine(_root.FullName, "dds"), Ct);
        await service.DecodeDdsAsync(chunks[1], png, Ct);
        var pixels = await RasterFixtures.ReadPngPixelsAsync(png, 512, 512, Ct);
        Assert.Equal(new byte[] { alpha == 0 ? (byte)0 : (byte)255, 0, 0, alpha }, pixels[..4]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, pixels.AsSpan(400 * 4, 4).ToArray());
    }

    [Fact]
    public async Task InvalidRaster_PreservesDestinationAndCleansWorkspace()
    {
        var source = Path.Combine(_root.FullName, "invalid.jpg");
        var destination = Path.Combine(_root.FullName, "keep.png");
        await File.WriteAllTextAsync(source, "broken", Ct);
        await File.WriteAllTextAsync(destination, "keep", Ct);
        await Assert.ThrowsAsync<DiagnosticException>(() => RasterFixtures.Service(Workspace)
            .ConvertRasterToPngAsync(source, destination, Ct));
        Assert.Equal("keep", await File.ReadAllTextAsync(destination, Ct));
        Assert.Empty(Directory.EnumerateDirectories(Workspace));
    }

    public void Dispose() => _root.Delete(recursive: true);
}
