using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Writer.ugc;

/// <summary>UGC v8 on the editor's fixed 1920-tick measure axis.</summary>
public sealed class UgcChartWriter(string path, U.Chart chart)
{
    private readonly List<string> _lines = [];
    private readonly HashSet<U.Note> _written = [];
    private readonly HashSet<(int Tick, int Lane, int Width)> _effectCarriers = [];
    private static string Position(int tick) => $"{tick / 1920}'{tick % 1920}";
    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    private static char Coordinate(int value) => value switch
    {
        >= 0 and < 36 => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[value],
        >= -256 and < 0 => (char)(0xE000 - value - 1),
        >= 36 and < 292 => (char)(0xE100 + value - 36),
        >= -2048 and < -256 => (char)(0xE200 - value - 257),
        >= 292 and <= 4387 => (char)(0xE900 + value - 292),
        _ => throw new FormatException($"UGC coordinate is outside its encoding range: {value}")
    };
    private static string Height(decimal height)
    {
        if (height != decimal.Truncate(height))
        {
            throw new FormatException("UGC requires integral native height units.");
        }

        var high = (int)Math.Floor(height / 36);
        return $"{Coordinate(high)}{Coordinate((int)height - high * 36)}";
    }
    private static char Effect(ExEffect effect) => effect switch
    {
        ExEffect.DW => 'D',
        ExEffect.CE => 'C',
        ExEffect.LS => 'L',
        ExEffect.RS => 'R',
        ExEffect.LC => 'A',
        ExEffect.RC => 'W',
        ExEffect.BS => 'I',
        _ => 'U'
    };
    private static char AirColor(Color color) => color switch
    {
        Color.PNK => 'I',
        Color.NON => 'Z',
        Color.GRN => 'G',
        Color.LIM => 'M',
        Color.RED => 'R',
        Color.BLK => 'K',
        Color.VLT => 'V',
        Color.BLU => 'B',
        Color.DGR => 'D',
        Color.AQA => 'A',
        Color.CYN => 'C',
        Color.YEL => 'Y',
        Color.ORN => 'O',
        Color.GRY => 'H',
        Color.PPL => 'P',
        _ => 'N'
    };
    private static char CrashColor(Color color) => color switch
    {
        Color.RED => '1',
        Color.ORN => '2',
        Color.YEL => '3',
        Color.LIM => '4',
        Color.GRN => '5',
        Color.AQA => '6',
        Color.CYN => '7',
        Color.DGR => '8',
        Color.BLU => '9',
        Color.PPL => 'A',
        Color.VLT => 'B',
        Color.GRY => 'C',
        Color.BLK => 'D',
        Color.PNK => 'E',
        Color.NON => 'Z',
        _ => '0'
    };
    private static string Direction(AirDirection direction) => direction switch
    {
        AirDirection.UL => "UL",
        AirDirection.UR => "UR",
        AirDirection.DW => "DC",
        AirDirection.DL => "DL",
        AirDirection.DR => "DR",
        _ => "UC"
    };
    private void Head(U.Note note, string payload) => _lines.Add($"#{Position(note.Tick.Original)}:{payload}");

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            _lines.Clear();
            _written.Clear();
            _effectCarriers.Clear();
            foreach (var carrier in chart.Notes.Children.OfType<U.ExTap>().Where(x => x.Role != U.ExTapRole.Explicit))
            {
                _effectCarriers.Add((carrier.Tick.Original, carrier.Lane, carrier.Width));
            }

            var m = chart.Meta;
            AppendMetadata(m);
            foreach (var e in chart.Events.Children)
            {
                var line = e switch
                {
                    U.BeatEvent b => $"@BEAT\t{b.Bar}\t{b.Numerator}\t{b.Denominator}",
                    U.BpmEvent b => $"@BPM\t{Position(b.Tick.Original)}\t{b.Bpm}",
                    U.ScrollSpeedEvent s => $"@TIL\t{s.Timeline}\t{Position(s.Tick.Original)}\t{s.Speed}",
                    U.NoteSpeedEvent s => $"@SPDMOD\t{Position(s.Tick.Original)}\t{s.Speed}",
                    _ => null
                };
                if (line is not null)
                {
                    _lines.Add(line);
                }
            }
            foreach (var n in chart.Notes.Children.Where(n => n is not U.NegativeNote and not U.SoflanArea).OrderBy(n => n.Tick.Original).ThenBy(n => n.Lane).ThenBy(n => n.Width))
            {
                WriteNote(n);
            }

            foreach (var n in chart.Notes.Children.OfType<U.NegativeNote>())
            {
                WriteNote(n);
            }

            foreach (var tick in chart.Extras.ClickTicks)
            {
                _lines.Add($"#{Position(tick)}:c");
            }

            chart.Extras.CaptureSlideEffects(chart);
            chart.Extras.RoundTripBookmarks = C2sRoundTripComment.FormatBookmarks(m).ToList();
            chart.Extras.UgcContentKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', _lines))));
            var copyright = $"@COPYRIGHT\tMGR_SYNC_REV={Guid.NewGuid():N};MGR_CLKCNT={chart.Extras.Count(m.BgmInitialNumerator)};" +
                (chart.Extras.SourceSnapshot.Length > 0 ? C2SRoundTrip.Pack(chart.Extras.SourceSnapshot) : "") + chart.Extras.ToCopyright();
            _lines.Insert(0, copyright);
            await File.WriteAllLinesAsync(path, _lines, new UTF8Encoding(false), ct);
            return OperationResult.Success();
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    private static int DifficultyValue(Difficulty difficulty) => difficulty switch
    {
        Difficulty.WorldsEnd => 4,
        Difficulty.Ultima => 5,
        _ => (int)difficulty
    };

