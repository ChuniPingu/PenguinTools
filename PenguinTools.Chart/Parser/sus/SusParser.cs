using System.Globalization;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;

namespace PenguinTools.Chart.Parser.sus;

using umgr = Models.umgr;

public sealed class SusParser
{
    private const int DefaultTicksPerBeat = 480;

    private const decimal UmgrTicksPerBeat =
        ChartResolution.UmiguriTick / (decimal)UmiguriParserCommon.DefaultBeatDenominator;

    private static readonly int[] RoundedAirWidths = [1, 2, 3, 4, 6, 8, 16];
    private readonly Dictionary<int, List<RawNotePoint>> _airHoldPoints = [];

    private readonly Dictionary<string, decimal> _bpmDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RawNotePoint> _directionalPoints = [];
    private readonly Dictionary<(int Channel, int Lane), List<RawNotePoint>> _holdPoints = [];
    private readonly Dictionary<int, decimal> _measureLengthDefinitions = [];
    private readonly List<RawTokenPoint> _pendingBpmChanges = [];
    private readonly Dictionary<int, List<RawNotePoint>> _slidePoints = [];
    private readonly List<RawNotePoint> _tapPoints = [];
    private readonly Dictionary<int, List<TilDefinitionPoint>> _tilDefinitions = [];

    private int _currentHispeedId;
    private int _measureBase;
    private int _susTicksPerBeat = DefaultTicksPerBeat;

