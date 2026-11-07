using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

using c2sModel = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart.Parser.c2s;

public sealed class C2SParser
{
    private static readonly HashSet<string> SupportedVersions =
    [
        "0.00.00",
        "1.01.00",
        "1.07.00",
        "1.08.00",
        "1.10.01",
        "1.11.00",
        "1.12.00",
        "1.13.00",
        "1.14.00",
        "1.15.00"
    ];

    private readonly List<PendingPair> _pendingPairs = [];
    private int _resolution = ChartResolution.ChunithmTick;
    private string? _version;
    private bool _sourceGameplayChanged;
    private readonly HashSet<int> _adjustedNumberLines = [];
    private readonly Dictionary<int, string> _adjustedSourceLines = [];
    private readonly Dictionary<int, string> _sourceLines = [];

    static C2SParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public C2SParser(C2SParseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);

        Path = request.Path;
    }

    private DiagnosticCollector Diagnostic { get; } = new();
    private string Path { get; }
    private c2sModel.Chart C2s { get; } = new();

    internal static Dictionary<c2sModel.Note, string> ParseNoteRecords(IEnumerable<string> lines, int resolution)
    {
        var parser = new C2SParser(new C2SParseRequest("snapshot")) { _resolution = resolution };
        var records = new Dictionary<c2sModel.Note, string>();
        var number = 0;
        foreach (var line in lines)
        {
            var before = parser.C2s.Notes.Count;
            parser.ParseTokens(Tokenize(line), line, ++number);
            foreach (var note in parser.C2s.Notes.Skip(before))
            {
                records[note] = C2SRoundTrip.Normalize(line, resolution);
            }
        }
        parser.ResolvePairings();
        return records;
    }

    public async Task<OperationResult<c2sModel.Chart>> ParseAsync(CancellationToken ct = default)
    {
        C2s.Meta.FilePath = Path;
        C2s.Meta.BgmEnableBarOffset = true;
        C2s.Meta.BgmFixedZeroOrigin = true;
        C2s.Extras.ClickEnabled = false;

        var lines = await ReadLinesAsync(Path, ct);
        foreach (var line in lines)
        {
            _sourceLines[line.Number] = line.Text;
        }
        C2s.Extras.InteropSourceText = string.Join("\n", lines.Select(l => l.Text));
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            ParseLine(line);
        }
        if (_adjustedSourceLines.Count > 0)
        {
            C2s.Extras.InteropSourceText = string.Join("\n", lines.Select(l => _adjustedSourceLines.GetValueOrDefault(l.Number, l.Text)));
        }

        if (string.IsNullOrWhiteSpace(_version))
        {
            Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.C2s_Version_line_not_found), Path));
        }

        C2s.Extras.InferClickCount(C2s);
        C2s.Extras.Tutorial = C2s.Extras.Headers.GetValueOrDefault("TUTORIAL", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() == "1";
        C2s.Extras.CaptureClock(C2s);
        C2s.Extras.SourceMetaKey = ChartExtras.MetaKey(C2s);
        ResolvePairings();
        try
        {
            NoteWidthRules.Validate(C2s);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.Report(ex);
        }
        var diagnostics = DiagnosticSnapshot.Create(Diagnostic);
        if (!diagnostics.HasError)
        {
            C2s.Extras.SourceKey = Writer.c2s.C2SChartWriter.GetContentKey(C2s);
            if (!_sourceGameplayChanged)
            {
                C2sJudgeSource.Capture(C2s.Extras, C2s.Extras.InteropSourceText!);
            }
        }

        return diagnostics.HasError
            ? OperationResult<c2sModel.Chart>.Failure().WithDiagnostics(diagnostics)
            : OperationResult<c2sModel.Chart>.Success(C2s).WithDiagnostics(diagnostics);
    }

    private void ParseLine(SourceLine line)
    {
        var text = line.Text.Trim();
        if (text.StartsWith("//" + ChartExtras.PendingSnapshotShiftPrefix, StringComparison.Ordinal))
        {
            ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Invalid_field, "pending snapshot translation"), line.Number);
            return;
        }
        if (text.StartsWith("//MGR_PREROLL=", StringComparison.Ordinal))
        {
            if (text != "//" + ChartExtras.PrerollBar1Marker)
            {
                ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Invalid_field, "preroll extension"), line.Number);
            }
            else
            {
                C2s.Extras.PrerollBar1Applied = true;
            }
            return;
        }
        if (text.StartsWith("//MGR_CLICK_V1", StringComparison.Ordinal))
        {
            ParseMetronomeExtension(text, line.Number);
            return;
        }
        if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))
        {
            return;
        }

        var tokens = Tokenize(text);
        if (tokens.Length == 0)
        {
            return;
        }

        if (tokens[0].StartsWith("T_", StringComparison.Ordinal))
        {
            C2s.Extras.SourceStatistics.Add(text);
        }

        if (tokens[0] is "VERSION" or "MUSIC" or "DIFFICULT" or "LEVEL" or "CREATOR" or "BPM_DEF" or "MET_DEF")
        {
            C2s.Extras.Headers[tokens[0]] = text;
        }

        ParseTokens(tokens, text, line.Number);
        if (_adjustedNumberLines.Contains(line.Number))
        {
            var adjusted = string.Join('\t', tokens);
            _adjustedSourceLines[line.Number] = adjusted;
            if (C2s.Extras.Headers.ContainsKey(tokens[0]))
            {
                C2s.Extras.Headers[tokens[0]] = adjusted;
            }
        }
    }

    private void ParseMetronomeExtension(string text, int lineNumber)
    {
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 3 || !int.TryParse(fields[1], out var count) || count < 0 || fields[2] is not ("0" or "1"))
        {
            ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Invalid_field, "metronome extension"), lineNumber);
            return;
        }

        C2s.Extras.ClickCount = count;
        C2s.Extras.ClickEnabled = fields[2] == "1";
    }

    private void ParseTokens(string[] tokens, string text, int lineNumber)
    {
        switch (tokens[0].ToUpperInvariant())
        {
            case "VERSION":
                ParseVersion(tokens, lineNumber);
                break;
            case "MUSIC":
                ParseMusic(tokens);
                break;
            case "DIFFICULT":
                ParseDifficulty(tokens);
                break;
            case "LEVEL":
                ParseLevel(tokens);
                break;
            case "CREATOR":
                ParseCreator(tokens);
                break;
            case "BPM_DEF":
                ParseBpmDefinition(tokens, lineNumber);
                break;
            case "MET_DEF":
                ParseMeterDefinition(tokens);
                break;
            case "RESOLUTION":
                ParseResolution(tokens, lineNumber);
                break;
            case "BPM":
                ParseBpm(tokens, lineNumber);
                break;
            case "MET":
                ParseMet(tokens, lineNumber);
                break;
            case "SLP":
                ParseSlp(tokens, lineNumber);
                break;
            case "SFL":
                ParseSfl(tokens, lineNumber);
                break;
            case "STP":
                ParseStop(tokens, lineNumber);
                break;
            case "DCM":
                ParseDcm(tokens, lineNumber);
                break;
            case "TAP":
            case "MNE":
            case "FLK":
            case "CHR":
            case "HLD":
            case "HXD":
            case "SLA":
            case "SLC":
            case "SLD":
            case "SXC":
            case "SXD":
            case "AIR":
            case "AUL":
            case "AUR":
            case "ADW":
            case "ADL":
            case "ADR":
            case "ASC":
            case "ASD":
            case "ALD":
                ParseNote(tokens, lineNumber);
                break;
            case "ASO":
            case "HHD":
            case "HHX":
                _sourceGameplayChanged = true;
                ReportAtLine(Severity.Warning,
                    Msg.Create(MsgKeys.C2s_Unsupported_note_type, ("noteType", tokens[0])), lineNumber);
                break;
            case "AHD":
            case "AHX":
            case "ASX":
                ParseAirHold(tokens, lineNumber);
                break;
            case "T_JUDGE_TAP":
            case "T_JUDGE_HLD":
            case "T_JUDGE_SLD":
            case "T_JUDGE_AIR":
            case "T_JUDGE_FLK":
            case "T_JUDGE_ALL":
                ParseJudgeSummary(tokens, lineNumber);
                break;
            case "SEQUENCEID":
            case "CLK_DEF":
            case "PROGJUDGE_BPM":
            case "PROGJUDGE_AER":
            case "TUTORIAL":
                C2s.Extras.Headers[tokens[0]] = text;
                if (tokens[0] == "CLK_DEF" && !C2s.Extras.ClickCount.HasValue && tokens.Length > 1)
                {
                    C2s.Extras.ClickEnabled = tokens[1] != "0";
                }

                break;
            case "CLK":
                if (TryGetInt(tokens, 1, lineNumber, "CLK measure", out var bar) &&
                    TryGetInt(tokens, 2, lineNumber, "CLK offset", out var offset))
                {
                    C2s.Extras.ClickTicks.Add(ScalePosition(bar, offset).Original);
                }

                break;
            default:
                if (tokens[0].Length == 3)
                {
                    _sourceGameplayChanged = true;
                    ReportAtLine(Severity.Information, Msg.Create(MsgKeys.Mg_Unrecognized_note, tokens[0]),
                        lineNumber);
                }

                if (tokens[0].Length > 3 && !tokens[0].StartsWith("T_", StringComparison.Ordinal))
                {
                    C2s.Extras.Headers[tokens[0]] = text;
                }
                // Unknown metadata is carried through without changing modeled fields.
                break;
        }
    }

    private void ParseJudgeSummary(string[] tokens, int lineNumber)
    {
        if (!TryGetInt(tokens, 1, lineNumber, tokens[0], out var value))
        {
            return;
        }

        if (value < 0)
        {
            ReportAtLine(
                Severity.Error,
                Msg.Create(MsgKeys.C2s_Invalid_field, tokens[0]),
                lineNumber,
                value);
            return;
        }

        switch (tokens[0].ToUpperInvariant())
        {
            case "T_JUDGE_TAP":
                C2s.Meta.C2sJudgeTap = value;
                break;
            case "T_JUDGE_HLD":
                C2s.Meta.C2sJudgeHld = value;
                break;
            case "T_JUDGE_SLD":
                C2s.Meta.C2sJudgeSld = value;
                break;
            case "T_JUDGE_AIR":
                C2s.Meta.C2sJudgeAir = value;
                break;
            case "T_JUDGE_FLK":
                C2s.Meta.C2sJudgeFlk = value;
                break;
            case "T_JUDGE_ALL":
                C2s.Meta.C2sJudgeAll = value;
                break;
        }
    }

    private void ParseVersion(string[] tokens, int lineNumber)
    {
        if (!TryGetToken(tokens, 1, lineNumber, "VERSION", out var version))
        {
            return;
        }

        _version = version;
        if (!SupportedVersions.Contains(version))
        {
            ReportAtLine(Severity.Warning, Msg.Create(MsgKeys.C2s_Unsupported_version, version), lineNumber);
        }
    }

    private void ParseMusic(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            return;
        }

        C2s.Meta.MgxcId = tokens[1];
        if (int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            C2s.Meta.Id = id;
        }
    }

    private void ParseDifficulty(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            return;
        }

        if (int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            C2s.Meta.Difficulty = UmiguriParserCommon.DifficultyFromValue(value);
        }
    }

    private void ParseLevel(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            return;
        }

        if (decimal.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var level))
        {
            C2s.Meta.Level = level;
        }
    }

    private void ParseCreator(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            return;
        }

        C2s.Meta.Designer = string.Join('\t', tokens.Skip(1));
    }

    private void ParseBpmDefinition(string[] tokens, int lineNumber)
    {
        if (tokens.Length < 2)
        {
            return;
        }

        if (!TryGetDecimal(tokens, 1, lineNumber, "BPM_DEF", out var bpm))
        {
            return;
        }

        C2s.Meta.MainBpm = tokens.Length > 2 && TryGetDecimal(tokens, 2, lineNumber, "BPM_DEF", out var mainBpm) ? mainBpm : bpm;
        C2s.Meta.BgmInitialBpm = bpm;
        for (var i = 3; i < Math.Min(tokens.Length, 5); i++)
        {
            TryGetDecimal(tokens, i, lineNumber, "BPM_DEF", out _);
        }
    }

    private void ParseMeterDefinition(string[] tokens)
    {
        if (tokens.Length < 3)
        {
            return;
        }

        if (int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator))
        {
            C2s.Meta.BgmInitialDenominator = denominator;
        }

        if (int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator))
        {
            C2s.Meta.BgmInitialNumerator = numerator;
        }
    }

    private void ParseResolution(string[] tokens, int lineNumber)
    {
        if (!TryGetInt(tokens, 1, lineNumber, "resolution", out var resolution))
        {
            return;
        }

        if (resolution <= 0)
        {
            ReportAtLine(Severity.Error, Msg.Key(MsgKeys.C2s_Resolution_must_be_positive), lineNumber);
            return;
        }

        _resolution = resolution;
        C2s.Extras.SourceResolution = resolution;
    }

    private void ParseBpm(string[] tokens, int lineNumber)
    {
        if (!TryParseNode(tokens, lineNumber, out var tick) ||
            !TryGetDecimal(tokens, 3, lineNumber, "BPM value", out var value))
        {
            return;
        }

        C2s.Events.Add(new c2sModel.Bpm
        {
            Tick = tick,
            Value = value
        });
    }

    private void ParseMet(string[] tokens, int lineNumber)
    {
        if (!TryParseNode(tokens, lineNumber, out var tick) ||
            !TryGetInt(tokens, 3, lineNumber, "MET denominator", out var denominator) ||
            !TryGetInt(tokens, 4, lineNumber, "MET numerator", out var numerator))
        {
            return;
        }

        if (numerator < 0 || denominator < 0)
        {
            ReportAtLine(Severity.Warning,
                Msg.Create(MsgKeys.C2s_Invalid_field, numerator < 0 ? "MET numerator" : "MET denominator"),
                lineNumber);
            return;
        }

        // Some existing charts use a terminal MET with denominator zero.
        // Preserve its record; it is not used to construct the editor's bar axis.
        if (denominator == 0)
        {
            ReportAtLine(Severity.Warning, Msg.Create(MsgKeys.C2s_Invalid_field, "MET denominator"), lineNumber);
        }

        C2s.Events.Add(new c2sModel.Met
        {
            Tick = tick,
            Denominator = denominator,
            Numerator = numerator
        });
    }

    private void ParseSlp(string[] tokens, int lineNumber)
    {
        if (!TryParseSpeedEvent(tokens, lineNumber, out var tick, out var length, out var speed))
        {
            return;
        }

        if (!TryGetInt(tokens, 5, lineNumber, "SLP timeline", out var timeline))
        {
            return;
        }

        C2s.Events.Add(new c2sModel.Slp
        {
            Tick = tick,
            Length = length,
            Speed = speed,
            Timeline = timeline
        });
    }

    private void ParseSfl(string[] tokens, int lineNumber)
    {
        if (!TryParseSpeedEvent(tokens, lineNumber, out var tick, out var length, out var speed))
        {
            return;
        }

#pragma warning disable CS0612
        C2s.Events.Add(new c2sModel.Sfl
        {
            Tick = tick,
            Length = length,
            Speed = speed
        });
#pragma warning restore CS0612
    }

    private void ParseStop(string[] tokens, int lineNumber)
    {
        if (!TryParseNode(tokens, lineNumber, out var tick) ||
            !TryGetInt(tokens, 3, lineNumber, "STP length", out var length))
        {
            return;
        }

#pragma warning disable CS0612
        C2s.Events.Add(new c2sModel.Stop
        {
            Tick = tick,
            Length = ScaleLength(length),
            Speed = 0m
        });
#pragma warning restore CS0612
    }

    private void ParseDcm(string[] tokens, int lineNumber)
    {
        if (!TryParseSpeedEvent(tokens, lineNumber, out var tick, out var length, out var speed))
        {
            return;
        }

        C2s.Events.Add(new c2sModel.Dcm
        {
            Tick = tick,
            Length = length,
            Speed = speed
        });
    }

    private void ParseNote(string[] tokens, int lineNumber)
    {
        switch (tokens[0].ToUpperInvariant())
        {
            case "TAP":
                ParseShortNote<c2sModel.Tap>(tokens, lineNumber);
                break;
            case "MNE":
                ParseShortNote<c2sModel.Damage>(tokens, lineNumber);
                break;
            case "FLK":
                ParseShortNote<c2sModel.Flick>(tokens, lineNumber);
                break;
            case "CHR":
                ParseExTap(tokens, lineNumber);
                break;
            case "HLD":
            case "HXD":
                ParseHold(tokens, lineNumber);
                break;
            case "SLA":
                ParseSla(tokens, lineNumber);
                break;
            case "SLC":
            case "SLD":
            case "SXC":
            case "SXD":
                ParseSlide(tokens, lineNumber);
                break;
            case "AIR":
            case "AUL":
            case "AUR":
            case "ADW":
            case "ADL":
            case "ADR":
                ParseAir(tokens, lineNumber);
                break;
            case "ASC":
            case "ASD":
                ParseAirSlide(tokens, lineNumber);
                break;
            case "ALD":
                ParseAirCrash(tokens, lineNumber);
                break;
        }
    }

    private void ParseShortNote<T>(string[] tokens, int lineNumber) where T : c2sModel.Note, new()
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase))
        {
            return;
        }

        C2s.Notes.Add(new T
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width
        });
    }

    private void ParseExTap(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase))
        {
            return;
        }

        var note = new c2sModel.ExTap
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width
        };

        if (TryReadEffect(tokens, 5, lineNumber, out var effect))
        {
            note.Effect = effect;
        }
        else if (UsesLegacyDefaultExTapEffect())
        {
            note.Effect = ExEffect.UP;
        }

        C2s.Notes.Add(note);
    }

    private void ParseHold(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetInt(tokens, 5, lineNumber, "HOLD length", out var length))
        {
            return;
        }

        var type = tokens[0].ToUpperInvariant();

        var note = new c2sModel.Hold
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = noteBase.Lane,
            EndWidth = noteBase.Width
        };

        if (type == "HXD")
        {
            note.Effect = TryReadEffect(tokens, 6, lineNumber, out var effect) ? effect : ExEffect.UP;
        }

        C2s.Notes.Add(note);
    }

    private void ParseSla(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetInt(tokens, 5, lineNumber, "SLA length", out var length) ||
            !TryGetInt(tokens, 6, lineNumber, "SLA timeline", out var timeline))
        {
            return;
        }

        C2s.Notes.Add(new c2sModel.Sla
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            Length = ScaleLength(length),
            Timeline = timeline
        });
    }

    private void ParseSlide(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase, allowZeroWidth: true) ||
            !TryGetInt(tokens, 5, lineNumber, "SLIDE length", out var length) ||
            !TryGetInt(tokens, 6, lineNumber, "SLIDE end lane", out var endLane))
        {
            return;
        }

        int endWidth;
        if (HasToken(tokens, 7))
        {
            if (!TryGetInt(tokens, 7, lineNumber, "SLIDE end width", out endWidth))
            {
                return;
            }
        }
        else
        {
            endWidth = noteBase.Width;
        }

        var type = tokens[0].ToUpperInvariant();
        var note = new c2sModel.Slide
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = endLane,
            EndWidth = Math.Min(endWidth, 16),
            Joint = type.EndsWith('C') ? Joint.C : Joint.D
        };

        note.NoLine = HasToken(tokens, 8) && tokens[8].Equals("NCL", StringComparison.OrdinalIgnoreCase);
        // Older exporters can omit the link field. Only a recognized effect
        // identifies that abbreviated form; other link markers use the normal line.
        var effectIndex = !HasToken(tokens, 9) && HasToken(tokens, 8) &&
                          Enum.TryParse<ExEffect>(tokens[8], true, out var legacyEffect) && Enum.IsDefined(legacyEffect)
            ? 8
            : 9;

        if (type is "SXD" or "SXC")
        {
            note.Effect = TryReadEffect(tokens, effectIndex, lineNumber, out var effect) ? effect : ExEffect.UP;
        }

        C2s.Notes.Add(note);
    }

    private void ParseAir(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase))
        {
            return;
        }

        if (!TryGetToken(tokens, 5, lineNumber, "AIR parent", out var parentId))
        {
            return;
        }

        var note = new c2sModel.Air
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            Direction = AirDirectionFromId(tokens[0])
        };

        if (TryReadColor(tokens, 6, lineNumber, out var color))
        {
            note.Color = color;
        }

        C2s.Notes.Add(note);
        _pendingPairs.Add(new PendingPair(note, parentId, lineNumber));
    }

    private void ParseAirSlide(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase, allowZeroWidth: true) ||
            !TryGetToken(tokens, 5, lineNumber, "AIR-SLIDE parent", out var parentId) ||
            !TryGetDecimal(tokens, 6, lineNumber, "AIR-SLIDE height", out var height) ||
            !TryGetInt(tokens, 7, lineNumber, "AIR-SLIDE length", out var length) ||
            !TryGetInt(tokens, 8, lineNumber, "AIR-SLIDE end lane", out var endLane) ||
            !TryGetInt(tokens, 9, lineNumber, "AIR-SLIDE end width", out var endWidth) ||
            !TryGetDecimal(tokens, 10, lineNumber, "AIR-SLIDE end height", out var endHeight))
        {
            return;
        }

        var note = new c2sModel.AirSlide
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            Joint = tokens[0].EndsWith('C') ? Joint.C : Joint.D,
            Height = ParseDisplayedHeight(height),
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = endLane,
            EndWidth = Math.Min(endWidth, 16),
            EndHeight = ParseDisplayedHeight(endHeight)
        };

        if (TryReadColor(tokens, 11, lineNumber, out var color))
        {
            note.Color = color;
        }

        C2s.Notes.Add(note);
        _pendingPairs.Add(new PendingPair(note, parentId, lineNumber));
    }

    private void ParseAirHold(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetToken(tokens, 5, lineNumber, "AIR-HOLD parent", out var parentId) ||
            !TryGetInt(tokens, 6, lineNumber, "AIR-HOLD length", out var length))
        {
            return;
        }

        var note = new c2sModel.AirHold
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            Joint = tokens[0].EndsWith("X", StringComparison.OrdinalIgnoreCase) ? Joint.C : Joint.D,
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = noteBase.Lane,
            EndWidth = noteBase.Width
        };

        if (TryReadColor(tokens, 7, lineNumber, out var color))
        {
            note.Color = color;
        }

        C2s.Notes.Add(note);
        _pendingPairs.Add(new PendingPair(note, parentId, lineNumber));
    }

    private void ParseAirCrash(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetInt(tokens, 5, lineNumber, "AIR-CRASH density", out var density) ||
            !TryGetDecimal(tokens, 6, lineNumber, "AIR-CRASH height", out var height) ||
            !TryGetInt(tokens, 7, lineNumber, "AIR-CRASH length", out var length) ||
            !TryGetInt(tokens, 8, lineNumber, "AIR-CRASH end lane", out var endLane) ||
            !TryGetInt(tokens, 9, lineNumber, "AIR-CRASH end width", out var endWidth) ||
            !TryGetDecimal(tokens, 10, lineNumber, "AIR-CRASH end height", out var endHeight))
        {
            return;
        }

        var note = new c2sModel.AirCrash
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            Density = ScaleLength(density),
            Height = ParseDisplayedHeight(height),
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = endLane,
            EndWidth = Math.Min(endWidth, 16),
            EndHeight = ParseDisplayedHeight(endHeight)
        };

        if (TryReadColor(tokens, 11, lineNumber, out var color))
        {
            note.Color = color;
        }

        if (TryReadAirLadderAttr(tokens, 12, lineNumber, out var attr))
        {
            note.Attr = attr;
        }

        C2s.Notes.Add(note);
    }

    private bool TryParseSpeedEvent(string[] tokens, int lineNumber, out Time tick, out Time length, out decimal speed)
    {
        tick = default;
        length = default;
        speed = 1m;

        if (!TryParseNode(tokens, lineNumber, out tick) ||
            !TryGetInt(tokens, 3, lineNumber, "event length", out var rawLength) ||
            !TryGetDecimal(tokens, 4, lineNumber, "event speed", out speed))
        {
            return false;
        }

        length = ScaleLength(rawLength);
        return true;
    }

    private bool TryParseNode(string[] tokens, int lineNumber, out Time tick)
    {
        tick = default;
        if (!TryGetInt(tokens, 1, lineNumber, "measure", out var measure) ||
            !TryGetInt(tokens, 2, lineNumber, "offset", out var offset))
        {
            return false;
        }

        tick = ScalePosition(measure, offset);
        return true;
    }

    private bool TryParseNoteBase(string[] tokens, int lineNumber, out NoteBase noteBase, bool allowZeroWidth = false)
    {
        noteBase = default;
        if (!TryParseNode(tokens, lineNumber, out var tick) ||
            !TryGetInt(tokens, 3, lineNumber, "lane", out var lane) ||
            !TryGetInt(tokens, 4, lineNumber, "width", out var width))
        {
            return false;
        }

        if (width <= 0 && !(allowZeroWidth && width == 0))
        {
            ReportAtLine(Severity.Error, Msg.Create(MsgKeys.MgCrit_Invalid_note_width, width), lineNumber);
            return false;
        }
        // Zero is provisional until resolved paths prove it continues a native
        // nonjudging clipped control rather than starting a playable note.
        noteBase = new NoteBase(tick, lane, Math.Clamp(width, allowZeroWidth ? 0 : 1, 16));
        return true;
    }

    private void ResolvePairings()
    {
        var roots = new Dictionary<c2sModel.Note, c2sModel.Note>();
        foreach (var path in C2sSlidePaths.Build(C2s.Notes.OfType<c2sModel.Slide>()))
        {
            foreach (var segment in path)
            {
                roots[segment] = path[0];
            }
        }
        foreach (var path in C2sAirCrashPaths.Build(C2s.Notes.OfType<c2sModel.AirCrash>()))
        {
            foreach (var segment in path)
            {
                roots[segment] = path[0];
            }
        }

        var pendingByNote = _pendingPairs.ToDictionary(p => p.Note);
        var parents = new List<AirParentState>();
        var stateByRoot = new Dictionary<c2sModel.Note, AirParentState>();
        foreach (var note in C2s.Notes)
        {
            if (!pendingByNote.TryGetValue(note, out var pending))
            {
                var root = roots.GetValueOrDefault(note, note);
                if (!stateByRoot.TryGetValue(root, out var state))
                {
                    state = new AirParentState(root);
                    stateByRoot.Add(root, state);
                    parents.Add(state);
                }
                state.Tail = note;
                continue;
            }

            // Each ground record has one AIR slot. Replay statements so a
            // later parent or continuation cannot affect an earlier binding.
            var binding = FindPairParent(note, pending.ParentId, parents);
            if (binding is { } found)
            {
                ((c2sModel.IPairable)note).Parent = found.Parent;
                found.State.AirTail = note;
                continue;
            }
            ReportAtLine(Severity.Warning,
                Msg.Create(MsgKeys.C2s_Parent_not_resolved, pending.ParentId, pending.Note.Id), pending.LineNumber,
                pending.Note);
        }
    }

    private static AirParentBinding? FindPairParent(c2sModel.Note note, string parentId,
        List<AirParentState> parents)
    {
        parentId = parentId.ToUpperInvariant();
        foreach (var state in parents)
        {
            if (note is c2sModel.AirHold && parentId is "AHD" or "AHX" or "ASX")
            {
                if (state.AirTail is c2sModel.AirHold airHold &&
                    airHold.EndTick == note.Tick && EndLane(state.Tail) == note.Lane &&
                    EndWidth(state.Tail) == note.Width)
                {
                    return new AirParentBinding(airHold, state);
                }
            }
            else if (note is c2sModel.AirSlide && parentId is "ASD" or "ASC")
            {
                if (state.AirTail is c2sModel.AirSlide airSlide && airSlide.Id == parentId &&
                    IsAttachPoint(airSlide, note) && C2sHeight.Key(airSlide.EndHeight) == C2sHeight.Key(((c2sModel.AirSlide)note).Height))
                {
                    return new AirParentBinding(airSlide, state);
                }
            }
            else if (state.AirTail is null && ParentTypeMatches(state.Root, parentId) && IsAttachPoint(state.Tail, note))
            {
                return new AirParentBinding(state.Tail, state);
            }
        }
        return null;
    }

    private static bool ParentTypeMatches(c2sModel.Note candidate, string parentId) => candidate switch
    {
        c2sModel.Hold => parentId is "HLD" or "HXD",
        c2sModel.Slide => parentId is "SLD" or "SXD" or "SLC" or "SXC",
        _ => candidate.Id == parentId
    };

    private static bool IsAttachPoint(c2sModel.Note candidate, c2sModel.Note note) =>
        EndTick(candidate) == note.Tick.Original && EndLane(candidate) == note.Lane && EndWidth(candidate) == note.Width;

    private static int EndTick(c2sModel.Note note) => note switch
    {
        c2sModel.LongNote longNote => longNote.EndTick.Original,
        c2sModel.Sla sla => checked(sla.Tick.Original + sla.Length.Original),
        _ => note.Tick.Original
    };

    private static int EndLane(c2sModel.Note note) => note is c2sModel.LongNote longNote ? longNote.EndLane : note.Lane;

    private static int EndWidth(c2sModel.Note note) => note is c2sModel.LongNote longNote ? longNote.EndWidth : note.Width;

    private sealed class AirParentState(c2sModel.Note root)
    {
        public c2sModel.Note Root { get; } = root;
        public c2sModel.Note Tail { get; set; } = root;
        public c2sModel.Note? AirTail { get; set; }
    }

    private readonly record struct AirParentBinding(c2sModel.Note Parent, AirParentState State);

    private bool TryReadEffect(string[] tokens, int index, int lineNumber, out ExEffect effect) =>
        TryReadEnum(tokens, index, lineNumber, MsgKeys.C2s_Unknown_ex_effect, default, out effect);

    private bool TryReadColor(string[] tokens, int index, int lineNumber, out Color color) =>
        TryReadEnum(tokens, index, lineNumber, MsgKeys.C2s_Unknown_air_color, Color.DEF, out color);

    private bool TryReadAirLadderAttr(string[] tokens, int index, int lineNumber, out AirLadderAttr attr) =>
        TryReadEnum(tokens, index, lineNumber, MsgKeys.C2s_Unknown_air_ladder_attr, AirLadderAttr.DEF, out attr);

    private bool TryReadEnum<TEnum>(string[] tokens, int index, int lineNumber, string unknownKey,
        TEnum defaultValue, out TEnum value) where TEnum : struct, Enum
    {
        value = defaultValue;
        if (!HasToken(tokens, index))
        {
            return false;
        }

        if (Enum.TryParse(tokens[index], true, out value) && Enum.IsDefined(value))
        {
            return true;
        }

        ReportAtLine(Severity.Warning, Msg.Create(unknownKey, tokens[index]), lineNumber);
        return false;
    }

    private static AirDirection AirDirectionFromId(string id)
    {
        return id.ToUpperInvariant() switch
        {
            "AUL" => AirDirection.UL,
            "AUR" => AirDirection.UR,
            "ADW" => AirDirection.DW,
            "ADL" => AirDirection.DL,
            "ADR" => AirDirection.DR,
            _ => AirDirection.IR
        };
    }

    private Time ScalePosition(int measure, int offset)
    {
        return ScaleLength(checked(measure * _resolution + offset));
    }

    private Time ScaleLength(int ticks)
    {
        return (int)Math.Round(
            ticks * (decimal)ChartResolution.UmiguriTick / _resolution,
            MidpointRounding.AwayFromZero);
    }

    private Time AddLength(Time tick, int length)
    {
        return tick.Original + ScaleLength(length).Original;
    }

    private static Height ParseDisplayedHeight(decimal value)
    {
        return (value - 1m) / 0.5m * 10m;
    }

    private bool UsesLegacyDefaultExTapEffect()
    {
        return _version is "0.00.00" or "1.01.00" or "1.07.00";
    }

    private bool TryGetToken(string[] tokens, int index, int lineNumber, string field, out string value)
    {
        value = string.Empty;
        if (index < tokens.Length && !string.IsNullOrWhiteSpace(tokens[index]))
        {
            value = tokens[index];
            return true;
        }

        ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Missing_field, field), lineNumber);
        return false;
    }

    private static bool HasToken(string[] tokens, int index)
    {
        return index < tokens.Length && !string.IsNullOrWhiteSpace(tokens[index]);
    }

    private bool TryGetInt(string[] tokens, int index, int lineNumber, string field, out int value)
    {
        value = default;
        if (index < tokens.Length &&
            int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Invalid_field, field), lineNumber,
            index < tokens.Length ? tokens[index] : null);
        return false;
    }

    private bool TryGetDecimal(string[] tokens, int index, int lineNumber, string field, out decimal value)
    {
        value = default;
        if (index < tokens.Length &&
            decimal.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            (parsed != 0 || !NativeNumber.HasNonzeroCoefficient(tokens[index])) &&
            float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var native) &&
            float.IsFinite(native) && TryKeepSingleValue(parsed, native, out value))
        {
            return true;
        }

        if (index < tokens.Length &&
            double.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var approximation))
        {
            var original = tokens[index];
            var kind = field.StartsWith("BPM", StringComparison.Ordinal) ? NativeNumberKind.Bpm :
                field == "event speed" ? NativeNumberKind.Speed : NativeNumberKind.Value;
            value = NativeNumber.Read(approximation, kind, out _, original: original);
            tokens[index] = value.ToString("G29", CultureInfo.InvariantCulture);
            _adjustedNumberLines.Add(lineNumber);
            _sourceGameplayChanged = true;
            ReportAtLine(Severity.Warning, Msg.Create(MsgKeys.Warn_Number_adjusted, $"C2S {field}", original, tokens[index]), lineNumber);
            return true;
        }

        ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Invalid_field, field), lineNumber,
            index < tokens.Length ? tokens[index] : null);
        return false;
    }

    [SuppressMessage("Reliability", "S1244",
        Justification = "The result must round to the exact native float32 value; tolerance comparisons can change timing and judgement thresholds.")]
    private static bool TryKeepSingleValue(decimal parsed, float native, out decimal value)
    {
        value = parsed;
        var projected = float.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (projected == native)
        {
            return true;
        }

        var scale = (byte)((decimal.GetBits(value)[3] >> 16) & 0xff);
        var unit = new decimal(1, 0, 0, false, scale);
        try
        {
            value += projected < native ? unit : -unit;
            return float.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) == native;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private void ReportAtLine(Severity severity, MessageDescriptor message, int lineNumber, object? target = null)
    {
        Diagnostic.Report(new LocationDiagnostic(severity, message, lineNumber, Path)
        {
            Target = target ?? (_sourceLines.TryGetValue(lineNumber, out var text) ? new SourceLineDiagnosticTarget(text) : null)
        });
    }

    private static string[] Tokenize(string text)
    {
        return text.Contains('\t')
            ? text.Split('\t', StringSplitOptions.TrimEntries)
            : text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<SourceLine[]> ReadLinesAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.GetEncoding(932).GetString(bytes);
        }

        using var reader = new StringReader(text);
        List<SourceLine> lines = [];
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lines.Add(new SourceLine(lines.Count + 1, line));
        }

        return [.. lines];
    }

    private readonly record struct SourceLine(int Number, string Text);

    private readonly record struct NoteBase(Time Tick, int Lane, int Width);

    private sealed record PendingPair(c2sModel.Note Note, string ParentId, int LineNumber);
}
