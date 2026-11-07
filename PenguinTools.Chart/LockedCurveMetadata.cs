using System.Buffers.Binary;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

public sealed record LockedCurveProfile(string Key, byte[] Data);

/// <summary>Retains editable curve sources only while their baked paths still match.</summary>
internal static class LockedCurveMetadata
{
    private sealed record Entry(int Ordinal, int[][] Current, byte[] Snapshot);

    internal static void Read(U.Chart chart, byte[] data, byte[] notes)
    {
        var native = NativePaths(notes);
        var paths = chart.Notes.Children.Where(n => n is U.Slide or U.AirSlide).ToArray();
        var profiles = new List<LockedCurveProfile>();
        foreach (var entry in Decode(data))
        {
            if (entry.Ordinal >= native.Count || entry.Ordinal >= paths.Length ||
                !SamePath(entry.Current, native[entry.Ordinal]))
            {
                continue;
            }

            profiles.Add(new(MargreteCurveMetadata.Key(paths[entry.Ordinal]), Encode([entry])));
        }
        chart.Extras.LockedCurves = profiles;
    }

    internal static byte[] Write(IReadOnlyList<U.Note> paths, ChartExtras extras, byte[] notes)
    {
        var native = NativePaths(notes);
        var saved = extras.LockedCurves.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => new Queue<LockedCurveProfile>(g));
        var entries = new List<Entry>();
        for (var i = 0; i < paths.Count; i++)
        {
            if (!saved.TryGetValue(MargreteCurveMetadata.Key(paths[i]), out var matches) || matches.Count == 0)
            {
                continue;
            }
            var decoded = Decode(matches.Dequeue().Data);
            if (decoded.Count != 1 || i >= native.Count || native[i].Length != decoded[0].Current.Length)
            {
                throw MargreteInput.Invalid("xbl1", "matching locked path records");
            }
            var entry = decoded[0];
            // The writer can normalize AIR direction and insert its arrow.
            // Refresh native identities while retaining the source and child tags.
            for (var n = 0; n < native[i].Length; n++)
            {
                native[i][n][8] = entry.Current[n][8];
            }
            entries.Add(new(i, native[i], entry.Snapshot));
        }
        return entries.Count == 0 ? [] : Encode(entries);
    }

    private static bool SamePath(int[][] a, int[][] b) => a.Length == b.Length &&
        a.Where((record, i) => record.Where((value, j) => j != 8 && value != b[i][j]).Any()).Any() == false;

    private static List<int[][]> NativePaths(byte[] notes)
    {
        var result = new List<int[][]>();
        List<int[]>? current = null;
        for (var at = 0; at < notes.Length;)
        {
            if (notes.Length - at < 20)
            {
                throw MargreteInput.Invalid("dat2", "complete native note record");
            }
            var n = notes.AsSpan(at, 20);
            var kind = n[0];
            var attr = n[1];
            int[] record = [kind, attr, (sbyte)n[2], (sbyte)n[3], (sbyte)n[4], (sbyte)n[7], n[6],
                BinaryPrimitives.ReadInt32LittleEndian(n[8..]), 0,
                BinaryPrimitives.ReadInt32LittleEndian(n[12..]), BinaryPrimitives.ReadInt32LittleEndian(n[16..])];
            if (kind is 6 or 9 && attr == 1)
            {
                if (current is not null)
                {
                    result.Add(current.ToArray());
                }

                current = [record];
            }
            else if (kind is 6 or 9 && attr is >= 2 and <= 6 && current is not null && current[0][0] == kind)
            {
                current.Add(record);
            }
            at += kind == 10 && attr == 1 ? 24 : 20;
            if (at > notes.Length)
            {
                throw MargreteInput.Invalid("dat2", "complete AIR-CRUSH record");
            }
        }
        if (current is not null)
        {
            result.Add(current.ToArray());
        }

        return result;
    }

    private static List<Entry> Decode(byte[] data)
    {
        if (data.Length > 64 * 1024 * 1024)
        {
            throw MargreteInput.Invalid("xbl1", "bounded lock metadata");
        }

        using var reader = new BinaryReader(new MemoryStream(data));
        try
        {
            if (reader.ReadInt32() != 1)
            {
                throw MargreteInput.Invalid("xbl1", "lock metadata version 1");
            }

            var count = Count(reader);
            var entries = new List<Entry>();
            var used = new HashSet<int>();
            for (var i = 0; i < count; i++)
            {
                var ordinal = reader.ReadInt32();
                if (ordinal < 0 || !used.Add(ordinal))
                {
                    throw MargreteInput.Invalid("xbl1", "unique path ordinal");
                }

                var head = Record(reader);
                var children = Records(reader);
                ValidatePath(head, children);
                var start = checked((int)reader.BaseStream.Position);
                var source = Record(reader);
                var controls = Records(reader);
                ValidatePath(source, controls);
                var baked = Records(reader);
                if (source[0] != head[0] || baked.Any(n => n[0] != source[0] || n[1] != 3))
                {
                    throw MargreteInput.Invalid("xbl1", "matching frozen path records");
                }

                foreach (var _ in baked)
                {
                    if (!float.IsFinite(reader.ReadSingle()))
                    {
                        throw MargreteInput.Invalid("xbl1", "finite frozen height");
                    }
                }

                if (source[8] == 0x6c000000)
                {
                    var basisCount = Count(reader);
                    if (basisCount == 0 || (long)basisCount * 28 > data.Length - reader.BaseStream.Position)
                    {
                        throw MargreteInput.Invalid("xbl1", "complete height basis");
                    }

                    for (var n = 0; n < basisCount; n++)
                    {
                        var kind = reader.ReadInt32();
                        var attr = reader.ReadInt32();
                        if (n == 0 && (kind != 9 || attr != 1))
                        {
                            throw MargreteInput.Invalid("xbl1", "AIR-SLIDE height basis");
                        }

                        reader.BaseStream.Position += 20;
                    }
                }
                entries.Add(new(ordinal, new[] { head }.Concat(children).ToArray(), data[start..checked((int)reader.BaseStream.Position)]));
            }
            if (reader.BaseStream.Position != data.Length)
            {
                throw MargreteInput.Invalid("xbl1", "no trailing lock data");
            }

            return entries;
        }
        catch (EndOfStreamException)
        {
            throw MargreteInput.Invalid("xbl1", "complete lock metadata");
        }
    }

    private static void ValidatePath(int[] head, int[][] children)
    {
        if (head[0] is not (6 or 9) || head[1] != 1 || children.Any(n => n[0] != head[0] || n[1] is < 2 or > 6))
        {
            throw MargreteInput.Invalid("xbl1", "valid curve root and children");
        }
    }

    private static int Count(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count is < 0 or > 1_000_000)
        {
            throw MargreteInput.Invalid("xbl1", "bounded record count");
        }

        return count;
    }

    private static int[] Record(BinaryReader reader) => Enumerable.Range(0, 11).Select(_ => reader.ReadInt32()).ToArray();

    private static int[][] Records(BinaryReader reader)
    {
        var count = Count(reader);
        if ((long)count * 44 > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw MargreteInput.Invalid("xbl1", "complete path records");
        }

        return Enumerable.Range(0, count).Select(_ => Record(reader)).ToArray();
    }

    private static byte[] Encode(IEnumerable<Entry> entries)
    {
        var array = entries.ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write(array.Length);
        foreach (var entry in array)
        {
            writer.Write(entry.Ordinal);
            foreach (var value in entry.Current[0])
            {
                writer.Write(value);
            }

            writer.Write(entry.Current.Length - 1);
            foreach (var child in entry.Current.Skip(1))
            {
                foreach (var value in child)
                {
                    writer.Write(value);
                }
            }

            writer.Write(entry.Snapshot);
        }
        return stream.ToArray();
    }
}
