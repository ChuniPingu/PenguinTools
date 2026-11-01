namespace PenguinTools.Chart;

public static partial class ChartStatistics
{
    private sealed partial class Calculator
    {
        private readonly record struct Cell(decimal Tick, int Lane, int Width);
        private sealed record Segment(Cell Start, Cell End, bool Judge, decimal StartHeight = 0, decimal EndHeight = 0);
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
        }

        private static string Kind(string tag) => tag switch
        {
            "HXD" => "HLD",
            "SLC" or "SXC" or "SXD" => "SLD",
            "AUL" or "AUR" or "ADW" or "ADR" or "ADL" => "AIR",
            "AHX" or "ASX" => "AHD",
            "ASC" => "ASD",
            _ => tag
        };

        private static Cell At(decimal tick, string[] p, int lane, int width) =>
            new(tick, I(p, lane), Math.Clamp(I(p, width), 1, 16));
        private static decimal Height(string[] p, int index) => (decimal)MathF.Truncate((float)N(p, index) * 10);

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
                        _notes.Add(new Note(kind, start));
                        break;
                    case "HLD":
                        var hold = new Note(kind, start);
                        hold.Path.Add(new(start, start with { Tick = start.Tick + N(p, 5) }, true));
                        _notes.Add(hold);
                        break;
                    case "SLD":
                        ReadSlide(p, start);
                        break;
                    case "ALD":
                        ReadCrash(p, start);
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
            var path = _notes.FirstOrDefault(n => n.Kind == "SLD" && n.NoLine == noLine && n.End == start);
            if (path is null)
            {
                path = new Note("SLD", start) { NoLine = noLine };
                _notes.Add(path);
            }
            path.Path.Add(new(start, end, p[0].EndsWith('D')));
        }

        private void ReadCrash(string[] p, Cell start)
        {
            var end = At(start.Tick + N(p, 7), p, 8, 9);
            var height = Height(p, 6);
            var density = N(p, 5);
            var color = p.Length > 11 ? p[11] : "DEF";
            var path = _notes.FirstOrDefault(n => n.Kind == "ALD" && n.Density == density &&
                n.Color == color && n.End == start && n.Path[^1].EndHeight == height);
            if (path is null)
            {
                path = new Note("ALD", start) { Density = density, Color = color };
                _notes.Add(path);
            }
            path.Path.Add(new(start, end, false, height, Height(p, 10)));
        }

        private void Attach(string parent, Air air)
        {
            var note = _notes.FirstOrDefault(n => n.Kind == Kind(parent) &&
                n.End == air.Path[0].Start && n.Air is null);
            if (note is not null)
            {
                note.Air = air;
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

            var parent = _notes.FirstOrDefault(n => n.Air is { } air && air.Kind == kind &&
                air.Path[^1].End == start && (!slide ||
                    air.Path[^1].EndHeight == segment.StartHeight && air.Path[^1].Judge == (p[5] == "ASD")));
            parent?.Air!.Path.Add(segment);
        }
    }
}
