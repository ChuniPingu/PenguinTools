using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PenguinTools.Chart.Parser.mgxc;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

// Roles are converter metadata. A role is restored only while its native block
// and all records at its tick still match, including overlapping EX coverage.
internal static class MgxcNoteRoles
{
    private sealed record Record(byte[] Bytes, int Block, int Index, int Version)
    {
        internal int Tick => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(12));
        internal bool IsExTap => (NoteType)Bytes[0] == NoteType.ExTap;
        internal bool IsLongHead => (NoteType)Bytes[0] is NoteType.Hold or NoteType.Slide && (LongAttr)Bytes[1] == LongAttr.Begin;
        internal int Lane => unchecked((sbyte)Bytes[Version >= 4 ? 7 : 5]);
        internal int Width => Version >= 3 ? Bytes[6] : BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(6));
    }

    internal static List<EditorNoteSnapshot> Capture(ReadOnlySpan<byte> payload,
        IReadOnlyList<ExTapRoleSnapshot> roles, int version = 4)
    {
        var records = Read(payload, version);
        var exTaps = records.Where(r => r.IsExTap).ToArray();
        if (exTaps.Length != roles.Count)
        {
            throw new InvalidOperationException("MGXC EXTAP metadata does not match the emitted notes.");
        }
        var keys = Keys(records);
        var heads = records.Where(r => r.IsLongHead).ToLookup(r => r.Tick);
        List<EditorNoteSnapshot> snapshots = [];
        for (var i = 0; i < exTaps.Length; i++)
        {
            var note = exTaps[i];
            var inferred = heads[note.Tick].Any(head => note.Lane <= head.Lane &&
                note.Lane + note.Width >= head.Lane + head.Width)
                ? U.ExTapRole.SharedLongCarrier : U.ExTapRole.Explicit;
            if (roles[i].Role != U.ExTapRole.Auto && roles[i].Role != inferred)
            {
                snapshots.Add(new(keys[note.Index], roles[i]));
            }
        }
        return snapshots;
    }

    internal static void Restore(ReadOnlySpan<byte> payload, IReadOnlyList<EditorNoteSnapshot> snapshots,
        IEnumerable<U.ExTap> notes)
    {
        if (snapshots.Count == 0)
        {
            return;
        }
        var records = Read(payload);
        var keys = Keys(records);
        var saved = snapshots.GroupBy(s => s.Key).ToDictionary(g => g.Key, g => new Queue<EditorNoteSnapshot>(g));
        foreach (var (record, note) in records.Where(r => r.IsExTap).Zip(notes))
        {
            if (!saved.TryGetValue(keys[record.Index], out var queue) ||
                !queue.TryDequeue(out var snapshot) || snapshot.ExTap is not { } role)
            {
                continue;
            }
            note.Role = role.Role;
            note.AirActionParent = role.Parent;
            note.AirActionParentJoint = role.ParentJoint;
            note.AirActionParentIsEx = role.ParentHasExEffect;
        }
    }

    private static List<Record> Read(ReadOnlySpan<byte> payload, int version = 4)
    {
        List<Record> records = [];
        var block = -1;
        while (!payload.IsEmpty)
        {
            var length = payload.Length >= 2 && (NoteType)payload[0] == NoteType.AirCrush &&
                (LongAttr)payload[1] == LongAttr.Begin ? 24 : 20;
            if (payload.Length < length)
            {
                throw new FormatException("Incomplete MGXC note record.");
            }
            if ((LongAttr)payload[1] is LongAttr.None or LongAttr.Begin || block < 0)
            {
                block++;
            }
            records.Add(new(payload[..length].ToArray(), block, records.Count, version));
            payload = payload[length..];
        }
        return records;
    }

    private static string[] Keys(List<Record> records)
    {
        var blocks = records.GroupBy(r => r.Block).ToDictionary(g => g.Key,
            g => Hash(string.Join('\n', g.Select(r => Convert.ToHexString(r.Bytes)))));
        var keys = new string[records.Count];
        foreach (var tick in records.GroupBy(r => r.Tick))
        {
            var group = Hash(string.Join('\n', tick.Select(r => blocks[r.Block] + ":" + Convert.ToHexString(r.Bytes))));
            var ordinal = 0;
            foreach (var record in tick)
            {
                keys[record.Index] = group + ":" + (ordinal++).ToString(CultureInfo.InvariantCulture);
            }
        }
        return keys;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
