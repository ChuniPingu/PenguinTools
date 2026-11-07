using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class SlideSegmentMetadata
{
    internal const string CommentPrefix = "'MGR_NOLINE_SEGMENTS_V1";

    internal static string Bits(U.Slide root) => new(new[] { root.NoLine }
        .Concat(root.Children.OfType<U.SlideJoint>().Select(n => n.NoLine)).SkipLast(1)
        .Select(value => value ? '1' : '0').ToArray());

    internal static bool IsMixed(string bits) => bits.Contains('0') && bits.Contains('1');

    internal static void Apply(U.Chart chart, U.Slide root, string bits)
    {
        var points = new U.Note[] { root }.Concat(root.Children.OfType<U.SlideJoint>()).SkipLast(1).ToArray();
        if (bits.Length != points.Length || bits.Any(c => c is not ('0' or '1')))
        {
            throw MargreteInput.Invalid("SLIDE segment flags", "one 0/1 flag per segment");
        }
        for (var i = 0; i < points.Length; i++)
        {
            if (points[i] is U.Slide slide)
            {
                slide.NoLine = bits[i] == '1';
            }
            else
            {
                ((U.SlideJoint)points[i]).NoLine = bits[i] == '1';
            }
        }
        chart.Extras.SegmentSlideLines = true;
    }

    internal static void ReadBinary(U.Chart chart, IEnumerable<byte[]> fields)
    {
        var slides = chart.Notes.Children.OfType<U.Slide>().ToArray();
        var used = new HashSet<int>();
        foreach (var bytes in fields)
        {
            if (bytes.Length < 24)
            {
                throw MargreteInput.Invalid("xns1", "complete SLIDE segment metadata");
            }

            int Get(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            var index = Get(0);
            if (index < 0 || index >= slides.Length || !used.Add(index) || Get(20) != bytes.Length - 24)
            {
                throw MargreteInput.Invalid("xns1", "unique existing SLIDE and matching flag count");
            }

            var root = slides[index];
            if (Get(4) != root.Tick.Original || Get(8) != root.Lane || Get(12) != root.Width || Get(16) != root.Timeline)
            {
                throw MargreteInput.Invalid("xns1", "matching SLIDE identity");
            }

            Apply(chart, root, Encoding.ASCII.GetString(bytes.AsSpan(24)));
        }
    }

    internal static void ReadComments(U.Chart chart, IEnumerable<string> lines, IReadOnlyList<U.Note?> roots)
    {
        var used = new HashSet<int>();
        foreach (var line in lines.Where(l => l.StartsWith(CommentPrefix, StringComparison.Ordinal)))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0] != CommentPrefix ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index >= roots.Count || !used.Add(index) || roots[index] is not U.Slide)
            {
                throw MargreteInput.Invalid(line, "unique SLIDE ordinal and segment flags");
            }

            Apply(chart, (U.Slide)roots[index]!, parts[2]);
        }
    }

    internal static void WriteBinary(BinaryWriter writer, IReadOnlyList<U.Slide> slides)
    {
        for (var i = 0; i < slides.Count; i++)
        {
            var root = slides[i];
            var bits = Bits(root);
            if (!IsMixed(bits))
            {
                continue;
            }

            if (bits.Length > ushort.MaxValue - 24)
            {
                throw MargreteInput.Invalid(bits.Length, "encodable SLIDE segment count");
            }

            writer.Write("xns1"u8);
            writer.Write((ushort)4);
            writer.Write((ushort)(24 + bits.Length));
            writer.Write(i);
            writer.Write(root.Tick.Original);
            writer.Write(root.Lane);
            writer.Write(root.Width);
            writer.Write(root.Timeline);
            writer.Write(bits.Length);
            writer.Write(Encoding.ASCII.GetBytes(bits));
        }
    }
}
