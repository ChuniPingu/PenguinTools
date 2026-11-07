using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

public sealed record CurvePathProfile(string Key, List<int> Modes, List<int?> Heights);

internal sealed class MargreteCurveMetadata
{
    private readonly Dictionary<int, (int[] Identity, U.CurveInterpretation Mode)> _modes = [];
    private readonly List<int[]> _legacy = [];
    private readonly Dictionary<U.Note, int[]> _nodes = [];
    private readonly List<byte> _height = [];
    private readonly List<byte> _locked = [];
    private int _heightSize;
    private int _lockedSize;
    private int _controlOrdinal;

    internal static bool IsField(string name) => name is "xbc1" or "xbz1" or "xbh1" or "xbl1";

    internal void Read(string name, byte[] bytes)
    {
        int Get(int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
        switch (name)
        {
            case "xbc1":
                if (bytes.Length != 32 || Get(0) < 0 || Get(4) is not (6 or 9) || Get(28) is not (1 or 2) ||
                    !_modes.TryAdd(Get(0), (Enumerable.Range(1, 6).Select(i => Get(i * 4)).ToArray(), (U.CurveInterpretation)Get(28))))
                {
                    throw MargreteInput.Invalid(name, "unique curve ordinal, identity and interpretation");
                }

                break;
            case "xbz1":
                if (bytes.Length != 24 || Get(0) is not (6 or 9))
                {
                    throw MargreteInput.Invalid(name, "legacy curve identity");
                }

                _legacy.Add(Enumerable.Range(0, 6).Select(i => Get(i * 4)).ToArray());
                break;
            case "xbh1":
                AppendChunk(_height, ref _heightSize, bytes, name);
                break;
            case "xbl1":
                AppendChunk(_locked, ref _lockedSize, bytes, name);
                break;
        }
    }

    private static void AppendChunk(List<byte> stream, ref int expected, byte[] bytes, string name)
    {
        if (bytes.Length < 8)
        {
            throw MargreteInput.Invalid(name, "complete extension chunk");
        }

        var total = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        if (total is < 8 or > 64 * 1024 * 1024 || offset != stream.Count ||
            (expected != 0 && expected != total) || stream.Count + bytes.Length - 8 > total)
        {
            throw MargreteInput.Invalid(name, "ordered bounded extension chunks");
        }

        expected = total;
        stream.AddRange(bytes[8..]);
    }

    internal void Observe(U.Note note, int kind, int attribute, int rawHeight, int version, bool extended)
    {
        if (kind is not (6 or 9))
        {
            return;
        }

        var identity = new[] { kind, note.Lane, note.Width, rawHeight, note.Tick.Original, note.Timeline };
        _nodes.Add(note, [kind, attribute, note.Lane, note.Width, rawHeight, note.Tick.Original, note.Timeline]);
        if (attribute != 4)
        {
            return;
        }

        var mode = version < 4 ? U.CurveInterpretation.Legacy :
            extended ? U.CurveInterpretation.OfficialExtended : U.CurveInterpretation.OfficialNormal;
        if (_modes.TryGetValue(_controlOrdinal++, out var saved) && saved.Identity.SequenceEqual(identity))
        {
            mode = saved.Mode;
        }
        else
        {
            var index = _legacy.FindIndex(n => n.SequenceEqual(identity));
            if (index >= 0)
            {
                mode = U.CurveInterpretation.Legacy;
                _legacy.RemoveAt(index);
            }
        }
        if (note is U.SlideJoint slide)
        {
            slide.CurveInterpretation = mode;
        }
        else if (note is U.AirSlideJoint air)
        {
            air.CurveInterpretation = mode;
        }
    }

    internal void Apply(U.Chart chart, byte[] notePayload)
    {
        if (_legacy.Count != 0 || _height.Count != _heightSize || _locked.Count != _lockedSize)
        {
            throw MargreteInput.Invalid("curve metadata", "complete metadata with matching legacy controls");
        }

        if (_locked.Count > 0)
        {
            LockedCurveMetadata.Read(chart, _locked.ToArray(), notePayload);
        }
        if (_height.Count == 0)
        {
            return;
        }

        using var reader = new BinaryReader(new MemoryStream(_height.ToArray()));
        var roots = chart.Notes.Children.Where(n => n is U.Slide or U.AirSlide).ToArray();
        try
        {
            if (reader.ReadInt32() != 1)
            {
                throw MargreteInput.Invalid("xbh1", "height basis version 1");
            }

            var count = Count(reader);
            var used = new HashSet<int>();
            for (var i = 0; i < count; i++)
            {
                var ordinal = reader.ReadInt32();
                if (ordinal < 0 || !used.Add(ordinal))
                {
                    throw MargreteInput.Invalid("xbh1", "unique path ordinal");
                }

                var saved = ReadNodes(reader);
                var basis = ReadNodes(reader);
                if (basis.Length == 0 || basis[0][0] != 9 || basis[0][1] != 1)
                {
                    throw MargreteInput.Invalid("xbh1", "AIR-SLIDE height basis");
                }

                if (ordinal >= roots.Length)
                {
                    continue;
                }

                var points = new[] { roots[ordinal] }.Concat(roots[ordinal].Children).ToArray();
                if (points.Length != saved.Length || basis.Length != saved.Length ||
                    points.Where((n, index) => !_nodes.TryGetValue(n, out var value) || !value.SequenceEqual(saved[index]) ||
                        basis[index][0] != value[0] || basis[index][1] != value[1]).Any())
                {
                    continue;
                }

                for (var n = 0; n < points.Length; n++)
                {
                    if (points[n] is U.AirSlide head)
                    {
                        head.SamplingHeight = basis[n][4];
                    }
                    else if (points[n] is U.AirSlideJoint child)
                    {
                        child.SamplingHeight = basis[n][4];
                    }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw MargreteInput.Invalid("xbh1", "no trailing metadata bytes");
            }
        }
        catch (EndOfStreamException)
        {
            throw MargreteInput.Invalid("xbh1", "complete height basis");
        }
    }

    private static int Count(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count is < 0 or > 1_000_000)
        {
            throw MargreteInput.Invalid(count, "bounded metadata count");
        }

        return count;
    }

    private static int[][] ReadNodes(BinaryReader reader)
    {
        var count = Count(reader);
        if ((long)count * 28 > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw MargreteInput.Invalid("xbh1", "complete node identities");
        }

        return Enumerable.Range(0, count).Select(_ => Enumerable.Range(0, 7).Select(_ => reader.ReadInt32()).ToArray()).ToArray();
    }

    private static IEnumerable<U.Note> Points(U.Note root) => new[] { root }.Concat(root.Children);
    internal static string Key(U.Note root) => string.Join(';', Points(root).Select(n => string.Create(CultureInfo.InvariantCulture,
        $"{n.GetType().Name},{n.Tick.Original},{n.Lane},{n.Width},{n.Timeline},{ExactHeight(n)},{CurveExpansion.IsControl(n)},{Attributes(n)}")));
    private static decimal ExactHeight(U.Note n) => n switch
    {
        U.AirSlide a => a.Height,
        U.AirSlideJoint a => a.Height,
        _ => 80
    };
    private static string Attributes(U.Note n) => n switch
    {
        U.Slide s => $"{s.NoLine},{s.Effect}",
        U.SlideJoint s => $"{s.Joint},{s.NoLine},{s.HasEffectOverride},{s.SegmentEffect}",
        U.AirSlide s => s.Color.ToString(),
        U.AirSlideJoint s => s.Joint.ToString(),
        _ => ""
    };
    private static int Height(U.Note n) => n switch
    {
        U.AirSlide a => (int)a.Height,
        U.AirSlideJoint a => (int)a.Height,
        _ => 80
    };
    private static int Mode(U.Note n) => n switch
    {
        U.SlideJoint a => (int)a.CurveInterpretation,
        U.AirSlideJoint a => (int)a.CurveInterpretation,
        _ => 0
    };
    private static int? Basis(U.Note n) => n switch
    {
        U.AirSlide a => a.SamplingHeight,
        U.AirSlideJoint a => a.SamplingHeight,
        _ => null
    };

    internal static void CaptureProfiles(U.Chart chart) => chart.Extras.CurveProfiles = chart.Notes.Children
        .Where(n => n is U.Slide or U.AirSlide)
        .Select(n => new CurvePathProfile(Key(n), Points(n).Select(Mode).ToList(), Points(n).Select(Basis).ToList())).ToList();

    internal static void RestoreProfiles(U.Chart chart)
    {
        var profiles = chart.Extras.CurveProfiles.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => new Queue<CurvePathProfile>(g));
        foreach (var root in chart.Notes.Children.Where(n => n is U.Slide or U.AirSlide))
        {
            if (!profiles.TryGetValue(Key(root), out var saved) || saved.Count == 0)
            {
                continue;
            }

            var profile = saved.Dequeue();
            var points = Points(root).ToArray();
            if (profile.Modes.Count != points.Length || profile.Heights.Count != points.Length)
            {
                continue;
            }

            for (var i = 0; i < points.Length; i++)
            {
                var mode = profile.Modes[i];
                if (mode is < 0 or > 3)
                {
                    throw MargreteInput.Invalid(mode, "known curve interpretation");
                }

                switch (points[i])
                {
                    case U.SlideJoint s:
                        s.CurveInterpretation = (U.CurveInterpretation)mode;
                        break;
                    case U.AirSlide s:
                        s.SamplingHeight = profile.Heights[i];
                        break;
                    case U.AirSlideJoint s:
                        s.CurveInterpretation = (U.CurveInterpretation)mode;
                        s.SamplingHeight = profile.Heights[i];
                        break;
                }
            }
        }
    }

