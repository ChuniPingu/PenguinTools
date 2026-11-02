using System.Buffers.Binary;
using System.IO.Compression;
using PenguinTools.Core;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Media;

internal static class RasterFixtures
{
    // Synthetic 2x3 JPEG, filled with RGB (32, 96, 160).
    public const string Jpeg = "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAADAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDwqiiiv3M/HD//2Q==";

    public static string TexconvPath => Path.Combine(AppContext.BaseDirectory, "assets", "texconv", "texconv.exe");

    public static ImageService Service(string workspace) => new(TexconvPath,
        Path.Combine(AppContext.BaseDirectory, "assets", "image-tools", "image-tools.exe"), workspace);

    public static byte[] Png(byte red, byte green, byte blue, byte alpha, uint? gamma = null, bool srgb = false)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = [0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];
        WriteChunk(output, "IHDR"u8, header);
        if (gamma is { } value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            WriteChunk(output, "gAMA"u8, bytes);
        }
        if (srgb)
        {
            WriteChunk(output, "sRGB"u8, [0]);
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var row = 0; row < 3; row++)
            {
                zlib.Write([0, red, green, blue, alpha, red, green, blue, alpha]);
            }
        }
        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    public static async Task<byte[]> ReadPngPixelsAsync(string path, int width, int height, CancellationToken ct)
    {
        var png = await File.ReadAllBytesAsync(path, ct);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal((uint)width, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)));
        Assert.Equal((uint)height, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)));
        var command = NativeProcess.CreateStartInfo(TexconvPath,
            ["-nologo", "-y", "-nogpu", "-dx10", "-m", "1", "-ft", "dds", "-f", "R8G8B8A8_UNORM",
                "--ignore-srgb", "-o", Path.GetDirectoryName(path)!, "--", path]);
        var result = await NativeProcess.RunAsync(command, ct);
        Assert.Equal(InterExitCode.Success, result.ExitCode);
        var dds = await File.ReadAllBytesAsync(Path.ChangeExtension(path, ".dds"), ct);
        Assert.Equal(148 + width * height * 4, dds.Length);
        Assert.Equal(28u, BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(128)));
        return dds[148..];
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(integer, (uint)data.Length);
        output.Write(integer);
        output.Write(type);
        output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in type.ToArray().Concat(data.ToArray()))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
        }
        BinaryPrimitives.WriteUInt32BigEndian(integer, crc ^ uint.MaxValue);
        output.Write(integer);
    }
}