    private void AppendMetadata(Meta m)
    {
        _lines.AddRange(["@VER\t8", "@TICKS\t480", $"@TITLE\t{Clean(m.Title)}", $"@ARTIST\t{Clean(m.Artist)}",
            $"@DESIGN\t{Clean(m.Designer)}", $"@DIFF\t{DifficultyValue(m.Difficulty)}", $"@CONST\t{m.Level}", $"@SONGID\t{m.Id ?? 0}",
            $"@MAINBPM\t{m.MainBpm}", $"@FLAG\tSOFFSET\t{(m.BgmEnableBarOffset ? 1 : 0)}",
            $"@FLAG\tCLICK\t{(chart.Extras.ClickEnabled ? 1 : 0)}", $"@CLKCNT\t{chart.Extras.Count(m.BgmInitialNumerator)}",
            $"@FLAG\tDIFFTTL\t{(chart.Extras.Tutorial ? 1 : 0)}"]);
        _lines.AddRange([$"@SORT\t{Clean(m.SortName)}", $"@BGM\t{Clean(m.BgmFilePath)}",
            $"@BGMOFS\t{m.BgmManualOffset}", $"@JACKET\t{Clean(m.JacketFilePath)}",
            $"@MAINTIL\t{m.MainTil}", $"@CMT\t{Clean(C2sRoundTripComment.Strip(m.Comment))}"]);
    }

    private static string ExTapRoleSuffix(U.ExTapRole role) => role switch
    {
        U.ExTapRole.Explicit => "!",
        U.ExTapRole.SharedLongCarrier => "~",
        _ => ""
    };

    private static string AirCrashAxisSuffix(AirLadderAttr attribute) => attribute switch
    {
        AirLadderAttr.AxisY => "Y",
        AirLadderAttr.AxisZ => "Z",
        _ => ""
    };

    private static string NotePayload(U.Note n, string xy)
    {
        return n switch
        {
            U.ExTap x => $"x{xy}{Effect(x.Effect)}{ExTapRoleSuffix(x.Role)}",
            U.Tap => "t" + xy,
            U.Flick => "f" + xy + "L",
            U.Damage => "d" + xy,
            U.Hold => "h" + xy,
            U.Slide => "s" + xy,
            U.Air a => $"a{xy}{Direction(a.Direction)}{AirColor(a.Color)}",
            U.AirHold a => "H" + xy + AirColor(a.Color),
            U.AirSlide a => "S" + xy + Height(a.Height) + AirColor(a.Color),
            U.AirCrash a => "C" + xy + Height(a.Height) + CrashColor(a.Color) + AirCrashAxisSuffix(a.Attr) + "," + (a.Density == int.MaxValue ? "$" : a.Density.Original.ToString(CultureInfo.InvariantCulture)),
            _ => throw new FormatException($"Unsupported UGC note: {n.GetType().Name}")
        };
    }

    private void WriteChildren(U.Note n)
    {
        var noLine = n is U.Slide slide && slide.NoLine;
        foreach (var child in n.Children)
        {
            var childXy = $"{Coordinate(child.Lane)}{Coordinate(child.Width)}";
            var joint = child switch
            {
                U.SlideJoint s => s.Joint,
                U.AirSlideJoint a => a.Joint,
                U.AirHoldJoint a => a.Joint,
                _ => Joint.D
            };
            var marker = joint == Joint.C ? 'c' : 's';
            if (n is U.Slide && noLine)
            {
                marker = joint == Joint.C ? 'N' : 'n';
            }

            var body = child switch
            {
                U.HoldJoint => "s",
                U.AirHoldJoint => marker.ToString(),
                U.AirSlideJoint a => $"{marker}{childXy}{Height(a.Height)}",
                U.AirCrashJoint a => $"c{childXy}{Height(a.Height)}",
                _ => $"{marker}{childXy}"
            };
            _lines.Add($"#{child.Tick.Original - n.Tick.Original}>{body}");
            _written.Add(child);
            noLine = child is U.SlideJoint slidePoint && slidePoint.NoLine;
        }
    }

    private void WriteNote(U.Note n)
    {
        if (!_written.Add(n))
        {
            return;
        }

        if (n is U.NegativeNote negative && negative.PairNote is { } parent && !_written.Contains(parent))
        {
            WriteNote(parent.Parent is U.Note root ? root : parent);
        }

        _lines.Add($"@USETIL\t{n.Timeline}");
        var xy = $"{Coordinate(n.Lane)}{Coordinate(n.Width)}";
        if (n is U.ExTapableNote ex && ex.Effect is { } effect && _effectCarriers.Add((n.Tick.Original, n.Lane, n.Width)))
        {
            Head(n, $"x{xy}{Effect(effect)}~");
        }

        var payload = NotePayload(n, xy);
        if (n is U.AirHold ah)
        {
            Head(n, $"a{xy}{Direction(ah.Direction)}{AirColor(ah.Color)}");
        }

        if (n is U.AirSlide air)
        {
            Head(n, $"a{xy}{Direction(air.Direction)}{AirColor(air.Color)}");
        }

        Head(n, payload);
        WriteChildren(n);
        foreach (var point in new[] { n }.Concat(n.Children))
        {
            if (point is U.PositiveNote positive && positive.PairNote is { } paired)
            {
                WriteNote(paired);
            }
        }
    }
}
