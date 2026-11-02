using System.Buffers.Binary;
using PenguinTools.Core;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Media;

public sealed class DdsDecodeTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("penguintools-dds-透明-");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string TexconvPath => Path.Combine(AppContext.BaseDirectory, "assets", "texconv", "texconv.exe");

    [Theory]
    [InlineData("DXT1", 255)]
    [InlineData("DXT5", 128)]
    [InlineData("DX10", 255)]
    public async Task DecodeDds_PreservesSrgbPixelsAndAlpha(string format, byte alpha)
    {
        var source = Path.Combine(_root.FullName, "封面.unknown");
        var destination = Path.Combine(_root.FullName, "decoded.png");
        var workspace = Path.Combine(_root.FullName, "work");
        await File.WriteAllBytesAsync(source, CreateDds(format), Ct);
        var service = new ImageService(TexconvPath, Path.Combine(_root.FullName, "unused.exe"), workspace);

        var result = await service.DecodeDdsAsync(source, destination, Ct);

        Assert.Equal(new DdsDecodeResult(source, destination), result);
        var png = await File.ReadAllBytesAsync(destination, Ct);
        Assert.Equal(new byte[] { 0 }, ReadPngChunk(png, "sRGB"u8));
        var gamma = ReadPngChunk(png, "gAMA"u8);
        if (gamma is not null)
        {
            Assert.Equal(45455u, BinaryPrimitives.ReadUInt32BigEndian(gamma));
        }

        // Read stored PNG samples without applying its color metadata, and compare
        // against the known BC endpoint. A second gamma transform would alter RGB.
        var command = NativeProcess.CreateStartInfo(TexconvPath,
            ["-nologo", "-y", "-nogpu", "-dx10", "-m", "1", "-ft", "dds", "-f", "R8G8B8A8_UNORM",
                "--ignore-srgb", "-o", _root.FullName, "--", destination]);
        var conversion = await NativeProcess.RunAsync(command, Ct);
        Assert.Equal(InterExitCode.Success, conversion.ExitCode);
        var pixels = await File.ReadAllBytesAsync(Path.Combine(_root.FullName, "decoded.dds"), Ct);
        Assert.Equal(148 + 4 * 4 * 4, pixels.Length);
        Assert.Equal(28u, BinaryPrimitives.ReadUInt32LittleEndian(pixels.AsSpan(128)));
        for (var offset = 148; offset < pixels.Length; offset += 4)
        {
            Assert.Equal(new byte[] { 66, 65, 66, alpha }, pixels.AsSpan(offset, 4).ToArray());
        }
        Assert.Empty(Directory.EnumerateDirectories(workspace));
    }

    private static byte[] CreateDds(string format)
    {
        var headerSize = format == "DX10" ? 148 : 128;
        var blockSize = format == "DXT5" ? 16 : 8;
        var data = new byte[headerSize + blockSize];
        "DDS "u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 124); // Header size
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0x81007); // Caps, dimensions, format, linear size
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 4); // Height
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 4); // Width
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), (uint)blockSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76), 32); // Pixel format size
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(80), 4); // FourCC
        System.Text.Encoding.ASCII.GetBytes(format, data.AsSpan(84, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(108), 0x1000); // Texture
        if (format == "DX10")
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(128), 72); // BC1_UNORM_SRGB
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(132), 3); // Texture2D
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(140), 1); // Array size
        }
        if (format == "DXT5")
        {
            data[headerSize] = 128; // First alpha endpoint, selected by all pixels
        }
        // RGB565 endpoint (8, 16, 8), selected by all sixteen pixels.
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(headerSize + blockSize - 8), 0x4208);
        return data;
    }

    private static byte[]? ReadPngChunk(ReadOnlySpan<byte> png, ReadOnlySpan<byte> type)
    {
        for (var offset = 8; offset < png.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[offset..]));
            if (png.Slice(offset + 4, 4).SequenceEqual(type))
            {
                return png.Slice(offset + 8, length).ToArray();
            }
            offset += length + 12;
        }
        return null;
    }

    public void Dispose() => _root.Delete(recursive: true);
}
