using System.Text;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser.ugc;

public partial class UgcParser
{
    private int? _currentLineNumber;
    private string? _currentLineText;

    private int _currentTimeline;
    private int _sourceTicks = 480;
    private int _version = 8;
    private int? _declaredVersion;
    private bool _highPrecision;
    private bool _fixedBarCoordinates;
    private readonly List<UmgrModel.Note?> _sourceRoots = [];

    private int Lane(char value)
    {
        if (_version >= 9)
        {
            var digit = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".IndexOf(value);
            if (digit < 0)
            {
                ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, value, "UGC v9 base62 coordinate"));
            }

            return digit - 16;
        }
        if (MargreteInput.LegacyLane(value) is { } legacy)
        {
            return legacy;
        }
        if (UgcPayload.Base36(value) < 0)
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, value.ToString(), "base UGC coordinate"));
        }
        return UgcPayload.Base36(value);
    }

    private int Width(char value) => _version >= 9 ? UgcPayload.Base36(value) : Lane(value);

    private int HeightDigits => _highPrecision ? 2 : 1;

    private decimal Height(ReadOnlySpan<char> value)
    {
        // Earlier extended exports retained the fixed field length by encoding
        // the leading height digit in the same private-use ranges as lanes.
        // This field is independent of v9's base62 lane encoding.
        var height = MargreteInput.LegacyLane(value[0]) ?? UgcPayload.Base36(value[0]);
        if (height < 0 && MargreteInput.LegacyLane(value[0]) is null)
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, value.ToString(), "UGC AIR height encoding"));
        }
        foreach (var character in value[1..])
        {
            var digit = UgcPayload.Base36(character);
            if (digit < 0)
            {
                ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, value.ToString(), "base36 trailing AIR height digit"));
            }
            height = checked(height * 36 + digit);
        }
        return height * (_highPrecision ? 1m : 10m);
    }


    private int ScaleTick(int tick)
    {
        var scaled = (long)tick * 480;
        if (scaled % _sourceTicks != 0)
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, tick, "exact 1/1920 tick"));
        }

        return checked((int)(scaled / _sourceTicks));
    }
    private UmgrModel.Note? _lastNote;
    private UmgrModel.Note? _lastParentNote;
    private readonly UgcSnapshotTracker _snapshots = new();

    static UgcParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public UgcParser(UgcParseRequest request, IMediaValidator mediaValidator)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mediaValidator);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        ArgumentNullException.ThrowIfNull(request.Assets);

        MediaValidator = mediaValidator;
        Path = request.Path;
        Assets = request.Assets;
        Ugc.Meta.ExVersion = 0;
        Ugc.Meta.ExLongEnabled = false;
    }

    private IMediaValidator MediaValidator { get; }
    private DiagnosticCollector Diagnostic { get; } = new();
    private string Path { get; }
    private AssetManager Assets { get; }
    private List<Task> Tasks { get; } = [];
    private UmgrModel.Chart Ugc { get; } = new();

    public async Task<OperationResult<UmgrModel.Chart>> ParseAsync(CancellationToken ct = default)
    {
        try
        {
            Ugc.Meta.FilePath = Path;
            var lines = await ReadLinesAsync(Path, ct);

            if (TryGetIgnoreLine(lines, out var ignoreLine))
            {
                return ChartMetaCommands.SkipParse(Diagnostic, Path, ignoreLine);
            }

            // Reject unsupported versions before interpreting their records.
            foreach (var line in lines.Where(l => l.Text.StartsWith("@VER\t", StringComparison.OrdinalIgnoreCase)))
            {
                SetCurrentLine(line);
                HandleVer(line.Text.Split('\t').Skip(1).ToArray());
            }
            ClearCurrentLine();

            _fixedBarCoordinates = MargreteInput.FixedUgcCoordinates(lines.Select(l => l.Text), _version);

            foreach (var pending in lines.Where(l => l.Text.Trim().StartsWith("'" + ChartExtras.PendingSnapshotShiftPrefix, StringComparison.Ordinal)))
            {
                ChartExtras.RejectPendingSnapshotShift(pending.Text.Trim()[1..]);
            }
            ParseLines(lines, ct);
            SlideSegmentMetadata.ReadComments(Ugc, lines.Select(l => l.Text.Trim()), _sourceRoots);
            LegacyCrashMetadata.Read(Ugc, lines.Select(l => l.Text.Trim()));
            Ugc.Extras.FixedZeroOrigin |= _fixedBarCoordinates;
            foreach (var origin in lines.Where(l => l.Text.Trim().StartsWith("'MGR_ORIGIN=", StringComparison.Ordinal)))
            {
                Ugc.Extras.FixedZeroOrigin |= ChartExtras.HasFixedZeroOrigin(origin.Text.Trim()[1..]);
            }
            foreach (var preroll in lines.Where(l => l.Text.Trim().StartsWith("'MGR_PREROLL=", StringComparison.Ordinal)))
            {
                Ugc.Extras.PrerollBar1Applied |= ChartExtras.HasPrerollBar1(preroll.Text.Trim()[1..]);
            }
            Ugc.Extras.ApplyCoordinateOrigin(Ugc.Meta);
            Ugc.Extras.RestoreUserText(Ugc.Meta);
            Ugc.Meta.FieldColorName = Ugc.Extras.EditorFieldColorName;
            Ugc.Extras.RestoreEditorEvents(Ugc);
            Ugc.Extras.EditorComments = lines.Select(l => l.Text).Where(l => l.StartsWith('\'') &&
                !l.StartsWith("' Created with Margrete ", StringComparison.Ordinal) &&
                !l.TrimStart('\'', ' ').StartsWith(MargreteInput.DialectPrefix, StringComparison.Ordinal) &&
                !l.StartsWith(SlideSegmentMetadata.CommentPrefix, StringComparison.Ordinal) &&
                !l.StartsWith(LegacyCrashMetadata.Prefix, StringComparison.Ordinal) &&
                l.Trim() != "'" + ChartExtras.FixedZeroOriginMarker &&
                l.Trim() != "'" + ChartExtras.PrerollBar1Marker).ToList();

            EditorNumbers.Restore(Ugc);
            Ugc.Extras.CheckEventView(Ugc);
            var (matches, contextValid, slaValid) = _snapshots.Validate(Ugc.Extras, lines.Select(l => l.Text).ToArray(), Ugc.Meta.C2sSlaSnapshot);
            RestoreExTapSnapshots(matches);
            var post = new ChartPostProcessor(Ugc, Diagnostic, Assets);
            post.Run();
            var eligible = Ugc.Extras.BinarySnapshotValid ? null : matches.Keys.SelectMany(n => new[] { n }.Concat(n.Children)).ToHashSet();
            if (Ugc.Extras.BinarySnapshotValid || matches.Count > 0)
            {
                Ugc.Extras.RestoreAppearance(Ugc, eligible);
            }
            EditorProjection.Restore(Ugc, eligible, restoreEvents: contextValid);
            MargreteCurveMetadata.RestoreProfiles(Ugc);
            NoteWidthRules.Validate(Ugc);
            Ugc.Extras.EditorFormatVersion = _version;

            Ugc.Extras.SpeedModelKey = ChartExtras.SpeedKey(Ugc);
            Ugc.Extras.ParsedEventModelKey = C2SRoundTrip.OrderedViewHash(ChartExtras.EventView(Ugc).Split('\n'));
            Ugc.Extras.AirModelKey = C2sRoundTripKeys.FormatAirEditKey(Ugc);
            Ugc.Extras.SlaModelKey = C2sRoundTripKeys.FormatSlaEditKey(Ugc);
            Ugc.Extras.CheckLegacyColors(Ugc, capture: true);
            ValidateSlaSnapshot(slaValid);
            ProcessMeta();

            await Task.WhenAll(Tasks);
            return OperationResult<UmgrModel.Chart>.Success(Ugc).WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.TimeCalculator ??= Ugc.GetCalculator();
            Diagnostic.BackfillTimeCalculator();
            Diagnostic.Report(ex);
            return OperationResult<UmgrModel.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
    }

    private static void RestoreExTapSnapshots(Dictionary<UmgrModel.Note, EditorNoteSnapshot> matches)
    {
        foreach (var (note, snapshot) in matches)
        {
            if (note is UmgrModel.ExTap ex && snapshot.ExTap is { } saved)
            {
                RestoreExTap(ex, saved);
            }
        }
    }

    private static void RestoreExTap(UmgrModel.ExTap ex, ExTapRoleSnapshot saved)
    {
        ex.Role = saved.Role;
        ex.AirActionParent = saved.Parent;
        ex.AirActionParentJoint = saved.ParentJoint;
        ex.AirActionParentIsEx = saved.ParentHasExEffect;
    }

    private void ValidateSlaSnapshot(bool valid)
    {
        if (Ugc.Extras.UgcSnapshotVersion != 2 || Ugc.Extras.UgcSlaKey is null)
        {
            return;
        }
        if (valid)
        {
            Ugc.Meta.C2sSlaEditKey = Ugc.Extras.SlaModelKey;
        }
        else
        {
            Ugc.Meta.C2sSlaSnapshot = null;
            Ugc.Meta.C2sSlaEditKey = null;
        }
    }

    private void ParseLines(SourceLine[] lines, CancellationToken ct)
    {
        // Restore extension defaults first; native fields take precedence
        // even when an editor moves COPYRIGHT to the end of the header.
        foreach (var line in lines.OrderBy(line =>
                     line.Text.Split('\t')[0].ToUpperInvariant() switch
                     {
                         "@COPYRIGHT" => 0,
                         "@DIFF" => 1,
                         _ => 2
                     }))
        {
            ct.ThrowIfCancellationRequested();
            SetCurrentLine(line);
            if (line.Text.StartsWith('@'))
            {
                DispatchHeaderLine(line.Text);
            }
        }

        ClearCurrentLine();
        BuildBarAxis();
        if (_sourceTicks != 480)
        {
            UgcSpeedFields.NormalizeResolution(Ugc.Extras.EditorHeaders, ScaleTick);
        }
        Diagnostic.TimeCalculator = Ugc.GetCalculator();

        NormalizeLegacyAirHoldBlocks(lines);
        _currentTimeline = 0;
        // Restore extension defaults first; native fields take precedence
        // even when an editor moves COPYRIGHT to the end of the header.
        foreach (var line in lines.OrderBy(line =>
                     line.Text.Split('\t')[0].Equals("@COPYRIGHT", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            ct.ThrowIfCancellationRequested();
            SetCurrentLine(line);
            if (line.Text.StartsWith("@USETIL", StringComparison.Ordinal))
            {
                ApplyUseTil(line.Text);
            }
            else if (line.Text.StartsWith('#'))
            {
                ParseBodyLine(line.Text);
            }
        }

        ClearCurrentLine();
    }

    private void ParseBodyLine(string text)
    {
        var previous = _lastNote;
        DispatchBodyLine(text);
        if (text.Contains('\''))
        {
            _sourceRoots.Add(_lastNote);
        }
        if (!text.Contains('\''))
        {
            _snapshots.Append(text, _currentTimeline);
        }
        else if (_lastNote is { } note && !ReferenceEquals(note, previous) && ReferenceEquals(note.Parent, Ugc.Notes))
        {
            _snapshots.Begin(note, text, _currentTimeline);
        }
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

        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        var lines = new List<SourceLine>();
        var lineNumber = 1;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lines.Add(new SourceLine(lineNumber, line));
            lineNumber++;
        }

        return [.. lines];
    }

    private void SetCurrentLine(SourceLine line)
    {
        _currentLineNumber = line.Number;
        _currentLineText = line.Text;
    }

    private void ClearCurrentLine()
    {
        _currentLineNumber = null;
        _currentLineText = null;
    }

    private void ReportAtCurrentLine(Severity severity, MessageDescriptor message, object? target = null)
    {
        if (_currentLineNumber is not { } line)
        {
            Diagnostic.Report(new Diagnostic(severity, message)
            {
                Target = target
            });
            return;
        }

        Diagnostic.Report(new LocationDiagnostic(severity, message, line, Path)
        {
            Target = target ?? new SourceLineDiagnosticTarget(_currentLineText!)
        });
    }

    private void ThrowAtCurrentLine(MessageDescriptor message, object? target = null, int? tick = null)
    {
        if (_currentLineNumber is not { } line)
        {
            if (tick is { } resolvedTick)
            {
                throw new TimedDiagnosticException(message, resolvedTick, target);
            }

            throw new DiagnosticException(message, target);
        }

        if (tick is { } timedTick)
        {
            throw new TimedLocationDiagnosticException(message, line, timedTick, Path,
                target ?? new SourceLineDiagnosticTarget(_currentLineText!));
        }

        throw new LocationDiagnosticException(message, line, Path,
            target ?? new SourceLineDiagnosticTarget(_currentLineText!));
    }

    private void BuildBarAxis()
    {
        var beats = Ugc.Events.Children.OfType<UmgrModel.BeatEvent>().OrderBy(b => b.Bar).ToList();
        if (beats.Count == 0 || beats[0].Bar != 0)
        {
            var defaultBeat = new UmgrModel.BeatEvent
            {
                Bar = 0,
                Numerator = DefaultBeatNumerator,
                Denominator = DefaultBeatDenominator,
                Tick = 0
            };
            Ugc.Events.AppendChild(defaultBeat);
            beats.Insert(0, defaultBeat);
        }

        beats[0].Tick = 0;
        var accum = 0;
        for (var i = 0; i < beats.Count - 1; i++)
        {
            var curr = beats[i];
            var next = beats[i + 1];
            accum += ChartResolution.UmiguriTick * curr.Numerator / curr.Denominator * (next.Bar - curr.Bar);
            next.Tick = accum;
        }

        foreach (var (bar, tick, bpm) in _pendingBpms)
        {
            Ugc.Events.AppendChild(new UmgrModel.BpmEvent { Tick = BarTickToAbsTick(bar, tick), Bpm = bpm });
        }

        if (!Ugc.Events.Children.OfType<UmgrModel.BpmEvent>().Any(b => b.Tick.Original == 0) && Ugc.Meta.MainBpm > 0)
        {
            Ugc.Events.AppendChild(new UmgrModel.BpmEvent { Tick = 0, Bpm = Ugc.Meta.MainBpm });
        }
        foreach (var (bar, tick, spd) in _pendingSpdMods)
        {
            Ugc.Events.AppendChild(new UmgrModel.NoteSpeedEvent { Tick = BarTickToAbsTick(bar, tick), Speed = spd });
        }

        foreach (var (tilId, bar, tick, spd) in _pendingTils)
        {
            Ugc.Events.AppendChild(new UmgrModel.ScrollSpeedEvent
            { Timeline = tilId, Tick = BarTickToAbsTick(bar, tick), Speed = spd });
        }
    }

    private void ProcessMeta()
    {
        if (string.IsNullOrWhiteSpace(Ugc.Meta.SortName))
        {
            Ugc.Meta.SortName = ChartPostProcessor.GetSortName(Ugc.Meta.Title);
            Diagnostic.Report(new Diagnostic(Severity.Information, Msg.Create(MsgKeys.Mg_No_sortname_provided,
                ("title", Ugc.Meta.Title), ("sortName", Ugc.Meta.SortName))));
        }

        if (Ugc.Meta.IsCustomStage && !string.IsNullOrWhiteSpace(Ugc.Meta.FullBgiFilePath))
        {
            QueueValidation(
                MediaValidator.CheckImageValidAsync(Ugc.Meta.FullBgiFilePath),
                Ugc.Meta.FullBgiFilePath,
                MsgKeys.Error_Invalid_bg_image,
                () =>
                {
                    Ugc.Meta.IsCustomStage = false;
                    Ugc.Meta.BgiFilePath = string.Empty;
                });
        }
    }

    private void QueueValidation(Task<MediaValidationResult> validationTask, string path, string messageKey,
        Action onFailure)
    {
        Tasks.Add(MediaValidation.ReportAsync(validationTask, path, messageKey, onFailure, Diagnostic));
    }

    private static bool TryGetIgnoreLine(SourceLine[] lines, out int lineNumber)
    {
        lineNumber = 0;
        foreach (var line in lines)
        {
            if (!line.Text.StartsWith('@'))
            {
                continue;
            }

            var tokens = line.Text.Split('\t');
            if (tokens.Length == 0)
            {
                continue;
            }

            if (!tokens[0].TrimStart('@').Equals("CMT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var comment = tokens.Length >= 2 ? tokens[1] : string.Empty;
            if (!ChartMetaCommands.IsIgnored(comment))
            {
                continue;
            }

            lineNumber = line.Number;
            return true;
        }

        return false;
    }

    private readonly record struct SourceLine(int Number, string Text);
}
