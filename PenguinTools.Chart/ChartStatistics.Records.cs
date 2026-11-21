namespace PenguinTools.Chart;

public static partial class ChartStatistics
{
    private sealed partial class Calculator
    {
        private readonly Dictionary<Cell, SortedSet<int>> _groundEnds = [];
        private readonly Dictionary<decimal, SortedSet<int>> _airEnds = [];

        private readonly record struct Cell(decimal Tick, int Lane, int Width);
        private sealed record Segment(Cell Start, Cell End, bool Judge, int StartHeight = 0, int EndHeight = 0,
            int EndHeight2 = 0);
        private sealed class Air(string kind, Segment first)
        {
            internal string Kind { get; } = kind;
            internal List<Segment> Path { get; } = [first];
        }
        private sealed class Note(string kind, Cell start)
        {
            internal string Kind { get; } = kind;
            internal Cell Start { get; } = start;
            internal Cell End => Path.Count == 0 ? Start : Path[^1].End;
            internal List<Segment> Path { get; } = [];
            internal Air? Air { get; set; }
            internal bool NoLine { get; init; }
            internal decimal Density { get; init; }
            internal string Color { get; init; } = "DEF";
            internal int HeavenFlags { get; init; }
            internal string? ExEffect { get; init; }
        }

        private static string Kind(string tag) => tag switch
        {
            "HXD" => "HLD",
            "HHX" => "HHD",
            "SLC" or "SXC" or "SXD" => "SLD",
            "AUL" or "AUR" or "ADW" or "ADR" or "ADL" => "AIR",
            "AHX" => "AHD",
            "ASC" => "ASD",
            _ => tag
        };

        private static Cell At(decimal tick, string[] p, int lane, int width) =>
            new(tick, I(p, lane), Math.Clamp(I(p, width), 1, 16));
        private static int Height(string[] p, int index) => C2sHeight.Key(N(p, index));

        private int FindEnd<TKey>(Dictionary<TKey, SortedSet<int>> ends, TKey key, Func<Note, bool> matches)
            where TKey : notnull
        {
            // A continuation may move an older root to a cell that already
            // contains a newer root. Keep the statement order of the roots.
            return ends.TryGetValue(key, out var roots)
                ? roots.FirstOrDefault(index => matches(_notes[index]), -1)
                : -1;
        }

        private static void AddEnd<TKey>(Dictionary<TKey, SortedSet<int>> ends, TKey key, int index)
            where TKey : notnull
        {
            if (!ends.TryGetValue(key, out var roots))
            {
                ends.Add(key, roots = []);
            }
            roots.Add(index);
        }

        private static void MoveEnd<TKey>(Dictionary<TKey, SortedSet<int>> ends, TKey from, TKey to, int index)
            where TKey : notnull
        {
            if (EqualityComparer<TKey>.Default.Equals(from, to))
            {
                return;
            }
            var roots = ends[from];
            roots.Remove(index);
            if (roots.Count == 0)
            {
                ends.Remove(from);
            }
            AddEnd(ends, to, index);
        }

        private int AddNote(Note note)
        {
            var index = _notes.Count;
            _notes.Add(note);
            AddEnd(_groundEnds, note.End, index);
            return index;
        }

        private void AppendGroundSegment(int index, Segment segment)
        {
            var note = _notes[index];
            MoveEnd(_groundEnds, note.End, segment.End, index);
            note.Path.Add(segment);
        }

        private void ReadNotes()
        {
            // Attachment and continuation are decided as each statement arrives.
            // A later parent, a consumed parent, or an unknown continuation cannot
            // contribute an AIR judgement.
            foreach (var p in _rows.Where(p => ChartExtras.NoteTags.Contains(p[0])))
            {
                var kind = Kind(p[0]);
                var start = At(Tick(p), p, 3, 4);
                switch (kind)
                {
                    case "TAP" or "CHR" or "FLK" or "MNE":
                        AddNote(new Note(kind, start));
                        break;
                    case "HLD" or "SLA":
                        var span = new Note(kind, start);
                        span.Path.Add(new(start, start with { Tick = start.Tick + N(p, 5) }, kind == "HLD"));
                        AddNote(span);
                        break;
                    case "SLD":
                        ReadSlide(p, start);
                        break;
                    case "ALD":
                        ReadCrash(p, start);
                        break;
                    case "ASO" or "HHD":
                        ReadHeightSpan(p, start, kind);
                        break;
                    case "AIR":
                        Attach(p[5], new Air(kind, new(start, start, true)));
                        break;
                    case "AHD" or "ASD":
                        ReadLongAir(p, start, kind);
                        break;
                }
            }
        }

