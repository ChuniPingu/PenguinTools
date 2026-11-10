using System.Globalization;
using System.Text;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Parser;

public sealed record EditorMetadata(Meta Metadata, string Format, int Version);

/// <summary>Reads only the common header fields verified in MGXC v4 and UGC v9.</summary>
public static class EditorMetadataReader
{
    public static async Task<EditorMetadata?> ReadAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var meta = new Meta { FilePath = path };
        if (Path.GetExtension(path).Equals(".mgxc", StringComparison.OrdinalIgnoreCase))
        {
            return ReadMgxc(path, meta, ct);
        }
        return Path.GetExtension(path).Equals(".ugc", StringComparison.OrdinalIgnoreCase)
            ? await ReadUgcAsync(path, meta, ct) : null;
    }

    private static EditorMetadata? ReadMgxc(string path, Meta meta, CancellationToken ct)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUtf8String(4) != "MGXC")
        {
            return null;
        }

        reader.ReadInt32();
        if (reader.ReadInt32() != 4)
        {
            return null;
        }

        reader.ReadBlock("meta", field =>
        {
            ct.ThrowIfCancellationRequested();
            ReadMgxcField(field, meta);
        });
        return new EditorMetadata(meta, "MGXC", 4);
    }

    private static void ReadMgxcField(BinaryReader field, Meta meta)
    {
        var name = field.ReadUtf8String(4);
        // Bound strings to this metadata block, including long UTF-8 fields.
        var type = field.ReadInt16();
        var attr = field.ReadInt16();
        if (type == 4 && (attr < 0 || attr > field.BaseStream.Length - field.BaseStream.Position))
        {
            throw new EndOfStreamException();
        }

        field.BaseStream.Position -= 4;
        var value = field.ReadField();
        Apply(meta, name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
    }

    private static async Task<EditorMetadata?> ReadUgcAsync(string path, Meta meta, CancellationToken ct)
    {
        using var text = new StreamReader(path, new UTF8Encoding(false, true));
        var hasVersion = false;
        while (await text.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith('#') || line.Equals("@ENDHEAD", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var fields = line.Split('\t');
            if (fields.Length < 2)
            {
                continue;
            }

            if (fields[0].Equals("@VER", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(fields[1], out var version) || version != 9)
                {
                    return null;
                }

                hasVersion = true;
            }
            Apply(meta, fields[0].ToUpperInvariant(), fields[1]);
        }
        return hasVersion ? new EditorMetadata(meta, "UGC", 9) : null;
    }

    private static void Apply(Meta meta, string name, string value)
    {
        switch (name)
        {
            case "titl":
            case "@TITLE":
                meta.Title = value;
                break;
            case "arts":
            case "@ARTIST":
                meta.Artist = value;
                break;
            case "dsgn":
            case "@DESIGN":
                meta.Designer = value;
                break;
            case "sgid":
            case "@SONGID":
                meta.MgxcId = value;
                meta.Id = ResourceIds.TryParseNonNegative(value, out var id) ? id : null;
                break;
            case "diff":
            case "@DIFF":
                if (int.TryParse(value, out var difficulty))
                {
                    meta.Difficulty = UmiguriParserCommon.DifficultyFromValue(difficulty);
                }

                break;
            case "cnst":
            case "@CONST":
                if (decimal.TryParse(value, CultureInfo.InvariantCulture, out var level))
                {
                    meta.Level = level;
                }

                break;
            case "mbpm":
            case "@MAINBPM":
                if (decimal.TryParse(value, CultureInfo.InvariantCulture, out var bpm))
                {
                    meta.MainBpm = bpm;
                }

                break;
        }
    }
}
