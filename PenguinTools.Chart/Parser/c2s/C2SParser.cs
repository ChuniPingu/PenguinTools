using System.Globalization;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser.c2s;

using c2sModel = Models.c2s;

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
        "1.13.00"
    ];

    private readonly List<PendingPair> _pendingPairs = [];
    private readonly HashSet<c2sModel.Note> _usedPairParents = [];
    private int _resolution = ChartResolution.ChunithmTick;
    private string? _version;

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

    public async Task<OperationResult<c2sModel.Chart>> ParseAsync(CancellationToken ct = default)
    {
        C2s.Meta.FilePath = Path;

        var lines = await ReadLinesAsync(Path, ct);
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            ParseLine(line);
        }

        if (string.IsNullOrWhiteSpace(_version))
            Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.C2s_Version_line_not_found), Path));

        ResolvePairings();

        var diagnostics = DiagnosticSnapshot.Create(Diagnostic);
        return diagnostics.HasError
            ? OperationResult<c2sModel.Chart>.Failure().WithDiagnostics(diagnostics)
            : OperationResult<c2sModel.Chart>.Success(C2s).WithDiagnostics(diagnostics);
    }

    private void ParseLine(SourceLine line)
    {
        var text = line.Text.Trim();
        if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal)) return;

        var tokens = Tokenize(text);
        if (tokens.Length == 0) return;

        switch (tokens[0].ToUpperInvariant())
        {
            case "VERSION":
                ParseVersion(tokens, line.Number);
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
                ParseBpmDefinition(tokens);
                break;
            case "MET_DEF":
                ParseMeterDefinition(tokens);
                break;
            case "RESOLUTION":
                ParseResolution(tokens, line.Number);
                break;
            case "BPM":
                ParseBpm(tokens, line.Number);
                break;
            case "MET":
                ParseMet(tokens, line.Number);
                break;
            case "SLP":
                ParseSlp(tokens, line.Number);
                break;
            case "SFL":
                ParseSfl(tokens, line.Number);
                break;
            case "STP":
                ParseStop(tokens, line.Number);
                break;
            case "DCM":
                ParseDcm(tokens, line.Number);
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
                ParseNote(tokens, line.Number);
                break;
            case "AHD":
            case "AHX":
                ParseAirHold(tokens, line.Number);
                break;
            case "ASX":
                ReportAtLine(Severity.Information,
                    Msg.Create(MsgKeys.C2s_Note_type_not_represented, tokens[0]), line.Number);
                break;
            case "SEQUENCEID":
            case "CLK_DEF":
            case "PROGJUDGE_BPM":
            case "PROGJUDGE_AER":
            case "TUTORIAL":
            case "CLK":
                break;
            default:
                if (tokens[0].Length == 3)
                    ReportAtLine(Severity.Information, Msg.Create(MsgKeys.Mg_Unrecognized_note, tokens[0]),
                        line.Number);
                // Skip unknown meta properties (e.g. T_* chart statistics).
                break;
        }
    }

    private void ParseVersion(string[] tokens, int lineNumber)
    {
        if (!TryGetToken(tokens, 1, lineNumber, "VERSION", out var version)) return;

        _version = version;
        if (!SupportedVersions.Contains(version))
            ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Unsupported_version, version), lineNumber);
    }

    private void ParseMusic(string[] tokens)
    {
        if (tokens.Length < 2) return;

        C2s.Meta.MgxcId = tokens[1];
        if (int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) C2s.Meta.Id = id;
    }

    private void ParseDifficulty(string[] tokens)
    {
        if (tokens.Length < 2) return;
        if (int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            C2s.Meta.Difficulty = UmiguriParserCommon.DifficultyFromValue(value);
    }

    private void ParseLevel(string[] tokens)
    {
        if (tokens.Length < 2) return;
        if (decimal.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var level))
            C2s.Meta.Level = level;
    }

    private void ParseCreator(string[] tokens)
    {
        if (tokens.Length < 2) return;
        C2s.Meta.Designer = string.Join('\t', tokens.Skip(1));
    }

    private void ParseBpmDefinition(string[] tokens)
    {
        if (tokens.Length < 2) return;
        if (!decimal.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm)) return;

        C2s.Meta.MainBpm = bpm;
        C2s.Meta.BgmInitialBpm = bpm;
    }

    private void ParseMeterDefinition(string[] tokens)
    {
        if (tokens.Length < 3) return;
        if (int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator))
            C2s.Meta.BgmInitialDenominator = denominator;
        if (int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator))
            C2s.Meta.BgmInitialNumerator = numerator;
    }

    private void ParseResolution(string[] tokens, int lineNumber)
    {
        if (!TryGetInt(tokens, 1, lineNumber, "resolution", out var resolution)) return;
        if (resolution <= 0)
        {
            ReportAtLine(Severity.Error, Msg.Key(MsgKeys.C2s_Resolution_must_be_positive), lineNumber);
            return;
        }

        _resolution = resolution;
    }

    private void ParseBpm(string[] tokens, int lineNumber)
    {
        if (!TryParseNode(tokens, lineNumber, out var tick) ||
            !TryGetDecimal(tokens, 3, lineNumber, "BPM value", out var value))
            return;

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
            return;

        if (numerator <= 0 || denominator <= 0)
        {
            ReportAtLine(Severity.Warning,
                Msg.Create(MsgKeys.C2s_Invalid_field, numerator <= 0 ? "MET numerator" : "MET denominator"),
                lineNumber);
            return;
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
        if (!TryParseSpeedEvent(tokens, lineNumber, out var tick, out var length, out var speed)) return;
        if (!TryGetInt(tokens, 5, lineNumber, "SLP timeline", out var timeline)) return;

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
        if (!TryParseSpeedEvent(tokens, lineNumber, out var tick, out var length, out var speed)) return;

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
            return;

#pragma warning disable CS0612
        C2s.Events.Add(new c2sModel.Sfl
        {
            Tick = tick,
            Length = ScaleLength(length),
            Speed = 0m
        });
#pragma warning restore CS0612
    }

    private void ParseDcm(string[] tokens, int lineNumber)
    {
        if (!TryParseSpeedEvent(tokens, lineNumber, out var tick, out var length, out var speed)) return;

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
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase)) return;
        C2s.Notes.Add(new T
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width
        });
    }

    private void ParseExTap(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase)) return;

        var note = new c2sModel.ExTap
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width
        };

        if (TryReadEffect(tokens, 5, lineNumber, out var effect))
            note.Effect = effect;
        else if (UsesLegacyDefaultExTapEffect())
            note.Effect = ExEffect.UP;

        C2s.Notes.Add(note);
    }

    private void ParseHold(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetInt(tokens, 5, lineNumber, "HOLD length", out var length))
            return;

        var note = new c2sModel.Hold
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = noteBase.Lane,
            EndWidth = noteBase.Width
        };

        if (TryReadEffect(tokens, 6, lineNumber, out var effect)) note.Effect = effect;
        C2s.Notes.Add(note);
    }

    private void ParseSla(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetInt(tokens, 5, lineNumber, "SLA length", out var length) ||
            !TryGetInt(tokens, 6, lineNumber, "SLA timeline", out var timeline))
            return;

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
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetInt(tokens, 5, lineNumber, "SLIDE length", out var length) ||
            !TryGetInt(tokens, 6, lineNumber, "SLIDE end lane", out var endLane) ||
            !TryGetInt(tokens, 7, lineNumber, "SLIDE end width", out var endWidth))
            return;

        var type = tokens[0].ToUpperInvariant();
        var note = new c2sModel.Slide
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = endLane,
            EndWidth = endWidth,
            Joint = type.EndsWith('C') ? Joint.C : Joint.D
        };

        // Column 8 is the legacy literal slide linkage marker (SLD), not an effect.
        if (TryReadEffect(tokens, 9, lineNumber, out var effect))
            note.Effect = effect;

        C2s.Notes.Add(note);
    }

    private void ParseAir(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase)) return;
        if (!TryGetToken(tokens, 5, lineNumber, "AIR parent", out var parentId)) return;

        var note = new c2sModel.Air
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            Direction = AirDirectionFromId(tokens[0])
        };

        if (TryReadColor(tokens, 6, lineNumber, out var color)) note.Color = color;

        C2s.Notes.Add(note);
        _pendingPairs.Add(new PendingPair(note, parentId, lineNumber));
    }

    private void ParseAirSlide(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetToken(tokens, 5, lineNumber, "AIR-SLIDE parent", out var parentId) ||
            !TryGetDecimal(tokens, 6, lineNumber, "AIR-SLIDE height", out var height) ||
            !TryGetInt(tokens, 7, lineNumber, "AIR-SLIDE length", out var length) ||
            !TryGetInt(tokens, 8, lineNumber, "AIR-SLIDE end lane", out var endLane) ||
            !TryGetInt(tokens, 9, lineNumber, "AIR-SLIDE end width", out var endWidth) ||
            !TryGetDecimal(tokens, 10, lineNumber, "AIR-SLIDE end height", out var endHeight))
            return;

        var note = new c2sModel.AirSlide
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            Joint = tokens[0].EndsWith('C') ? Joint.C : Joint.D,
            Height = ParseDisplayedHeight(height),
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = endLane,
            EndWidth = endWidth,
            EndHeight = ParseDisplayedHeight(endHeight)
        };

        if (TryReadColor(tokens, 11, lineNumber, out var color)) note.Color = color;

        C2s.Notes.Add(note);
        _pendingPairs.Add(new PendingPair(note, parentId, lineNumber));
    }

    private void ParseAirHold(string[] tokens, int lineNumber)
    {
        if (!TryParseNoteBase(tokens, lineNumber, out var noteBase) ||
            !TryGetToken(tokens, 5, lineNumber, "AIR-HOLD parent", out var parentId) ||
            !TryGetInt(tokens, 6, lineNumber, "AIR-HOLD length", out var length))
            return;

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

        if (TryReadColor(tokens, 7, lineNumber, out var color)) note.Color = color;

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
            return;

        var note = new c2sModel.AirCrash
        {
            Tick = noteBase.Tick,
            Lane = noteBase.Lane,
            Width = noteBase.Width,
            Density = ScaleLength(density),
            Height = ParseDisplayedHeight(height),
            EndTick = AddLength(noteBase.Tick, length),
            EndLane = endLane,
            EndWidth = endWidth,
            EndHeight = ParseDisplayedHeight(endHeight)
        };

        if (TryReadColor(tokens, 11, lineNumber, out var color)) note.Color = color;
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
            return false;

        length = ScaleLength(rawLength);
        return true;
    }

    private bool TryParseNode(string[] tokens, int lineNumber, out Time tick)
    {
        tick = default;
        if (!TryGetInt(tokens, 1, lineNumber, "measure", out var measure) ||
            !TryGetInt(tokens, 2, lineNumber, "offset", out var offset))
            return false;

        tick = ScalePosition(measure, offset);
        return true;
    }

    private bool TryParseNoteBase(string[] tokens, int lineNumber, out NoteBase noteBase)
    {
        noteBase = default;
        if (!TryParseNode(tokens, lineNumber, out var tick) ||
            !TryGetInt(tokens, 3, lineNumber, "lane", out var lane) ||
            !TryGetInt(tokens, 4, lineNumber, "width", out var width))
            return false;

        noteBase = new NoteBase(tick, lane, width);
        return true;
    }

    private void ResolvePairings()
    {
        foreach (var pending in _pendingPairs)
        {
            var parent = FindPairParent(pending.Note, pending.ParentId);
            if (parent != null)
            {
                ((c2sModel.IPairable)pending.Note).Parent = parent;
                _usedPairParents.Add(parent);
                continue;
            }

            ReportAtLine(Severity.Warning,
                Msg.Create(MsgKeys.C2s_Parent_not_resolved, pending.ParentId, pending.Note.Id), pending.LineNumber,
                pending.Note);
        }
    }

    private c2sModel.Note? FindPairParent(c2sModel.Note note, string parentId)
    {
        return C2s.Notes
            .Where(candidate => ParentTypeMatches(candidate, parentId))
            .Where(candidate => IsAttachPoint(candidate, note))
            .OrderBy(candidate => _usedPairParents.Contains(candidate))
            .ThenBy(candidate => PairDistance(candidate, note))
            .FirstOrDefault();
    }

    private static bool ParentTypeMatches(c2sModel.Note candidate, string parentId)
    {
        // C2S writers use SLD as the generic parent marker even when the
        // attach-point segment itself is a control (SLC/SXC) segment.
        if (parentId.Equals("SLD", StringComparison.OrdinalIgnoreCase) && candidate is c2sModel.Slide)
            return true;
        if (parentId.Equals("HLD", StringComparison.OrdinalIgnoreCase) && candidate is c2sModel.Hold)
            return true;
        if (parentId.Equals("AHD", StringComparison.OrdinalIgnoreCase) && candidate is c2sModel.AirHold)
            return true;
        return string.Equals(candidate.Id, parentId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAttachPoint(c2sModel.Note candidate, c2sModel.Note note)
    {
        if (candidate is c2sModel.LongHeightNote heightNote &&
            heightNote.EndTick.Original == note.Tick.Original &&
            heightNote.EndLane == note.Lane &&
            heightNote.EndWidth == note.Width &&
            note is c2sModel.LongHeightNote targetHeight &&
            heightNote.EndHeight.Result == targetHeight.Height.Result)
            return true;

        if (candidate is c2sModel.LongNote longNote &&
            longNote.EndTick.Original == note.Tick.Original &&
            longNote.EndLane == note.Lane &&
            longNote.EndWidth == note.Width)
            return true;

        return candidate.Tick.Original == note.Tick.Original &&
               candidate.Lane == note.Lane &&
               candidate.Width == note.Width;
    }

    private static int PairDistance(c2sModel.Note candidate, c2sModel.Note note)
    {
        if (candidate is c2sModel.LongNote longNote)
            return Math.Abs(longNote.EndTick.Original - note.Tick.Original);

        return Math.Abs(candidate.Tick.Original - note.Tick.Original);
    }

    private bool TryReadEffect(string[] tokens, int index, int lineNumber, out ExEffect effect)
    {
        effect = default;
        if (index >= tokens.Length || string.IsNullOrWhiteSpace(tokens[index])) return false;
        if (Enum.TryParse(tokens[index], true, out effect) && Enum.IsDefined(effect)) return true;

        ReportAtLine(Severity.Warning, Msg.Create(MsgKeys.C2s_Unknown_ex_effect, tokens[index]), lineNumber);
        return false;
    }

    private bool TryReadColor(string[] tokens, int index, int lineNumber, out Color color)
    {
        color = Color.DEF;
        if (index >= tokens.Length || string.IsNullOrWhiteSpace(tokens[index])) return false;
        if (Enum.TryParse(tokens[index], true, out color) && Enum.IsDefined(color)) return true;

        ReportAtLine(Severity.Warning, Msg.Create(MsgKeys.C2s_Unknown_air_color, tokens[index]), lineNumber);
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

    private bool TryGetInt(string[] tokens, int index, int lineNumber, string field, out int value)
    {
        value = default;
        if (index < tokens.Length &&
            int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Invalid_field, field), lineNumber,
            index < tokens.Length ? tokens[index] : null);
        return false;
    }

    private bool TryGetDecimal(string[] tokens, int index, int lineNumber, string field, out decimal value)
    {
        value = default;
        if (index < tokens.Length &&
            decimal.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        ReportAtLine(Severity.Error, Msg.Create(MsgKeys.C2s_Invalid_field, field), lineNumber,
            index < tokens.Length ? tokens[index] : null);
        return false;
    }

    private void ReportAtLine(Severity severity, MessageDescriptor message, int lineNumber, object? target = null)
    {
        Diagnostic.Report(new LocationDiagnostic(severity, message, lineNumber, Path)
        {
            Target = target
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
        for (var lineNumber = 1;; lineNumber++)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            lines.Add(new SourceLine(lineNumber, line));
        }

        return [.. lines];
    }

    private readonly record struct SourceLine(int Number, string Text);

    private readonly record struct NoteBase(Time Tick, int Lane, int Width);

    private sealed record PendingPair(c2sModel.Note Note, string ParentId, int LineNumber);
}