        private void ReadSlide(string[] p, Cell start)
        {
            var end = At(start.Tick + N(p, 5), p, 6, p.Length > 7 ? 7 : 4);
            var noLine = p.Length > 8 && p[8] == "NCL";
            var index = FindEnd(_groundEnds, start, n => n.Kind == "SLD" && n.NoLine == noLine);
            if (index < 0)
            {
                index = AddNote(new Note("SLD", start) { NoLine = noLine });
            }
            AppendGroundSegment(index, new(start, end, p[0].EndsWith('D')));
        }

        private void ReadCrash(string[] p, Cell start)
        {
            var end = At(start.Tick + N(p, 7), p, 8, 9);
            var height = Height(p, 6);
            var density = N(p, 5);
            var color = LadderColor(p, 11);
            var index = FindEnd(_groundEnds, start, n => n.Kind == "ALD" && n.Density == density &&
                n.Color == color && n.Path[^1].EndHeight == height);
            if (index < 0)
            {
                index = AddNote(new Note("ALD", start) { Density = density, Color = color });
            }
            AppendGroundSegment(index, new(start, end, false, height, Height(p, 10)));
        }

        private void ReadHeightSpan(string[] p, Cell start, string kind)
        {
            var solid = kind == "ASO";
            var duration = C2SRoundTrip.DurationIndex(p[0]);
            var end = At(start.Tick + N(p, duration), p, duration + 1, duration + 2);
            var height = Height(p, 5);
            var height2 = solid ? Height(p, 6) : 0;
            var endHeight = Height(p, duration + 3);
            var endHeight2 = solid ? Height(p, 11) : 0;
            var color = solid ? LadderColor(p, 12) : "DEF";
            var flags = solid ? 0 : I(p, 10);
            var effect = p[0] != "HHX" ? null
                : p.Length > 11 && p[11] is "AxisY" or "AxisZ" or "Trace" ? p[11] : "DEF";
            var index = FindEnd(_groundEnds, start, n => n.Kind == kind && n.Color == color &&
                n.HeavenFlags == flags && n.ExEffect == effect &&
                n.Path[^1].EndHeight == height && n.Path[^1].EndHeight2 == height2);
            if (index < 0)
            {
                index = AddNote(new Note(kind, start) { Color = color, HeavenFlags = flags, ExEffect = effect });
            }
            AppendGroundSegment(index, new(start, end, !solid, height, endHeight, endHeight2));
        }

        private static string LadderColor(string[] p, int column) =>
            p.Length > column && p[column] is "RED" or "ORN" or "YEL" or "LIM" or "GRN" or "AQA" or
                "CYN" or "DGR" or "BLU" or "PPL" or "VLT" or "PNK" or "GRY" or "BLK" or "NON" ? p[column] : "DEF";

        private void Attach(string parent, Air air)
        {
            var kind = Kind(parent);
            var index = FindEnd(_groundEnds, air.Path[0].Start, n => n.Kind == kind && n.Air is null);
            if (index >= 0)
            {
                _notes[index].Air = air;
                if (air.Kind != "AIR")
                {
                    AddEnd(_airEnds, air.Path[^1].End.Tick, index);
                }
            }
        }

        private void ReadLongAir(string[] p, Cell start, string kind)
        {
            var slide = kind == "ASD";
            var end = slide ? At(start.Tick + N(p, 7), p, 8, 9) : start with { Tick = start.Tick + N(p, 6) };
            var segment = new Segment(start, end, p[0].EndsWith('D'),
                slide ? Height(p, 6) : 0, slide ? Height(p, 10) : 0);
            var continuation = slide ? p[5] is "ASC" or "ASD" : Kind(p[5]) == "AHD";
            if (!continuation)
            {
                Attach(p[5], new Air(kind, segment));
                return;
            }

            var index = FindEnd(_airEnds, start.Tick, n => n.Air is { } air && air.Kind == kind && (slide
                    ? air.Path[^1].End == start && air.Path[^1].EndHeight == segment.StartHeight &&
                        air.Path[^1].Judge == (p[5] == "ASD")
                    : n.End.Lane == start.Lane && n.End.Width == start.Width));
            if (index >= 0)
            {
                MoveEnd(_airEnds, start.Tick, segment.End.Tick, index);
                _notes[index].Air!.Path.Add(segment);
            }
        }
    }
}
