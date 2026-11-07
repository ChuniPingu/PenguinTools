using System.Globalization;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.IO;
using PenguinTools.Core.Metadata;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Writer.ugc;

/// <summary>Writes UGC for the requested editor capabilities.</summary>
public sealed class UgcChartWriter(string path, U.Chart source, EditorTarget target = EditorTarget.Extended)
{
    private const string FalseValue = "FALSE";

    private readonly List<string> _lines = [];
    private readonly HashSet<U.Note> _written = [];
    private readonly HashSet<(int Tick, int Lane, int Width)> _effectCarriers = [];
    private U.Chart _chart = null!;
    private UgcSnapshotTracker _snapshots = new();
    private int _rootOrdinal;
    private int _crashOrdinal;
    private string Position(int tick)
    {
        var beat = _chart.Events.Children.OfType<U.BeatEvent>().Where(b => b.Tick.Original <= tick).MaxBy(b => b.Bar);
        var start = beat?.Tick.Original ?? 0;
        var length = beat is null ? 1920 : 1920 * beat.Numerator / beat.Denominator;
        if (length == 0)
        {
            return $"{beat!.Bar}'{tick - start}";
        }
        return $"{(beat?.Bar ?? 0) + (tick - start) / length}'{(tick - start) % length}";
    }
    private static string Clean(string value) => ChartExtras.CleanEditorText(value);
    private char Coordinate(int value) => target == EditorTarget.Legacy ? Digit(value) :
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"[checked(value + 16)];
    private static char Digit(int value) => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[value];
    private static string Height(decimal height)
    {
        if (height != decimal.Truncate(height))
        {
            throw new FormatException("UGC requires integral native height units.");
        }

        var high = (int)Math.Floor(height / 36);
        return $"{Digit(high)}{Digit((int)height - high * 36)}";
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
    private static char AirColor(Color color) => color == Color.PNK ? 'I' : 'N';
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
    private void Head(U.Note note, string payload)
    {
        if (note is U.Slide slide)
        {
            var bits = SlideSegmentMetadata.Bits(slide);
            if (SlideSegmentMetadata.IsMixed(bits))
            {
                _lines.Add($"{SlideSegmentMetadata.CommentPrefix}\t{_rootOrdinal}\t{bits}");
            }
        }
        _rootOrdinal++;
        if (note is U.AirCrash crash)
        {
            if (crash.HasExplicitJudgements)
            {
                _lines.Add(LegacyCrashMetadata.Write(crash, _crashOrdinal));
            }

            _crashOrdinal++;
        }
        var line = $"#{Position(note.Tick.Original)}:{payload}";
        _lines.Add(line);
        _snapshots.Begin(note, line, note.Timeline);
    }

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var diagnostics = new DiagnosticCollector();
            try
            {
                _chart = EditorProjection.Create(source, ugc: true, diagnostics, target);
            }
            catch (DiagnosticException ex)
            {
                diagnostics.Report(ex.ToDiagnostic().WithPathFallback(path));
                return OperationResult.Failure().WithDiagnostics(diagnostics);
            }
            CurveExpansion.Expand(_chart);
            EditorProjection.CaptureAirColorKeys(_chart);
            try
            {
                EditorProjection.ValidateUgcHeights(_chart);
            }
            catch (DiagnosticException ex)
            {
                diagnostics.Report(ex.ToDiagnostic().WithPathFallback(path));
                return OperationResult.Failure().WithDiagnostics(diagnostics);
            }
            _chart.Extras.MgxcNoteSnapshots.Clear();
            _chart.Extras.FixedZeroOrigin |= _chart.Extras.UsesFixedCoordinateOrigin;
            _chart.Extras.EditorFieldColorName = _chart.Meta.FieldColorName;
            _lines.Clear();
            // Sequential readers need the version before version-dependent flags.
            _lines.Add($"@VER\t{EditorTargetRules.Version(target, ugc: true)}");
            _rootOrdinal = 0;
            _crashOrdinal = 0;
            _snapshots = new();
            // Older extended readers use the origin marker as a fallback for
            // v8 fixed-width positions. v9 output always uses musical bars.
            _lines.Add($"'{MargreteInput.DialectPrefix} original");
            if (_chart.Extras.FixedZeroOrigin)
            {
                _lines.Add("'" + ChartExtras.FixedZeroOriginMarker);
            }
            if (_chart.Extras.PrerollBar1Applied)
            {
                _lines.Add("'" + ChartExtras.PrerollBar1Marker);
            }
            _lines.AddRange(_chart.Extras.EditorComments);
            _lines.AddRange(_chart.Extras.EditorHeaders);
            _written.Clear();
            _effectCarriers.Clear();
            _chart.Extras.AirAppearances.Clear();
            foreach (var carrier in _chart.Notes.Children.OfType<U.ExTap>().Where(x => x.Role != U.ExTapRole.Explicit))
            {
                _effectCarriers.Add((carrier.Tick.Original, carrier.Lane, carrier.Width));
            }

            var m = _chart.Meta;
            AppendMetadata(m);
            AppendEvents();
            foreach (var n in _chart.Notes.Children.Where(n => n is not U.NegativeNote and not U.SoflanArea))
            {
                WriteNote(n);
            }

            foreach (var n in _chart.Notes.Children.OfType<U.NegativeNote>())
            {
                WriteNote(n);
            }

            foreach (var tick in _chart.Extras.ClickTicks)
            {
                _lines.Add($"#{Position(tick)}:c");
            }

            _chart.Extras.CaptureSlideEffects(_chart);
            _chart.Extras.CaptureEditorEvents(_chart);
            _chart.Extras.CaptureUserText(m);
            _chart.Extras.RoundTripBookmarks = C2sRoundTripComment.FormatBookmarks(m).ToList();
            _snapshots.Capture(_chart.Extras, _lines, _chart.Meta.C2sSlaSnapshot);
            var copyright = (_chart.Extras.SourceSnapshot.Length > 0 ? C2SRoundTrip.Pack(_chart.Extras.SourceSnapshot) : "") +
                _chart.Extras.ToCopyright() + _chart.Extras.CopyrightWithTiming(Clean(m.Copyright));
            // Original editors copy this value into an unsigned 16-bit MGXC
            // string field. An oversized value can corrupt their saved file.
            // Extended readers migrate round-trip payloads into bookmarks,
            // but the user's copyright still occupies a native string field.
            var copyrightBytes = Encoding.UTF8.GetByteCount(target == EditorTarget.Extended
                ? ChartExtras.CopyrightText(Clean(m.Copyright))
                : copyright);
            if (copyrightBytes > ushort.MaxValue)
            {
                diagnostics.Report(new Diagnostic(Severity.Error,
                    Msg.Create(MsgKeys.MgCrit_Ugc_metadata_too_long, copyrightBytes, ushort.MaxValue))
                    .WithPathFallback(path));
                return OperationResult.Failure().WithDiagnostics(diagnostics);
            }
            _lines.Insert(1, "@COPYRIGHT\t" + copyright);
            await AtomicFile.WriteAsync(path, async (stream, token) =>
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                foreach (var line in _lines)
                {
                    await writer.WriteLineAsync(line.AsMemory(), token);
                }
                await writer.FlushAsync(token);
            }, ct);
            return OperationResult.Success().WithDiagnostics(diagnostics);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    private static int DifficultyValue(Difficulty difficulty) => difficulty switch
    {
        Difficulty.WorldsEnd => 4,
        Difficulty.Ultima => 5,
        _ => (int)difficulty
    };