    internal static void Write(BinaryWriter writer, IReadOnlyList<U.Note> paths, ChartExtras extras, byte[] notePayload)
    {
        var ordinal = 0;
        foreach (var path in paths)
        {
            foreach (var node in path.Children.Where(CurveExpansion.IsControl))
            {
                var mode = Mode(node);
                if (mode != 0)
                {
                    writer.Write(Encoding.ASCII.GetBytes(mode == 3 ? "xbz1" : "xbc1"));
                    writer.Write((ushort)4);
                    writer.Write((ushort)(mode == 3 ? 24 : 32));
                    if (mode != 3)
                    {
                        writer.Write(ordinal);
                    }

                    writer.Write(node is U.SlideJoint ? 6 : 9);
                    writer.Write(node.Lane);
                    writer.Write(node.Width);
                    writer.Write(Height(node));
                    writer.Write(node.Tick.Original);
                    writer.Write(node.Timeline);
                    if (mode != 3)
                    {
                        writer.Write(mode);
                    }
                }
                ordinal++;
            }
        }
        var bases = paths.Select((root, index) => (Root: root, Index: index))
            .Where(p => p.Root is U.AirSlide && Points(p.Root).All(n => Basis(n).HasValue)).ToArray();
        if (bases.Length > 0)
        {
            using var stream = new MemoryStream();
            using var payload = new BinaryWriter(stream);
            payload.Write(1);
            payload.Write(bases.Length);
            foreach (var (root, index) in bases)
            {
                var points = Points(root).ToArray();
                payload.Write(index);
                WriteNodes(payload, points, basis: false);
                WriteNodes(payload, points, basis: true);
            }
            WriteChunks(writer, "xbh1", stream.ToArray());
        }
        if (extras.LockedCurves.Count > 0)
        {
            WriteChunks(writer, "xbl1", LockedCurveMetadata.Write(paths, extras, notePayload));
        }
    }

