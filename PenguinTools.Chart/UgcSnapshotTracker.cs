using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

// Native note blocks are checked together with notes that can affect their
// pairing or EX coverage. Presentation metadata does not participate.
internal sealed class UgcSnapshotTracker
{
    private sealed record Entry(U.Note Note, List<string> Lines);
    private readonly List<Entry> _entries = [];
    private Entry? _current;

    // Height digits per AIR-SLIDE/AIR-CRUSH head, set by the parser from HIPRECISION.
    internal int HeightDigits { get; set; } = 1;

    internal void Begin(U.Note note, string line, int timeline)
    {
        _current = new(note, [Token(line, timeline, HeightDigits)]);
        _entries.Add(_current);
    }

    internal void Append(string line, int timeline) => _current?.Lines.Add(Token(line, timeline, HeightDigits));

    private static string Token(string line, int timeline, int heightDigits) =>
        string.Create(CultureInfo.InvariantCulture, $"{timeline}:{CanonicalNote(line, heightDigits)}");

    // Widths and AIR heights are base36 and read case-insensitively, so native
    // saves may change only their case. Lanes (v9 base62) and effect, direction
    // and color codes stay case-sensitive.
    private static string CanonicalNote(string line, int heightDigits)
    {
        var separator = line.StartsWith('#') ? line.IndexOfAny([':', '>']) : -1;
        if (separator < 0 || line.Length <= separator + 3)
        {
            return line;
        }
        var chars = line.ToCharArray();
        var body = separator + 1;
        if (line.AsSpan(1, separator - 1).IndexOf('\'') < 0)
        {
            // Child nodes: type, lane, then only width and height digits.
            for (var i = body + 2; i < chars.Length; i++)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
            }
            return new string(chars);
        }
        if ("txfdhsaHSC".Contains(chars[body], StringComparison.Ordinal))
        {
            chars[body + 2] = char.ToUpperInvariant(chars[body + 2]);
        }
        if (chars[body] is 'S' or 'C')
        {
            for (var i = body + 3; i < Math.Min(chars.Length, body + 3 + heightDigits); i++)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
            }
        }
        return new string(chars);
    }

    private static int HeightDigitsOf(IEnumerable<string> lines) =>
        lines.Select(l => l.Split('\t')).Any(p => p.Length >= 3 && p[0].Equals("@FLAG", StringComparison.OrdinalIgnoreCase) &&
            p[1].Equals("HIPRECISION", StringComparison.OrdinalIgnoreCase) &&
            (p[2].Equals("TRUE", StringComparison.OrdinalIgnoreCase) || p[2].Trim() == "1")) ? 2 : 1;

    internal void Capture(ChartExtras extras, IEnumerable<string> lines, string? slaSnapshot)
    {
        extras.UgcSnapshotVersion = 2;
        extras.UgcContextKey = ContextKey(lines, extras);
        extras.UgcMusicalKey = MusicalKey(lines, extras);
        var keys = Keys().ToArray();
        extras.UgcSlaKey = SlaKey(keys, slaSnapshot);
        var projected = extras.EditorNoteProjections.Select(p => p.Key).ToHashSet();
        // All blocks participate in group identity; only notes with restorable
        // properties need an entry in the file.
        bool NeedsSnapshot(U.Note note) =>
            note is U.ExTap or U.ExTapableNote or U.AirSlide or U.AirHold or U.AirCrash ||
            new[] { note }.Concat(note.Children).Any(n => projected.Contains(EditorProjection.Key(n)));
        extras.UgcNoteSnapshots = keys.Where(item => NeedsSnapshot(item.Note)).Select(item => new EditorNoteSnapshot(item.Key,
            item.Note is U.ExTap ex ? new(ex.Role, ex.AirActionParent, ex.AirActionParentJoint, ex.AirActionParentIsEx) : null)).ToList();
    }

    internal (Dictionary<U.Note, EditorNoteSnapshot> Matches, bool ContextValid, bool SlaValid) Validate(
        ChartExtras extras, string[] lines, string? slaSnapshot)
    {
        var matches = new Dictionary<U.Note, EditorNoteSnapshot>();
        if (extras.UgcSnapshotVersion != 2)
        {
            extras.BinarySnapshotValid = false;
            return (matches, false, false);
        }
        extras.BinarySnapshotValid = extras.UgcMusicalKey == MusicalKey(lines, extras);
        if (extras.UgcContextKey != ContextKey(lines, extras))
        {
            return (matches, false, false);
        }
        var saved = extras.UgcNoteSnapshots.GroupBy(n => n.Key)
            .ToDictionary(g => g.Key, g => new Queue<EditorNoteSnapshot>(g));
        var keys = Keys().ToArray();
        foreach (var (note, key) in keys)
        {
            if (saved.TryGetValue(key, out var queue) && queue.TryDequeue(out var snapshot))
            {
                matches[note] = snapshot;
            }
        }
        return (matches, true, extras.UgcSlaKey is not null && extras.UgcSlaKey == SlaKey(keys, slaSnapshot));
    }

    private static string? SlaKey(IEnumerable<(U.Note Note, string Key)> keys, string? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }
        long end = int.MinValue;
        foreach (var entry in snapshot.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = entry.Split(',');
            if (fields.Length != 5 || !int.TryParse(fields[0], out var tick) || !int.TryParse(fields[4], out var length))
            {
                return null;
            }
            end = Math.Max(end, (long)tick + length);
        }
        // Main-timeline notes can also be affected by saved SLA regions. Check
        // all blocks up to their last endpoint, plus every non-main timeline.
        return Hash(snapshot + "\n" + string.Join('\n', keys.Where(item => new[] { item.Note }.Concat(item.Note.Children)
            .Any(n => n.Timeline != 0 || n.Tick.Original <= end)).Select(item => item.Key)));
    }

    private IEnumerable<(U.Note Note, string Key)> Keys()
    {
        var roots = Enumerable.Range(0, _entries.Count).ToArray();
        int Root(int i)
        {
            while (roots[i] != i)
            {
                roots[i] = roots[roots[i]];
                i = roots[i];
            }
            return i;
        }
        void Join(int a, int b) => roots[Root(b)] = Root(a);

        var heads = JoinCoincidentNotes(Join);
        JoinExCoverage(heads, Join);
        foreach (var group in Enumerable.Range(0, _entries.Count).GroupBy(Root))
        {
            var indices = group.ToArray();
            var key = Hash(string.Join('\n', indices.Select(i => Hash(string.Join('\n', _entries[i].Lines)))));
            for (var i = 0; i < indices.Length; i++)
            {
                yield return (_entries[indices[i]].Note, key + ":" + i.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private Dictionary<int, List<int>> JoinCoincidentNotes(Action<int, int> join)
    {
        var cells = new Dictionary<(int Tick, int Lane, int Width), int>();
        var heads = new Dictionary<int, List<int>>();
        for (var i = 0; i < _entries.Count; i++)
        {
            var note = _entries[i].Note;
            foreach (var point in new[] { note }.Concat(note.Children))
            {
                var cell = (point.Tick.Original, point.Lane, point.Width);
                if (cells.TryGetValue(cell, out var previous))
                {
                    join(i, previous);
                }
                else
                {
                    cells[cell] = i;
                }
            }
            if (note is not (U.ExTap or U.ExTapableNote))
            {
                continue;
            }

            if (!heads.TryGetValue(note.Tick.Original, out var atTick))
            {
                heads[note.Tick.Original] = atTick = [];
            }

            atTick.Add(i);
        }
        return heads;
    }

    private void JoinExCoverage(Dictionary<int, List<int>> heads, Action<int, int> join)
    {
        foreach (var atTick in heads.Values)
        {
            foreach (var exIndex in atTick.Where(i => _entries[i].Note is U.ExTap))
            {
                var ex = _entries[exIndex].Note;
                foreach (var longIndex in atTick.Where(i => _entries[i].Note is U.ExTapableNote))
                {
                    var note = _entries[longIndex].Note;
                    if (ex.Lane <= note.Lane && ex.Lane + (long)ex.Width >= note.Lane + (long)note.Width)
                    {
                        join(exIndex, longIndex);
                    }
                }
            }
        }
    }

    private static bool IsPresentation(string command) => command is
        "@TITLE" or "@SORT" or "@ARTIST" or "@GENRE" or "@DESIGN" or "@DIFF" or "@LEVEL" or "@CONST" or
        "@SONGID" or "@RLDATE" or "@BGM" or "@BGMOFS" or "@BGMPRV" or "@JACKET" or "@BGIMG" or
        "@BGMODE" or "@BGSCENE" or "@FLDCOL" or "@FLDIMG" or "@FLDSCENE" or "@CMT" or "@ATINFO" or
        "@DLURL" or "@COPYRIGHT" or "@LICENSE" or "@ENDHEAD";

    private static string ContextKey(IEnumerable<string> lines, ChartExtras extras)
    {
        var headers = lines.Where(l => l.StartsWith('@')).Select(l => l.Split('\t'))
            .Where(p => !IsPresentation(p[0].ToUpperInvariant()) && !p[0].Equals("@USETIL", StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                p[0] = p[0].ToUpperInvariant();
                for (var i = 1; i < p.Length; i++)
                {
                    if (bool.TryParse(p[i], out var flag))
                    {
                        p[i] = flag ? "1" : "0";
                    }
                    else if (decimal.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        p[i] = number.ToString("G29", CultureInfo.InvariantCulture);
                    }
                }
                return string.Join('\t', p);
            });
        return Hash($"{extras.FixedZeroOrigin},{extras.PrerollBar1Applied}\n" +
            string.Join('\n', headers.OrderBy(h => h.StartsWith("@FLAG\t", StringComparison.Ordinal)
                ? string.Join('\t', h.Split('\t').Take(2)) : h.Split('\t')[0], StringComparer.Ordinal)));
    }

    private static string MusicalKey(IEnumerable<string> lines, ChartExtras extras)
    {
        var timeline = 0;
        var notes = new List<string>();
        var heightDigits = HeightDigitsOf(lines);
        foreach (var line in lines)
        {
            if (line.StartsWith("@USETIL\t", StringComparison.Ordinal) && int.TryParse(line.AsSpan(8), out var value))
            {
                timeline = value;
            }
            else if (line.StartsWith('#'))
            {
                notes.Add(Token(line, timeline, heightDigits));
            }
        }
        return Hash(ContextKey(lines, extras) + "\n" + string.Join('\n', notes));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