    private void AppendEvents()
    {
        foreach (var e in _chart.Events.Children)
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
    }

    private void AppendMetadata(Meta m)
    {
        _lines.Add($"@EXVER\t{m.ExVersion}");
        _lines.Add($"@FLAG\tEXFIELD\t{(m.ExtendedField ? "TRUE" : FalseValue)}");
        _lines.Add($"@FLAG\tEXLONG\t{(m.ExLongEnabled ? "TRUE" : FalseValue)}");
        _lines.Add($"@FLAG\tBGMWCMP\t{(m.WaitBgmEnd ? "TRUE" : FalseValue)}");
        _lines.Add($"@LEVEL\t{(m.Difficulty == Difficulty.WorldsEnd
            ? PenguinTools.Chart.Writer.mgxc.MgxcChartWriter.WeLevel(m.WeDifficulty)
            : PenguinTools.Chart.Writer.mgxc.MgxcChartWriter.FormatPlayLevel(m.Level))}");
        _lines.AddRange(["@FLAG\tHIPRECISION\tTRUE", "@TICKS\t480", $"@TITLE\t{Clean(m.Title)}", $"@ARTIST\t{Clean(m.Artist)}",
            $"@DESIGN\t{Clean(m.Designer)}", $"@DIFF\t{DifficultyValue(m.Difficulty)}", $"@CONST\t{m.Level}", $"@SONGID\t{(string.IsNullOrWhiteSpace(m.MgxcId) ? (m.Id ?? 0).ToString(CultureInfo.InvariantCulture) : Clean(m.MgxcId))}",
            $"@MAINBPM\t{m.MainBpm}", $"@FLAG\tSOFFSET\t{(m.BgmEnableBarOffset ? 1 : 0)}",
            $"@FLAG\tCLICK\t{(_chart.Extras.ClickEnabled ? 1 : 0)}", $"@CLKCNT\t{_chart.Extras.Count(m.BgmInitialNumerator)}",
            $"@FLAG\tDIFFTTL\t{(_chart.Extras.Tutorial ? 1 : 0)}"]);
        _lines.AddRange([$"@SORT\t{Clean(m.SortName)}", $"@BGM\t{Clean(ChartMediaPaths.ForOutput(m.BgmFilePath, m.FilePath, path))}",
            $"@BGMOFS\t{m.BgmManualOffset}", $"@JACKET\t{Clean(ChartMediaPaths.ForOutput(m.JacketFilePath, m.FilePath, path))}",
            $"@MAINTIL\t{m.MainTil}", $"@CMT\t{Clean(C2sRoundTripComment.Strip(m.Comment))}"]);
        _lines.Add($"@LICENSE\t{Clean(m.License)}\t{Clean(m.LicenseUrl)}");
        _lines.Add($"@GENRE\t{Clean(m.Genre?.Str ?? "")}");
        if (m.Difficulty == Difficulty.WorldsEnd)
        {
            _lines.Add($"@WEATTR\t{Clean(m.WeTag.Str)}");
        }
        _lines.Add($"@BGMPRV\t{m.BgmPreviewStart}\t{m.BgmPreviewStop}");
        _lines.Add($"@BGIMG\t{Clean(ChartMediaPaths.ForOutput(m.BgiFilePath, m.FilePath, path))}");
        _lines.Add($"@BGSCENE\t{Clean(m.BackgroundScene)}");
        _lines.Add($"@BGMODE\t{Clean(_chart.Extras.EditorBackgroundMode)}\t{(m.BackgroundSync ? FalseValue : "TRUE")}");
        _lines.Add($"@FLDCOL\t{m.FieldColorIndex}");
        _lines.Add($"@FLDIMG\t{Clean(ChartMediaPaths.ForOutput(m.FieldImage, m.FilePath, path))}");
        _lines.Add($"@FLDSCENE\t{Clean(m.FieldScene)}");
        _lines.Add($"@ATINFO\tAUTHORS\t{m.Authors.Replace('\r', ' ').Replace('\n', ' ')}");
        _lines.Add($"@ATINFO\tSITES\t{m.AuthorSites.Replace('\r', ' ').Replace('\n', ' ')}");
        _lines.Add($"@DLURL\t{Clean(m.DownloadUrl)}");
    }

    private string AirCrashAxisSuffix(AirLadderAttr attribute) => target == EditorTarget.Legacy ? "" : attribute switch
    {
        AirLadderAttr.AxisY => "Y",
        AirLadderAttr.AxisZ => "Z",
        _ => "N"
    };

    // Legacy native readers look one character past the documented AIR-HOLD
    // color. Repeating I retains inversion with both that reader and modern ones.
    private string AirHoldColor(Color color) => target == EditorTarget.Legacy && color == Color.PNK
        ? "II" : AirColor(color).ToString();

    private string NotePayload(U.Note n, string xy)
    {
        return n switch
        {
            U.ExTap x => $"x{xy}{Effect(x.Effect)}",
            U.Tap => "t" + xy,
            U.Flick f => "f" + xy + f.AutoplayDirection,
            U.Damage => "d" + xy,
            U.Hold => "h" + xy,
            U.Slide slide => "s" + xy + (slide.NoLine ? "Z" : "0"),
            U.Air a => $"a{xy}{Direction(a.Direction)}{AirColor(a.Color)}",
            U.AirHold a => "H" + xy + AirHoldColor(a.Color),
            U.AirSlide a => "S" + xy + Height(a.Height) + AirColor(a.Color),
            U.AirCrash a => "C" + xy + Height(a.Height) + CrashColor(a.Color) + AirCrashAxisSuffix(a.Attr) + "," + (a.Density == int.MaxValue ? "$" : a.Density.Original.ToString(CultureInfo.InvariantCulture)),
            _ => throw new FormatException($"Unsupported UGC note: {n.GetType().Name}")
        };
    }

    private void WriteChildren(U.Note n)
    {
        foreach (var child in n.Children)
        {
            _lines.Add($"@USETIL\t{child.Timeline}");
            var childXy = $"{Coordinate(child.Lane)}{Digit(child.Width)}";
            var joint = child switch
            {
                U.SlideJoint s => s.Joint,
                U.AirSlideJoint a => a.Joint,
                U.AirHoldJoint a => a.Joint,
                _ => Joint.D
            };
            var marker = joint == Joint.C ? 'c' : 's';
            var body = child switch
            {
                U.HoldJoint => "s",
                U.AirHoldJoint => marker.ToString(),
                U.AirSlideJoint a => $"{marker}{childXy}{Height(a.Height)}",
                U.AirCrashJoint a => $"c{childXy}{Height(a.Height)}",
                _ => $"{marker}{childXy}"
            };
            var line = $"#{child.Tick.Original - n.Tick.Original}>{body}";
            _lines.Add(line);
            _snapshots.Append(line, child.Timeline);
            _written.Add(child);
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
        var xy = $"{Coordinate(n.Lane)}{Digit(n.Width)}";
        if (n is U.ExTapableNote ex && ex.Effect is { } effect && _effectCarriers.Add((n.Tick.Original, n.Lane, n.Width)))
        {
            Head(new U.ExTap
            {
                Tick = n.Tick,
                Lane = n.Lane,
                Width = n.Width,
                Timeline = n.Timeline,
                Effect = effect,
                Role = U.ExTapRole.SharedLongCarrier
            }, $"x{xy}{Effect(effect)}");
        }

        if (n is U.AirHold airHold)
        {
            _chart.Extras.AirAppearances.Add(new(ChartExtras.AirKey(airHold), airHold.Color, airHold.Direction));
        }
        if (n is U.AirSlide airSlide)
        {
            _chart.Extras.AirAppearances.Add(new(ChartExtras.AirKey(airSlide), airSlide.Color, airSlide.Direction));
        }
        var payload = NotePayload(n, xy);
        Head(n, payload);
        WriteChildren(n);
        WritePairedNotes(n);
    }

    private void WritePairedNotes(U.Note n)
    {
        foreach (var point in new[] { n }.Concat(n.Children))
        {
            if (point is U.PositiveNote positive && positive.PairNote is { } paired)
            {
                WriteNote(paired);
            }
        }
    }
}