    private static void WriteNodes(BinaryWriter writer, U.Note[] points, bool basis)
    {
        writer.Write(points.Length);
        for (var i = 0; i < points.Length; i++)
        {
            var n = points[i];
            var attr = n switch
            {
                U.AirSlide => 1,
                U.AirSlideJoint { IsCurveControl: true } => 4,
                U.AirSlideJoint { Joint: Models.Joint.D } => i == points.Length - 1 ? 5 : 2,
                _ => i == points.Length - 1 ? 6 : 3
            };
            foreach (var value in new[] { 9, attr, n.Lane, n.Width, basis ? Basis(n)!.Value : Height(n), n.Tick.Original, n.Timeline })
            {
                writer.Write(value);
            }
        }
    }

    private static void WriteChunks(BinaryWriter writer, string name, byte[] bytes)
    {
        if (bytes.Length > 64 * 1024 * 1024)
        {
            throw MargreteInput.Invalid(name, "bounded curve metadata");
        }

        for (var offset = 0; offset < bytes.Length; offset += 60000)
        {
            var count = Math.Min(60000, bytes.Length - offset);
            writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write((ushort)4);
            writer.Write((ushort)(count + 8));
            writer.Write(bytes.Length);
            writer.Write(offset);
            writer.Write(bytes, offset, count);
        }
    }
}
