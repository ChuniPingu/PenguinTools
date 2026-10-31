using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.IO;
using PenguinTools.Core.Metadata;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Writer.ugc;

/// <summary>Writes UGC v8 using the selected editor coordinate dialect.</summary>
public sealed class UgcChartWriter(string path, U.Chart chart, ChartDialect dialect = ChartDialect.Auto)
{
    private readonly List<string> _lines = [];
    private readonly HashSet<U.Note> _written = [];
    private readonly HashSet<(int Tick, int Lane, int Width)> _effectCarriers = [];
    private ChartDialect _dialect;
    private string Position(int tick)
    {
        if (_dialect != ChartDialect.Original)
        {
            return $"{tick / 1920}'{tick % 1920}";
        }
        var beat = chart.Events.Children.OfType<U.BeatEvent>().Where(b => b.Tick.Original <= tick).MaxBy(b => b.Bar);
        var start = beat?.Tick.Original ?? 0;
        var length = beat is null ? 1920 : 1920 * beat.Numerator / beat.Denominator;
        return $"{(beat?.Bar ?? 0) + (tick - start) / length}'{(tick - start) % length}";
    }
    private static string Clean(string value) => ChartExtras.CleanEditorText(value);
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
        Color.PNK => 'Y',
        Color.VLT => 'B',
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
        Color.GRY => 'C',
        Color.BLK => 'D',
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
            _dialect = EditorCompatibility.OutputDialect(dialect, chart.Extras);
            EditorCompatibility.Validate(chart, _dialect, ugc: true);
            chart.Extras.ApplyEditorMode(dialect, chart.Meta);
            chart.Extras.EncodingDialect = _dialect;
            chart.Extras.FixedZeroOrigin |= chart.Extras.UsesFixedCoordinateOrigin;
            chart.Extras.CarrierEncodingVersion = 1;
            chart.Extras.EditorFieldColorName = chart.Meta.FieldColorName;
            _lines.Clear();
            _lines.Add(EditorCompatibility.UgcMarker + (_dialect == ChartDialect.Original ? "original" : "extended"));
            if (chart.Extras.FixedZeroOrigin)
            {
                _lines.Add("'" + ChartExtras.FixedZeroOriginMarker);
            }
            if (chart.Extras.PrerollBar1Applied)
            {
                _lines.Add("'" + ChartExtras.PrerollBar1Marker);
            }
            _lines.AddRange(chart.Extras.EditorComments);
            _lines.AddRange(chart.Extras.EditorHeaders);
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
            chart.Extras.CaptureEditorEvents(chart);
            chart.Extras.CaptureUserText(m);
            chart.Extras.RoundTripBookmarks = C2sRoundTripComment.FormatBookmarks(m).ToList();
            chart.Extras.UgcContentKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', _lines))));
            var copyright = $"@COPYRIGHT\tMGR_SYNC_REV={Guid.NewGuid():N};MGR_CLKCNT={chart.Extras.Count(m.BgmInitialNumerator)};" +
                (chart.Extras.SourceSnapshot.Length > 0 ? C2SRoundTrip.Pack(chart.Extras.SourceSnapshot) : "") + chart.Extras.ToCopyright() + chart.Extras.CopyrightWithTiming(Clean(m.Copyright));
            _lines.Insert(0, copyright);
            await AtomicFile.WriteAsync(path, async (stream, token) =>
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                foreach (var line in _lines)
                {
                    await writer.WriteLineAsync(line.AsMemory(), token);
                }
                await writer.FlushAsync(token);
            }, ct);
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
        _lines.Add($"@EXVER\t{m.ExVersion}");
        _lines.Add($"@FLAG\tEXLONG\t{(m.ExLongEnabled ? "TRUE" : "FALSE")}");
        _lines.Add($"@FLAG\tBGMWCMP\t{(m.WaitBgmEnd ? "TRUE" : "FALSE")}");
        _lines.Add($"@LEVEL\t{(m.Difficulty == Difficulty.WorldsEnd
            ? PenguinTools.Chart.Writer.mgxc.MgxcChartWriter.WeLevel(m.WeDifficulty)
            : PenguinTools.Chart.Writer.mgxc.MgxcChartWriter.FormatPlayLevel(m.Level))}");
        _lines.AddRange(["@VER\t8", "@FLAG\tHIPRECISION\tTRUE", "@TICKS\t480", $"@TITLE\t{Clean(m.Title)}", $"@ARTIST\t{Clean(m.Artist)}",
            $"@DESIGN\t{Clean(m.Designer)}", $"@DIFF\t{DifficultyValue(m.Difficulty)}", $"@CONST\t{m.Level}", $"@SONGID\t{(string.IsNullOrWhiteSpace(m.MgxcId) ? (m.Id ?? 0).ToString(CultureInfo.InvariantCulture) : Clean(m.MgxcId))}",
            $"@MAINBPM\t{m.MainBpm}", $"@FLAG\tSOFFSET\t{(m.BgmEnableBarOffset ? 1 : 0)}",
            $"@FLAG\tCLICK\t{(chart.Extras.ClickEnabled ? 1 : 0)}", $"@CLKCNT\t{chart.Extras.Count(m.BgmInitialNumerator)}",
            $"@FLAG\tDIFFTTL\t{(chart.Extras.Tutorial ? 1 : 0)}"]);
        _lines.AddRange([$"@SORT\t{Clean(m.SortName)}", $"@BGM\t{Clean(m.BgmFilePath)}",
            $"@BGMOFS\t{m.BgmManualOffset}", $"@JACKET\t{Clean(m.JacketFilePath)}",
            $"@MAINTIL\t{m.MainTil}", $"@CMT\t{Clean(C2sRoundTripComment.Strip(m.Comment))}"]);
        _lines.Add($"@LICENSE\t{Clean(m.License)}\t{Clean(m.LicenseUrl)}");
        _lines.Add($"@GENRE\t{Clean(m.Genre?.Str ?? "")}");
        _lines.Add($"@WEATTR\t{Clean(m.WeTag.Str)}");
        _lines.Add($"@BGMPRV\t{m.BgmPreviewStart}\t{m.BgmPreviewStop}");
        _lines.Add($"@BGIMG\t{Clean(m.BgiFilePath)}");
        _lines.Add($"@BGSCENE\t{Clean(m.BackgroundScene)}");
        _lines.Add($"@BGMODE\t{Clean(chart.Extras.EditorBackgroundMode)}\t{(m.BackgroundSync ? "FALSE" : "TRUE")}");
        _lines.Add($"@FLDCOL\t{m.FieldColorIndex}");
        _lines.Add($"@FLDIMG\t{Clean(m.FieldImage)}");
        _lines.Add($"@FLDSCENE\t{Clean(m.FieldScene)}");
        _lines.Add($"@ATINFO\tAUTHORS\t{m.Authors.Replace('\r', ' ').Replace('\n', ' ')}");
        _lines.Add($"@ATINFO\tSITES\t{m.AuthorSites.Replace('\r', ' ').Replace('\n', ' ')}");
        _lines.Add($"@DLURL\t{Clean(m.DownloadUrl)}");
    }

    private string ExTapRoleSuffix(U.ExTapRole role) => _dialect == ChartDialect.Original ? "" : role switch
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

    private string NotePayload(U.Note n, string xy)
    {
        return n switch
        {
            U.ExTap x => $"x{xy}{Effect(x.Effect)}{ExTapRoleSuffix(x.Role)}",
            U.Tap => "t" + xy,
            U.Flick f => "f" + xy + f.AutoplayDirection,
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
            _lines.Add($"@USETIL\t{child.Timeline}");
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
            Head(n, $"x{xy}{Effect(effect)}{(_dialect == ChartDialect.Original ? "" : "~")}");
        }

        var payload = NotePayload(n, xy);
        if (_dialect != ChartDialect.Original && n is U.AirHold ah)
        {
            Head(n, $"a{xy}{Direction(ah.Direction)}{AirColor(ah.Color)}");
        }

        if (_dialect != ChartDialect.Original && n is U.AirSlide air)
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
