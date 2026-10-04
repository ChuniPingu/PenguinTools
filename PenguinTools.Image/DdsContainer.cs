using System.Buffers.Binary;
using System.Text;

namespace PenguinTools.Image;

internal readonly record struct DdsChunk(int Offset, int Length, int Width, int Height, int MipLevels, string Format);

/// <summary>Reads DDS payload boundaries without interpreting or recompressing their pixels.</summary>
internal static class DdsContainer
{
    public static IReadOnlyList<DdsChunk> Locate(ReadOnlySpan<byte> data)
    {
        var chunks = new List<DdsChunk>();
        var cursor = 0;
        while (cursor <= data.Length - 4)
        {
            var relative = data[cursor..].IndexOf("DDS "u8);
            if (relative < 0) break;
            var start = checked(cursor + relative);
            var chunk = Read(data[start..]) with { Offset = start };
            chunks.Add(chunk);
            // Skip the entire payload: DDS/POF0 byte patterns can occur in compressed pixels.
            cursor = checked(start + chunk.Length);
        }

        if (chunks.Count == 0) throw new InvalidDataException("No DDS textures were found.");
        return chunks;
    }

    public static DdsChunk Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 128 || !data[..4].SequenceEqual("DDS "u8) || U32(data, 4) != 124 || U32(data, 76) != 32)
            throw new InvalidDataException("Invalid or truncated DDS header.");
        var width = checked((int)U32(data, 16));
        var height = checked((int)U32(data, 12));
        var mips = Math.Max(1, checked((int)U32(data, 28)));
        if (width <= 0 || height <= 0 || mips > 1 + System.Numerics.BitOperations.Log2((uint)Math.Max(width, height)))
            throw new InvalidDataException("Invalid DDS dimensions or mip count.");
        var headerSize = 128;
        var format = Encoding.ASCII.GetString(data.Slice(84, 4));
        var caps = U32(data, 112);
        var depth = (caps & 0x200000) != 0 ? Math.Max(1, checked((int)U32(data, 24))) : 1;
        var surfaces = (caps & 0x200) != 0 ? System.Numerics.BitOperations.PopCount(caps & 0xfc00) : 1;
        if (surfaces == 0) throw new InvalidDataException("DDS cubemap has no faces.");
        var blockBytes = format switch
        {
            "DXT1" or "ATI1" or "BC4U" or "BC4S" => 8,
            "DXT2" or "DXT3" or "DXT4" or "DXT5" or "ATI2" or "BC5U" or "BC5S" => 16,
            _ => 0
        };
        var bits = (int)U32(data, 88);
        if (format == "DX10")
        {
            if (data.Length < 148) throw new InvalidDataException("Truncated DDS DX10 header.");
            headerSize = 148;
            var dxgi = U32(data, 128);
            blockBytes = dxgi switch { >= 70 and <= 72 or >= 79 and <= 81 => 8, >= 73 and <= 78 or >= 82 and <= 84 or >= 94 and <= 99 => 16, _ => 0 };
            bits = dxgi switch
            {
                >= 1 and <= 4 => 128, >= 5 and <= 8 => 96, >= 9 and <= 22 => 64,
                >= 23 and <= 47 or >= 87 and <= 93 => 32,
                >= 48 and <= 59 or 85 or 86 or 115 => 16, >= 60 and <= 65 => 8, 66 => 1,
                _ => 0
            };
            var dimension = U32(data, 132);
            surfaces = checked((int)U32(data, 140));
            if (surfaces < 1 || dimension is < 2 or > 4)
                throw new InvalidDataException("Invalid DDS DX10 resource description.");
            if ((U32(data, 136) & 4) != 0) surfaces = checked(surfaces * 6);
            depth = dimension == 4 ? Math.Max(1, checked((int)U32(data, 24))) : 1;
            format = $"DXGI:{dxgi}";
        }
        else if (blockBytes == 0 && (U32(data, 80) & 4) != 0)
        {
            // Legacy D3DFORMAT numeric FourCC values.
            bits = U32(data, 84) switch { 36 or 110 or 113 or 115 => 64, 111 => 16, 112 or 114 => 32, 116 => 128, _ => 0 };
        }

        if (blockBytes == 0 && bits is not (1 or 8 or 16 or 24 or 32 or 64 or 96 or 128))
            throw new InvalidDataException($"Unsupported DDS storage format: {format}.");
        long payload = 0;
        var w = width;
        var h = height;
        for (var level = 0; level < mips; level++)
        {
            var row = blockBytes != 0 ? ((long)w + 3) / 4 * blockBytes : ((long)w * bits + 7) / 8;
            // Preserve explicitly padded legacy top-level scanlines.
            if (level == 0 && blockBytes == 0 && (U32(data, 8) & 8) != 0)
                row = Math.Max(row, U32(data, 20));
            payload = checked(payload + row * (blockBytes != 0 ? ((long)h + 3) / 4 : h) * depth);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            depth = Math.Max(1, depth / 2);
        }

        var length = checked(headerSize + payload * surfaces);
        if (length > data.Length) throw new InvalidDataException("Truncated DDS pixel data.");
        return new DdsChunk(0, checked((int)length), width, height, mips, format);
    }

    public static byte[] ReplaceStage(byte[] template, byte[] background, byte[] effects)
    {
        var chunks = Locate(template);
        if (chunks.Count < 2) throw new InvalidDataException("A stage template requires two DDS textures.");
        ValidateSlot(chunks[0], background, 1920, 1080, "DXT1");
        ValidateSlot(chunks[1], effects, 512, 512, "DXT5");
        var output = (byte[])template.Clone();
        background.CopyTo(output.AsSpan(chunks[0].Offset, chunks[0].Length));
        effects.CopyTo(output.AsSpan(chunks[1].Offset, chunks[1].Length));
        return output;
    }

    private static void ValidateSlot(DdsChunk slot, byte[] replacement, int width, int height, string format)
    {
        var actual = Read(replacement);
        if (slot.Width != width || slot.Height != height || slot.MipLevels != 1 || slot.Format != format ||
            actual.Width != width || actual.Height != height || actual.MipLevels != 1 || actual.Format != format ||
            replacement.Length != actual.Length || replacement.Length != slot.Length)
            throw new InvalidDataException($"Stage texture slot must be {width}x{height} {format} with one mip level and unchanged size.");
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
