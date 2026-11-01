using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace PenguinTools.Tests;

/// <summary>Re-encodes simple fixtures as the frozen v2/v8 wire layout for legacy-input tests.</summary>
internal static partial class LegacyEditorFixture
{
    internal static async Task DowngradeAsync(string path, CancellationToken ct)
    {
        if (Path.GetExtension(path) == ".ugc")
        {
            var text = (await File.ReadAllTextAsync(path, ct)).Replace("@VER\t9", "@VER\t8", StringComparison.Ordinal);
            text = CoordinateRegex().Replace(text, m => m.Groups[1].Value +
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"["0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".IndexOf(m.Groups[2].Value[0]) - 16]);
            await File.WriteAllTextAsync(path, text, ct);
            return;
        }
        var bytes = await File.ReadAllBytesAsync(path, ct);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 2);
        for (var block = 12; block < bytes.Length;)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(block + 4));
            if (bytes.AsSpan(block, 4).SequenceEqual("dat2"u8))
            {
                for (var offset = block + 8; offset < block + 8 + length;)
                {
                    bytes[offset + 5] = bytes[offset + 7];
                    bytes[offset + 7] = 0;
                    offset += bytes[offset] == 10 && bytes[offset + 1] == 1 ? 24 : 20;
                }
            }
            block += 8 + length;
        }
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    [GeneratedRegex(@"(#[0-9]+'[0-9]+:[txfdhsHaSC]|#[0-9]+>[cs])([0-9A-Za-z])")]
    private static partial Regex CoordinateRegex();
}