    public SusParser(SusParseRequest request, IMediaTool mediaTool)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mediaTool);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        ArgumentNullException.ThrowIfNull(request.Assets);

        MediaTool = mediaTool;
        Path = request.Path;
        Assets = request.Assets;
    }

    private IMediaTool MediaTool { get; }
    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private string Path { get; }
    private AssetManager Assets { get; }
    private List<Task> Tasks { get; } = [];
    private umgr.Chart Sus { get; } = new();

    public async Task<OperationResult<umgr.Chart>> ParseAsync(CancellationToken ct = default)
    {
        try
        {
            Sus.Meta.FilePath = Path;
            var lines = await ReadLinesAsync(Path, ct);

            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                ParseLine(line);
            }

            BuildChart();
            Diagnostic.TimeCalculator = Sus.GetCalculator();
            Diagnostic.BackfillTimeCalculator();

            var post = new ChartPostProcessor(Sus, Diagnostic, Assets);
            post.Run();
            ProcessMeta();

            await Task.WhenAll(Tasks);
            return OperationResult<umgr.Chart>.Success(Sus).WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.TimeCalculator ??= Sus.GetCalculator();
            Diagnostic.BackfillTimeCalculator();
            Diagnostic.Report(ex);
            return OperationResult<umgr.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
    }

    private void ParseLine(SourceLine line)
    {
        var text = line.Text.Trim();
        if (!text.StartsWith('#')) return;

        var body = text[1..].Trim();
        if (body.Length == 0) return;

        var colonIndex = body.IndexOf(':');
        if (char.IsDigit(body[0]))
        {
            if (colonIndex <= 0)
            {
                WarnMalformedLine(line.Number, body);
                return;
            }

            ParseMeasureDataLine(body[..colonIndex].Trim(), body[(colonIndex + 1)..].Trim(), line.Number);
            return;
        }

        if (colonIndex > 0)
        {
            ParseDefinitionLine(body[..colonIndex].Trim(), body[(colonIndex + 1)..].Trim(), line.Number);
            return;
        }

        ParseCommandLine(body, line.Number);
    }

    private void ParseDefinitionLine(string header, string data, int lineNumber)
    {
        var name = header.ToUpperInvariant();
        if (name.Length == 5 && name.StartsWith("BPM", StringComparison.Ordinal))
        {
            if (!decimal.TryParse(Unquote(data), CultureInfo.InvariantCulture, out var bpm))
            {
                WarnMalformedLine(lineNumber, $"{header}: {data}");
                return;
            }

            _bpmDefinitions[name[3..]] = bpm;
            return;
        }

        if (name.Length == 5 && name.StartsWith("TIL", StringComparison.Ordinal))
        {
            if (!TryParseBase36(name[3..], out var tilId))
            {
                WarnMalformedLine(lineNumber, $"{header}: {data}");
                return;
            }

            ParseTilDefinition(tilId, Unquote(data), lineNumber);
            return;
        }

        if (name.Length == 5 && name.StartsWith("ATR", StringComparison.Ordinal))
        {
            ReportIgnoredMeta(lineNumber, name, data);
            return;
        }

        ReportIgnoredMeta(lineNumber, name, data);
    }

    private void ParseCommandLine(string body, int lineNumber)
    {
        SplitNameValue(body, out var rawName, out var rawValue);
        var name = rawName.ToUpperInvariant();
        var value = Unquote(rawValue);

        switch (name)
        {
            case "TITLE":
                Sus.Meta.Title = value;
                break;
            case "ARTIST":
                Sus.Meta.Artist = value;
                break;
            case "DESIGNER":
            case "DESINGER":
                Sus.Meta.Designer = value;
                break;
            case "DIFFICULTY":
                HandleDifficulty(value);
                break;
            case "PLAYLEVEL":
                HandlePlayLevel(value);
                break;
            case "SONGID":
                Sus.Meta.MgxcId = value;
                if (int.TryParse(value, out var id)) Sus.Meta.Id = id;
                break;
            case "WAVE":
                Sus.Meta.BgmFilePath = value;
                if (!string.IsNullOrWhiteSpace(value))
                    QueueValidation(
                        MediaTool.CheckAudioValidAsync(Sus.Meta.FullBgmFilePath),
                        Sus.Meta.FullBgmFilePath,
                        MsgKeys.Error_Invalid_audio,
                        () => Sus.Meta.BgmFilePath = string.Empty);
                break;
            case "WAVEOFFSET":
                if (decimal.TryParse(value, CultureInfo.InvariantCulture, out var waveOffset))
                    Sus.Meta.BgmManualOffset = waveOffset;
                break;
            case "JACKET":
                Sus.Meta.JacketFilePath = value;
                if (!string.IsNullOrWhiteSpace(value))
                    QueueValidation(
                        MediaTool.CheckImageValidAsync(Sus.Meta.FullJacketFilePath),
                        Sus.Meta.FullJacketFilePath,
                        MsgKeys.Error_Invalid_jk_image,
                        () => Sus.Meta.JacketFilePath = string.Empty);
                break;
            case "BACKGROUND":
                Sus.Meta.BgiFilePath = value;
                Sus.Meta.IsCustomStage = !string.IsNullOrWhiteSpace(value);
                break;
            case "REQUEST":
                HandleRequest(value, lineNumber);
                break;
            case "HISPEED":
                if (TryParseBase36(value, out var speedId)) _currentHispeedId = speedId;
                break;
            case "NOSPEED":
                ReportIgnoredMeta(lineNumber, name, value);
                break;
            case "MEASUREBS":
                if (int.TryParse(value, out var measureBase)) _measureBase = measureBase;
                break;
            case "MEASUREHS":
                if (TryParseBase36(value, out var mainTil)) Sus.Meta.MainTil = mainTil;
                break;
            case "SUBTITLE":
            case "GENRE":
            case "MOVIE":
            case "MOVIEOFFSET":
            case "BASEBPM":
            case "ATTRIBUTE":
            case "NOATTRIBUTE":
                ReportIgnoredMeta(lineNumber, name, value);
                break;
            default:
                ReportIgnoredMeta(lineNumber, name, value);
                break;
        }
    }

    private void HandleDifficulty(string value)
    {
        if (int.TryParse(value, out var numeric))
        {
            Sus.Meta.Difficulty = numeric == 4
                ? Difficulty.WorldsEnd
                : UmiguriParserCommon.DifficultyFromValue(numeric);
        }
        else
        {
            var normalized = value.Trim().ToUpperInvariant();
            Sus.Meta.Difficulty = normalized switch
            {
                "BASIC" => Difficulty.Basic,
                "ADVANCED" => Difficulty.Advanced,
                "EXPERT" => Difficulty.Expert,
                "MASTER" => Difficulty.Master,
                "ULTIMA" => Difficulty.Ultima,
                _ when normalized.StartsWith("WORLD", StringComparison.Ordinal)
                       || normalized.StartsWith("WE", StringComparison.Ordinal)
                       || normalized.Contains(':') => Difficulty.WorldsEnd,
                _ => Sus.Meta.Difficulty
            };
        }

        if (Sus.Meta.Difficulty == Difficulty.WorldsEnd)
        {
            Sus.Meta.Stage = UmiguriParserCommon.CreateWorldsEndStage();
            if (TryParseWorldsEndStars(value, out var starDifficulty)) Sus.Meta.WeDifficulty = starDifficulty;
        }
    }

    private void HandlePlayLevel(string value)
    {
        var trimmed = value.Trim();
        var plus = trimmed.EndsWith('+');
        trimmed = trimmed.TrimEnd('+');

        if (!int.TryParse(trimmed, out var numericLevel)) return;

        if (Sus.Meta.Difficulty == Difficulty.WorldsEnd)
        {
            Sus.Meta.WeDifficulty = numericLevel switch
            {
                1 => StarDifficulty.S1,
                2 => StarDifficulty.S2,
                3 => StarDifficulty.S3,
                4 => StarDifficulty.S4,
                5 => StarDifficulty.S5,
                _ => StarDifficulty.Na
            };
            return;
        }

        Sus.Meta.Level = numericLevel + (plus ? 0.5m : 0m);
    }

    private void HandleRequest(string request, int lineNumber)
    {
        var tokens = request.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return;

        switch (tokens[0].ToLowerInvariant())
        {
            case "ticks_per_beat" when tokens.Length >= 2 && int.TryParse(tokens[1], out var ticksPerBeat) &&
                                       ticksPerBeat > 0:
                _susTicksPerBeat = ticksPerBeat;
                break;
            case "metronome":
                break;
            default:
                ReportIgnoredMeta(lineNumber, "REQUEST", request);
                break;
        }
    }

    private void ParseTilDefinition(int tilId, string text, int lineNumber)
    {
        if (!_tilDefinitions.TryGetValue(tilId, out var definitions))
        {
            definitions = [];
            _tilDefinitions[tilId] = definitions;
        }

        foreach (var entry in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colonIndex = entry.IndexOf(':');
            var tickIndex = entry.IndexOf('\'');
            if (colonIndex <= 0 || tickIndex <= 0 || tickIndex >= colonIndex)
            {
                WarnMalformedLine(lineNumber, entry);
                continue;
            }

            if (!int.TryParse(entry.AsSpan(0, tickIndex), out var measure) ||
                !int.TryParse(entry.AsSpan(tickIndex + 1, colonIndex - tickIndex - 1), out var tick) ||
                !decimal.TryParse(entry.AsSpan(colonIndex + 1), CultureInfo.InvariantCulture, out var speed))
            {
                WarnMalformedLine(lineNumber, entry);
                continue;
            }

            definitions.Add(new TilDefinitionPoint(measure, tick, speed));
        }
    }

    private void ParseMeasureDataLine(string header, string data, int lineNumber)
    {
        if (header.Length is not (5 or 6) || !int.TryParse(header.AsSpan(0, 3), out var measure))
        {
            WarnMalformedLine(lineNumber, $"{header}: {data}");
            return;
        }

        var effectiveMeasure = _measureBase + measure;
        var suffix = header[3..].ToUpperInvariant();

        if (suffix == "02")
        {
            if (decimal.TryParse(data, CultureInfo.InvariantCulture, out var beats) && beats > 0)
                _measureLengthDefinitions[effectiveMeasure] = beats;
            else
                WarnMalformedLine(lineNumber, $"{header}: {data}");

            return;
        }

        if (suffix == "08")
        {
            var tokens = EnumerateTokens(data).ToArray();
            for (var i = 0; i < tokens.Length; i++)
                if (!string.Equals(tokens[i], "00", StringComparison.OrdinalIgnoreCase))
                    _pendingBpmChanges.Add(new RawTokenPoint(effectiveMeasure, i, tokens.Length, tokens[i],
                        lineNumber));

            return;
        }

        if (suffix.Length == 2 && suffix[0] is '1' or '5')
        {
            if (!TryParseBase36(suffix[1].ToString(), out var lane))
            {
                WarnMalformedLine(lineNumber, $"{header}: {data}");
                return;
            }

            AddShortNotePoints(
                suffix[0] == '1' ? _tapPoints : _directionalPoints,
                effectiveMeasure,
                lane,
                data,
                lineNumber);
            return;
        }

        if (suffix.Length == 3 && suffix[0] is '2' or '3' or '4')
        {
            if (!TryParseBase36(suffix[1].ToString(), out var lane) ||
                !TryParseBase36(suffix[2].ToString(), out var channel))
            {
                WarnMalformedLine(lineNumber, $"{header}: {data}");
                return;
            }

            var target = suffix[0] switch
            {
                '2' => GetOrCreate(_holdPoints, (channel, lane)),
                '3' => GetOrCreate(_slidePoints, channel),
                '4' => GetOrCreate(_airHoldPoints, channel),
                _ => null
            };

            if (target != null) AddLongNotePoints(target, effectiveMeasure, lane, data, lineNumber);
            return;
        }

        ReportIgnoredMeta(lineNumber, header, data);
    }

    private void AddShortNotePoints(List<RawNotePoint> target, int measure, int lane, string data, int lineNumber)
    {
        var tokens = EnumerateTokens(data).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (string.Equals(token, "00", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryParsePair(token, out var kind, out var width))
            {
                WarnMalformedLine(lineNumber, token);
                continue;
            }

            target.Add(new RawNotePoint(measure, i, tokens.Length, lane, width, kind, _currentHispeedId, lineNumber));
        }
    }

    private void AddLongNotePoints(List<RawNotePoint> target, int measure, int lane, string data, int lineNumber)
    {
        var tokens = EnumerateTokens(data).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (string.Equals(token, "00", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryParsePair(token, out var kind, out var width))
            {
                WarnMalformedLine(lineNumber, token);
                continue;
            }

            target.Add(new RawNotePoint(measure, i, tokens.Length, lane, width, kind, _currentHispeedId, lineNumber));
        }
    }

    private void BuildChart()
    {
        var timing = BuildMeasureTiming();
        BuildBeatEvents(timing);
        BuildBpmEvents(timing);
        BuildTilEvents(timing);
        BuildTapNotes(timing);
        BuildHoldNotes(timing);
        BuildSlideNotes(timing);
        BuildDirectionalNotes(timing);
        BuildAirHoldNotes(timing);
    }

    private MeasureTiming BuildMeasureTiming()
    {
        var maxMeasure = 0;
        maxMeasure = Math.Max(maxMeasure, _measureLengthDefinitions.Keys.DefaultIfEmpty(0).Max());
        maxMeasure = Math.Max(maxMeasure, _pendingBpmChanges.Select(p => p.Measure).DefaultIfEmpty(0).Max());
        maxMeasure = Math.Max(maxMeasure, _tapPoints.Select(p => p.Measure).DefaultIfEmpty(0).Max());
        maxMeasure = Math.Max(maxMeasure, _directionalPoints.Select(p => p.Measure).DefaultIfEmpty(0).Max());
        maxMeasure = Math.Max(maxMeasure,
            _holdPoints.Values.SelectMany(p => p).Select(p => p.Measure).DefaultIfEmpty(0).Max());
        maxMeasure = Math.Max(maxMeasure,
            _slidePoints.Values.SelectMany(p => p).Select(p => p.Measure).DefaultIfEmpty(0).Max());
        maxMeasure =
            Math.Max(maxMeasure,
                _airHoldPoints.Values.SelectMany(p => p).Select(p => p.Measure).DefaultIfEmpty(0).Max());
        maxMeasure = Math.Max(maxMeasure,
            _tilDefinitions.Values.SelectMany(p => p).Select(p => p.Measure).DefaultIfEmpty(0).Max());

        Dictionary<int, decimal> beatsByMeasure = [];
        Dictionary<int, int> startTicksByMeasure = [];

        var currentBeats = _measureLengthDefinitions.TryGetValue(0, out var firstBeats)
            ? firstBeats
            : UmiguriParserCommon.DefaultBeatNumerator;
        var ticks = 0;

        for (var measure = 0; measure <= maxMeasure; measure++)
        {
            if (_measureLengthDefinitions.TryGetValue(measure, out var definedBeats)) currentBeats = definedBeats;
            beatsByMeasure[measure] = currentBeats;
            startTicksByMeasure[measure] = ticks;
            ticks += MeasureTicksFromBeats(currentBeats);
        }

        return new MeasureTiming(beatsByMeasure, startTicksByMeasure, maxMeasure);
    }

    private void BuildBeatEvents(MeasureTiming timing)
    {
        decimal? previousBeats = null;
        for (var measure = 0; measure <= timing.MaxMeasure; measure++)
        {
            var beats = timing.BeatsAt(measure);
            if (measure > 0 && previousBeats == beats) continue;

            var fraction = ToBeatSignature(beats);
            Sus.Events.AppendChild(new umgr.BeatEvent
            {
                Bar = measure,
                Tick = timing.StartTickAt(measure),
                Numerator = fraction.Numerator,
                Denominator = fraction.Denominator
            });

            previousBeats = beats;
        }
    }

    private void BuildBpmEvents(MeasureTiming timing)
    {
        foreach (var change in _pendingBpmChanges.OrderBy(p => timing.ToTick(p.Measure, p.Index, p.Count)))
        {
            if (!_bpmDefinitions.TryGetValue(change.Token, out var bpm))
            {
                ReportAtLine(Severity.Warning, Msg.Create(MsgKeys.Sus_Bpm_definition_not_found, change.Token),
                    change.Line);
                continue;
            }

            Sus.Events.AppendChild(new umgr.BpmEvent
            {
                Tick = timing.ToTick(change.Measure, change.Index, change.Count),
                Bpm = bpm
            });
        }
    }

    private void BuildTilEvents(MeasureTiming timing)
    {
        foreach (var (tilId, definitions) in _tilDefinitions)
        foreach (var definition in definitions.OrderBy(p => timing.ToSusTick(p.Measure, p.Tick, _susTicksPerBeat)))
            Sus.Events.AppendChild(new umgr.ScrollSpeedEvent
            {
                Timeline = tilId,
                Tick = timing.ToSusTick(definition.Measure, definition.Tick, _susTicksPerBeat),
                Speed = definition.Speed
            });
    }

    private void BuildTapNotes(MeasureTiming timing)
    {
        foreach (var point in _tapPoints.OrderBy(p => timing.ToTick(p.Measure, p.Index, p.Count)).ThenBy(p => p.Lane))
        {
            umgr.PositiveNote? note = point.Kind switch
            {
                1 => new umgr.Tap(),
                2 => new umgr.ExTap(),
                3 => new umgr.Flick(),
                4 => new umgr.Damage(),
                _ => null
            };

            if (note == null)
            {
                ReportAtLine(Severity.Information, Msg.Create(MsgKeys.Sus_Unsupported_tap_point_type, point.Kind),
                    point.Line,
                    timing.ToTick(point.Measure, point.Index, point.Count));
                continue;
            }

            note.Tick = timing.ToTick(point.Measure, point.Index, point.Count);
            note.Lane = point.Lane;
            note.Width = point.Width;
            note.Timeline = point.Timeline;
            Sus.Notes.AppendChild(note);
        }
    }

    private void BuildHoldNotes(MeasureTiming timing)
    {
        foreach (var points in _holdPoints.Values)
        {
            umgr.Hold? active = null;
            foreach (var point in ResolveLongPoints(points, timing))
            {
                switch (point.Kind)
                {
                    case 1:
                        active = new umgr.Hold
                        {
                            Tick = point.Tick,
                            Lane = point.Lane,
                            Width = point.Width,
                            Timeline = point.Timeline
                        };
                        Sus.Notes.AppendChild(active);
                        break;
                    case 2:
                    case 3:
                        if (active == null)
                        {
                            ReportAtLine(Severity.Warning, Msg.Key(MsgKeys.Sus_Hold_joint_ignored),
                                point.Line, point.Tick);
                            break;
                        }

                        active.AppendChild(new umgr.HoldJoint
                        {
                            Tick = point.Tick,
                            Timeline = active.Timeline
                        });

                        if (point.Kind == 2) active = null;
                        break;
                    default:
                        ReportAtLine(Severity.Information,
                            Msg.Create(MsgKeys.Sus_Unsupported_hold_point_type, point.Kind), point.Line,
                            point.Tick);
                        break;
                }
            }
        }
    }

    private void BuildSlideNotes(MeasureTiming timing)
    {
        foreach (var points in _slidePoints.Values)
        {
            umgr.Slide? active = null;
            foreach (var point in ResolveLongPoints(points, timing))
            {
                switch (point.Kind)
                {
                    case 1:
                        active = new umgr.Slide
                        {
                            Tick = point.Tick,
                            Lane = point.Lane,
                            Width = point.Width,
                            Timeline = point.Timeline
                        };
                        Sus.Notes.AppendChild(active);
                        break;
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                        if (active == null)
                        {
                            ReportAtLine(Severity.Warning,
                                Msg.Key(MsgKeys.Sus_Slide_joint_ignored), point.Line,
                                point.Tick);
                            break;
                        }

                        active.AppendChild(new umgr.SlideJoint
                        {
                            Tick = point.Tick,
                            Lane = point.Lane,
                            Width = point.Width,
                            Timeline = active.Timeline,
                            Joint = point.Kind == 4 ? Joint.C : Joint.D
                        });

                        if (point.Kind == 2) active = null;
                        break;
                    default:
                        ReportAtLine(Severity.Information,
                            Msg.Create(MsgKeys.Sus_Unsupported_slide_point_type, point.Kind),
                            point.Line, point.Tick);
                        break;
                }
            }
        }
    }

    private void BuildDirectionalNotes(MeasureTiming timing)
    {
        foreach (var point in _directionalPoints.OrderBy(p => timing.ToTick(p.Measure, p.Index, p.Count))
                     .ThenBy(p => p.Lane))
        {
            var tick = timing.ToTick(point.Measure, point.Index, point.Count);
            var pairPositive = FindPairPositive(tick, point.Lane, point.Width);
            if (pairPositive == null)
            {
                ReportAtLine(Severity.Warning, Msg.Key(MsgKeys.Sus_Air_note_ignored),
                    point.Line, tick);
                continue;
            }

            ApplyAirWidthRounding(pairPositive, point.Width);

            var air = new umgr.Air
            {
                Tick = tick,
                Timeline = point.Timeline,
                Direction = point.Kind switch
                {
                    2 => AirDirection.DW,
                    3 => AirDirection.UL,
                    4 => AirDirection.UR,
                    5 => AirDirection.DL,
                    6 => AirDirection.DR,
                    _ => AirDirection.IR
                },
                Color = Color.DEF
            };

            pairPositive.MakePair(air);
            Sus.Notes.AppendChild(air);
        }
    }

    private void BuildAirHoldNotes(MeasureTiming timing)
    {
        foreach (var points in _airHoldPoints.Values)
        {
            umgr.AirHold? active = null;
            foreach (var point in ResolveLongPoints(points, timing))
            {
                switch (point.Kind)
                {
                    case 1:
                    {
                        var pairPositive = FindPairPositive(point.Tick, point.Lane, point.Width);
                        if (pairPositive == null)
                        {
                            ReportAtLine(Severity.Warning,
                                Msg.Key(MsgKeys.Sus_Air_hold_ignored), point.Line,
                                point.Tick);
                            active = null;
                            break;
                        }

                        var attachedAir = Sus.Notes.Children.OfType<umgr.Air>()
                            .LastOrDefault(air =>
                                air.Tick.Original == point.Tick && ReferenceEquals(air.PairNote, pairPositive));
                        if (attachedAir != null) Sus.Notes.RemoveChild(attachedAir);

                        active = new umgr.AirHold
                        {
                            Tick = point.Tick,
                            Timeline = point.Timeline,
                            Color = Color.DEF
                        };
                        pairPositive.MakePair(active);
                        Sus.Notes.AppendChild(active);
                        break;
                    }
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                        if (active == null)
                        {
                            ReportAtLine(Severity.Warning,
                                Msg.Key(MsgKeys.Sus_Air_hold_joint_ignored), point.Line,
                                point.Tick);
                            break;
                        }

                        active.AppendChild(new umgr.AirHoldJoint
                        {
                            Tick = point.Tick,
                            Timeline = active.Timeline,
                            Joint = point.Kind == 4 ? Joint.C : Joint.D
                        });

                        if (point.Kind == 2) active = null;
                        break;
                    default:
                        ReportAtLine(Severity.Information,
                            Msg.Create(MsgKeys.Sus_Unsupported_air_hold_point_type, point.Kind),
                            point.Line, point.Tick);
                        break;
                }
            }
        }
    }

    private IEnumerable<ResolvedLongPoint> ResolveLongPoints(IEnumerable<RawNotePoint> points, MeasureTiming timing)
    {
        return points.Select(point => new ResolvedLongPoint(
                timing.ToTick(point.Measure, point.Index, point.Count),
                point.Lane,
                point.Width,
                point.Kind,
                point.Timeline,
                point.Line))
            .OrderBy(point => point.Tick)
            .ThenBy(point => LongPointPriority(point.Kind))
            .ThenBy(point => point.Lane);
    }

    private umgr.PositiveNote? FindPairPositive(int tick, int lane, int width)
    {
        return EnumeratePositiveNotes(Sus.Notes)
            .Where(note => note.Tick.Original == tick)
            .Where(note => note.Lane <= lane && note.Lane + note.Width >= lane + width)
            .OrderBy(note => note.Width)
            .ThenBy(note => Math.Abs(note.Lane - lane))
            .FirstOrDefault();
    }

    private static IEnumerable<umgr.PositiveNote> EnumeratePositiveNotes(umgr.Note parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is umgr.PositiveNote positive) yield return positive;
            foreach (var nested in EnumeratePositiveNotes(child)) yield return nested;
        }
    }

    private static void ApplyAirWidthRounding(umgr.PositiveNote pairPositive, int width)
    {
        var roundedWidth = RoundedAirWidths
            .OrderBy(candidate => Math.Abs(candidate - width))
            .ThenBy(candidate => candidate)
            .First();

        switch (pairPositive)
        {
            case umgr.HoldJoint { Parent: umgr.Hold hold }:
                hold.Width = roundedWidth;
                break;
            default:
                pairPositive.Width = roundedWidth;
                break;
        }
    }

    private void ProcessMeta()
    {
        if (string.IsNullOrWhiteSpace(Sus.Meta.SortName))
        {
            Sus.Meta.SortName = ChartPostProcessor.GetSortName(Sus.Meta.Title);
            Diagnostic.Report(new Diagnostic(Severity.Information, Msg.Key(MsgKeys.Mg_No_sortname_provided)));
        }

        if (Sus.Meta.IsCustomStage && !string.IsNullOrWhiteSpace(Sus.Meta.FullBgiFilePath))
            QueueValidation(
                MediaTool.CheckImageValidAsync(Sus.Meta.FullBgiFilePath),
                Sus.Meta.FullBgiFilePath,
                MsgKeys.Error_Invalid_bg_image,
                () =>
                {
                    Sus.Meta.IsCustomStage = false;
                    Sus.Meta.BgiFilePath = string.Empty;
                });
    }

    private void QueueValidation(Task<ProcessCommandResult> validationTask, string path, string messageKey,
        Action onFailure)
    {
        Tasks.Add(HandleValidationAsync(validationTask, path, messageKey, onFailure));
    }

    private async Task HandleValidationAsync(Task<ProcessCommandResult> validationTask, string path, string messageKey,
        Action onFailure)
    {
        try
        {
            var result = await validationTask;
            if (result.IsSuccess) return;

            onFailure();
            Diagnostic.Report(new PathDiagnostic(Severity.Warning, Msg.Key(messageKey), path)
            {
                Target = result
            });
        }
        catch (Exception ex)
        {
            onFailure();
            Diagnostic.Report(new PathDiagnostic(Severity.Warning, Msg.Key(messageKey), path)
            {
                Target = ex
            });
        }
    }

    private void ReportIgnoredMeta(int lineNumber, string name, string value)
    {
        ReportAtLine(Severity.Information, Msg.Create(MsgKeys.Mg_Unrecognized_meta, name, value), lineNumber);
    }

    private void WarnMalformedLine(int lineNumber, string value)
    {
        ReportAtLine(Severity.Warning, Msg.Create(MsgKeys.Sus_Malformed_line, value), lineNumber);
    }

    private void ReportAtLine(Severity severity, MessageDescriptor message, int lineNumber, object? target = null)
    {
        Diagnostic.Report(new LocationDiagnostic(severity, message, lineNumber, Path)
        {
            Target = target
        });
    }

    private void ReportAtLine(Severity severity, MessageDescriptor message, int lineNumber, int tick,
        object? target = null)
    {
        Diagnostic.Report(new TimedLocationDiagnostic(severity, message, lineNumber, tick, Path)
        {
            Target = target
        });
    }

    private static string[] EnumerateTokens(string data)
    {
        var normalized = new string(data.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var count = normalized.Length / 2;
        var tokens = new string[count];
        for (var i = 0; i < count; i++) tokens[i] = normalized.Substring(i * 2, 2).ToUpperInvariant();
        return tokens;
    }

    private static bool TryParsePair(string token, out int kind, out int width)
    {
        kind = -1;
        width = -1;
        if (token.Length != 2) return false;
        kind = Base36(token[0]);
        width = Base36(token[1]);
        return kind > 0 && width > 0;
    }

    private static bool TryParseBase36(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var c in text.Trim())
        {
            var digit = Base36(c);
            if (digit < 0) return false;
            value = value * 36 + digit;
        }

        return true;
    }

    private static int Base36(char c)
    {
        if (c is >= '0' and <= '9') return c - '0';
        if (c is >= 'a' and <= 'z') return 10 + c - 'a';
        if (c is >= 'A' and <= 'Z') return 10 + c - 'A';
        return -1;
    }

    private static void SplitNameValue(string text, out string name, out string value)
    {
        var separator = text.IndexOfAny([' ', '\t']);
        if (separator < 0)
        {
            name = text;
            value = string.Empty;
            return;
        }

        name = text[..separator];
        value = text[(separator + 1)..].Trim();
    }

    private static string Unquote(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' ? trimmed[1..^1] : trimmed;
    }

    private static int MeasureTicksFromBeats(decimal beats)
    {
        return (int)Math.Round(beats * UmgrTicksPerBeat, MidpointRounding.AwayFromZero);
    }

    private static Fraction ToBeatSignature(decimal beats)
    {
        var fraction = ToFraction(beats);
        var numerator = fraction.Numerator;
        var denominator = fraction.Denominator * UmiguriParserCommon.DefaultBeatDenominator;

        while (denominator > UmiguriParserCommon.DefaultBeatDenominator)
        {
            var gcd = GreatestCommonDivisor(Math.Abs(numerator), denominator);
            if (gcd <= 1) break;

            var reducedDenominator = denominator / gcd;
            if (reducedDenominator < UmiguriParserCommon.DefaultBeatDenominator) break;

            numerator /= gcd;
            denominator = reducedDenominator;
        }

        return new Fraction(numerator, denominator);
    }

    private static Fraction ToFraction(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        if (dot < 0) return new Fraction(int.Parse(text, CultureInfo.InvariantCulture), 1);

        var digits = text.Replace(".", string.Empty, StringComparison.Ordinal);
        var denominator = 1;
        for (var i = dot + 1; i < text.Length; i++) denominator *= 10;

        var numerator = int.Parse(digits, CultureInfo.InvariantCulture);
        var gcd = GreatestCommonDivisor(Math.Abs(numerator), denominator);
        return new Fraction(numerator / gcd, denominator / gcd);
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a == 0 ? 1 : a;
    }

    private static bool TryParseWorldsEndStars(string value, out StarDifficulty difficulty)
    {
        difficulty = StarDifficulty.Na;
        var starCount = value.Count(ch => ch is '*' or '☆');
        difficulty = starCount switch
        {
            1 => StarDifficulty.S1,
            2 => StarDifficulty.S2,
            3 => StarDifficulty.S3,
            4 => StarDifficulty.S4,
            5 => StarDifficulty.S5,
            _ => StarDifficulty.Na
        };
        return difficulty != StarDifficulty.Na;
    }

    private static TValue GetOrCreate<TKey, TValue>(IDictionary<TKey, TValue> dict, TKey key) where TKey : notnull
        where TValue : new()
    {
        if (dict.TryGetValue(key, out var value)) return value;
        value = new TValue();
        dict[key] = value;
        return value;
    }

    private static int LongPointPriority(int kind)
    {
        return kind switch
        {
            2 => 0,
            3 or 4 or 5 => 1,
            1 => 2,
            _ => 3
        };
    }

    private static async Task<SourceLine[]> ReadLinesAsync(string path, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, ct);
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

    private readonly record struct RawTokenPoint(int Measure, int Index, int Count, string Token, int Line);

    private readonly record struct RawNotePoint(
        int Measure,
        int Index,
        int Count,
        int Lane,
        int Width,
        int Kind,
        int Timeline,
        int Line);

    private readonly record struct ResolvedLongPoint(int Tick, int Lane, int Width, int Kind, int Timeline, int Line);

    private readonly record struct TilDefinitionPoint(int Measure, int Tick, decimal Speed);

    private readonly record struct Fraction(int Numerator, int Denominator);

    private sealed class MeasureTiming(
        Dictionary<int, decimal> beatsByMeasure,
        Dictionary<int, int> startTicksByMeasure,
        int maxMeasure)
    {
        public int MaxMeasure { get; } = maxMeasure;

        public decimal BeatsAt(int measure)
        {
            return beatsByMeasure.TryGetValue(measure, out var beats)
                ? beats
                : UmiguriParserCommon.DefaultBeatNumerator;
        }

        public int StartTickAt(int measure)
        {
            return startTicksByMeasure.TryGetValue(measure, out var tick) ? tick : 0;
        }

        public int ToTick(int measure, int index, int count)
        {
            var start = StartTickAt(measure);
            if (count <= 0) return start;

            var measureTicks = MeasureTicksFromBeats(BeatsAt(measure));
            return start + (int)Math.Round((decimal)measureTicks * index / count, MidpointRounding.AwayFromZero);
        }

        public int ToSusTick(int measure, int susTick, int susTicksPerBeat)
        {
            var start = StartTickAt(measure);
            return start + (int)Math.Round(susTick * UmgrTicksPerBeat / susTicksPerBeat, MidpointRounding.AwayFromZero);
        }
    }
}