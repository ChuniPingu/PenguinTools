using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser.ugc;

public partial class UgcParser
{
    private int? _currentLineNumber;

    private int _currentTimeline;
    private int _sourceTicks = 480;
    private readonly PenguinTools.Core.Metadata.ChartDialect _requestedDialect;
    private PenguinTools.Core.Metadata.ChartDialect _dialect;
    private bool _highPrecision;
    private bool _legacyColors;
    private bool _inferredFixedOrigin;
    private bool Extended => _dialect == PenguinTools.Core.Metadata.ChartDialect.Extended;

    private void SelectDialect(SourceLine[] lines)
    {
        var marker = lines.Select(l => l.Text).FirstOrDefault(l => l.StartsWith(EditorCompatibility.UgcMarker, StringComparison.Ordinal));
        var marked = marker?[EditorCompatibility.UgcMarker.Length..].Trim().ToLowerInvariant() switch
        {
            "original" or "margrete-1.8.0" => PenguinTools.Core.Metadata.ChartDialect.Original,
            "extended" or "margrete-2.3.2" => PenguinTools.Core.Metadata.ChartDialect.Extended,
            null => PenguinTools.Core.Metadata.ChartDialect.Auto,
            _ => throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header, marker!, "chart dialect"))
        };
        var copyright = lines.Select(l => l.Text).FirstOrDefault(l => l.StartsWith("@COPYRIGHT\t", StringComparison.OrdinalIgnoreCase));
        var copyrightValue = copyright is null ? "" : copyright[11..];
        var retainedExtras = ChartExtras.FromCopyright(copyrightValue);
        // Older writers used MGR_CLKCNT plus a source snapshot before MGR_CLK_DEF existed.
        // A decoded C2S snapshot distinguishes that encoding from a clock-count annotation alone.
        var legacySnapshot = retainedExtras.SourceSnapshot.StartsWith("HASH\t", StringComparison.Ordinal) &&
            retainedExtras.SourceSnapshot.Contains("\nRESOLUTION\t", StringComparison.Ordinal);
        var legacyWriter = lines.Any(l => l.Text.StartsWith("@COPYRIGHT\t", StringComparison.Ordinal) &&
            ((l.Text.Contains("MGR_SYNC_REV=", StringComparison.Ordinal) && l.Text.Contains("PT_C2S_", StringComparison.Ordinal)) ||
             (l.Text.Contains("MGR_CLKCNT=", StringComparison.Ordinal) && legacySnapshot)));
        var retained = retainedExtras.EncodingDialect;
        _legacyColors = _requestedDialect == PenguinTools.Core.Metadata.ChartDialect.Auto &&
            marked == PenguinTools.Core.Metadata.ChartDialect.Auto && retained == PenguinTools.Core.Metadata.ChartDialect.Auto && legacyWriter;
        // These frozen fallbacks read files written before stable format markers existed.
        // Unknown editor releases must not silently select an incompatible time axis.
        var creator = lines.Select(l => l.Text).FirstOrDefault(l => l.StartsWith("' Created with Margrete ", StringComparison.Ordinal));
        var legacyFixedAxis = creator?.StartsWith("' Created with Margrete v2.3.", StringComparison.Ordinal) == true;
        var generatedFixedAxis = creator is null && lines.Any(l =>
            l.Text.StartsWith("@C2S_BASECOUNT\t", StringComparison.OrdinalIgnoreCase));
        _inferredFixedOrigin = legacyFixedAxis || generatedFixedAxis;
        var legacyMusicalAxis = creator is null || creator.StartsWith("' Created with Margrete v1.7.", StringComparison.Ordinal) ||
            creator.StartsWith("' Created with Margrete v1.8.", StringComparison.Ordinal);
        if (_requestedDialect == PenguinTools.Core.Metadata.ChartDialect.Auto &&
            marked == PenguinTools.Core.Metadata.ChartDialect.Auto && retained == PenguinTools.Core.Metadata.ChartDialect.Auto &&
            !legacyWriter && !legacyFixedAxis && !legacyMusicalAxis)
        {
            throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header, creator!,
                "UGC format marker or --input-dialect Original/Extended"));
        }
        _dialect = _requestedDialect != PenguinTools.Core.Metadata.ChartDialect.Auto ? _requestedDialect :
            marked != PenguinTools.Core.Metadata.ChartDialect.Auto ? marked :
            retained != PenguinTools.Core.Metadata.ChartDialect.Auto ? retained :
            legacyWriter || legacyFixedAxis || generatedFixedAxis ? PenguinTools.Core.Metadata.ChartDialect.Extended : PenguinTools.Core.Metadata.ChartDialect.Original;
        _highPrecision = Extended;
    }

    private int Lane(char value)
    {
        if (!Extended && UgcPayload.Base36(value) < 0)
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, value.ToString(), "base UGC coordinate"));
        }
        return UgcPayload.Lane(value);
    }

    private decimal Height(ReadOnlySpan<char> value) => UgcPayload.Height36(value) * (_highPrecision ? 1 : 10);


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

    static UgcParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public UgcParser(UgcParseRequest request, IMediaTool mediaTool)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mediaTool);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        ArgumentNullException.ThrowIfNull(request.Assets);

        MediaTool = mediaTool;
        Path = request.Path;
        Assets = request.Assets;
        _requestedDialect = request.Dialect;
        Ugc.Meta.ExVersion = 0;
        Ugc.Meta.ExLongEnabled = false;
    }

    private IMediaTool MediaTool { get; }
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

            SelectDialect(lines);
            foreach (var pending in lines.Where(l => l.Text.Trim().StartsWith("'" + ChartExtras.PendingSnapshotShiftPrefix, StringComparison.Ordinal)))
            {
                ChartExtras.RejectPendingSnapshotShift(pending.Text.Trim()[1..]);
            }
            ParseLines(lines, ct);
            foreach (var origin in lines.Where(l => l.Text.Trim().StartsWith("'MGR_ORIGIN=", StringComparison.Ordinal)))
            {
                Ugc.Extras.FixedZeroOrigin |= ChartExtras.HasFixedZeroOrigin(origin.Text.Trim()[1..]);
            }
            foreach (var preroll in lines.Where(l => l.Text.Trim().StartsWith("'MGR_PREROLL=", StringComparison.Ordinal)))
            {
                Ugc.Extras.PrerollBar1Applied |= ChartExtras.HasPrerollBar1(preroll.Text.Trim()[1..]);
            }
            if (_requestedDialect == PenguinTools.Core.Metadata.ChartDialect.Auto && _inferredFixedOrigin)
            {
                Ugc.Extras.FixedZeroOrigin = true;
            }
            Ugc.Extras.ApplyEditorMode(_requestedDialect, Ugc.Meta);
            Ugc.Extras.RestoreUserText(Ugc.Meta);
            Ugc.Extras.EncodingDialect = _dialect;
            Ugc.Meta.FieldColorName = Ugc.Extras.EditorFieldColorName;
            Ugc.Extras.RestoreEditorEvents(Ugc);
            Ugc.Extras.EditorComments = lines.Select(l => l.Text).Where(l => l.StartsWith('\'') &&
                !l.StartsWith("' Created with Margrete ", StringComparison.Ordinal) &&
                l.Trim() != "'" + ChartExtras.FixedZeroOriginMarker &&
                l.Trim() != "'" + ChartExtras.PrerollBar1Marker &&
                !l.StartsWith(EditorCompatibility.UgcMarker, StringComparison.Ordinal)).ToList();

            Ugc.Extras.CheckEventView(Ugc);
            var post = new ChartPostProcessor(Ugc, Diagnostic, Assets);
            post.Run();
            Ugc.Extras.BinarySnapshotValid = Ugc.Extras.UgcContentKey == Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines.Where(l => !l.Text.StartsWith("@COPYRIGHT", StringComparison.Ordinal)).Select(l => l.Text)))));
            if (Ugc.Extras.BinarySnapshotValid)
            {
                Ugc.Extras.RestoreAppearance(Ugc);
            }

            Ugc.Extras.SpeedModelKey = ChartExtras.SpeedKey(Ugc);
            Ugc.Extras.ParsedEventModelKey = C2SRoundTrip.ViewHash(ChartExtras.EventView(Ugc).Split('\n'));
            Ugc.Extras.AirModelKey = C2sRoundTripKeys.FormatAirEditKey(Ugc);
            Ugc.Extras.SlaModelKey = C2sRoundTripKeys.FormatSlaEditKey(Ugc);
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
        Diagnostic.TimeCalculator = Ugc.GetCalculator();

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
                DispatchBodyLine(line.Text);
            }
        }

        ClearCurrentLine();
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
    }

    private void ClearCurrentLine()
    {
        _currentLineNumber = null;
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
            Target = target
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
            throw new TimedLocationDiagnosticException(message, line, timedTick, Path, target);
        }

        throw new LocationDiagnosticException(message, line, Path, target);
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
            Diagnostic.Report(new Diagnostic(Severity.Information, Msg.Key(MsgKeys.Mg_No_sortname_provided)));
        }

        if (Ugc.Meta.IsCustomStage && !string.IsNullOrWhiteSpace(Ugc.Meta.FullBgiFilePath))
        {
            QueueValidation(
                MediaTool.CheckImageValidAsync(Ugc.Meta.FullBgiFilePath),
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
