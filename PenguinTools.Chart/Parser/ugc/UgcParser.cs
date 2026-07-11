using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;

namespace PenguinTools.Chart.Parser.ugc;

using umgr = Models.umgr;

public partial class UgcParser
{
    private int? _currentLineNumber;

    private int _currentTimeline;
    private umgr.Note? _lastNote;
    private umgr.Note? _lastParentNote;

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
    }

    private IMediaTool MediaTool { get; }
    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private string Path { get; }
    private AssetManager Assets { get; }
    private List<Task> Tasks { get; } = [];
    private umgr.Chart Ugc { get; } = new();

    public async Task<OperationResult<umgr.Chart>> ParseAsync(CancellationToken ct = default)
    {
        try
        {
            Ugc.Meta.FilePath = Path;
            var lines = await ReadLinesAsync(Path, ct);

            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                SetCurrentLine(line);
                if (line.Text.StartsWith('@')) DispatchHeaderLine(line.Text);
            }

            ClearCurrentLine();
            BuildBarAxis();

            _currentTimeline = 0;
            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                SetCurrentLine(line);
                if (line.Text.StartsWith("@USETIL", StringComparison.Ordinal)) ApplyUseTil(line.Text);
                else if (line.Text.StartsWith('#')) DispatchBodyLine(line.Text);
            }

            ClearCurrentLine();
            Diagnostic.TimeCalculator = Ugc.GetCalculator();

            var post = new ChartPostProcessor(Ugc, Diagnostic, Assets);
            post.Run();
            ProcessMeta();

            await Task.WhenAll(Tasks);
            return OperationResult<umgr.Chart>.Success(Ugc).WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.Report(ex);
            return OperationResult<umgr.Chart>.Failure().WithDiagnostics(Diagnostic);
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

        using var reader = new StringReader(text);
        var lines = new List<SourceLine>();
        for (var lineNumber = 1;; lineNumber++)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            lines.Add(new SourceLine(lineNumber, line));
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

    private void ReportAtCurrentLine(Severity severity, MessageDescriptor message, int tick, object? target = null)
    {
        if (_currentLineNumber is not { } line)
        {
            Diagnostic.Report(new TimedDiagnostic(severity, message, tick)
            {
                Target = target
            });
            return;
        }

        Diagnostic.Report(new TimedLocationDiagnostic(severity, message, line, tick, Path)
        {
            Target = target
        });
    }

    private void ThrowAtCurrentLine(MessageDescriptor message, object? target = null, int? tick = null)
    {
        if (_currentLineNumber is not { } line)
        {
            if (tick is { } resolvedTick) throw new TimedDiagnosticException(message, resolvedTick, target);
            throw new DiagnosticException(message, target);
        }

        if (tick is { } timedTick) throw new TimedLocationDiagnosticException(message, line, timedTick, Path, target);
        throw new LocationDiagnosticException(message, line, Path, target);
    }

    private void BuildBarAxis()
    {
        var beats = Ugc.Events.Children.OfType<umgr.BeatEvent>().OrderBy(b => b.Bar).ToList();
        if (beats.Count == 0 || beats[0].Bar != 0)
        {
            var defaultBeat = new umgr.BeatEvent
            {
                Bar = 0,
                Numerator = DefaultBeatNumerator,
                Denominator = DefaultBeatDenominator,
                Tick = 0
            };
            Ugc.Events.AppendChild(defaultBeat);
            beats.Insert(0, defaultBeat);
        }

        if (beats.Count > 0)
        {
            beats[0].Tick = 0;
            var accum = 0;
            for (var i = 0; i < beats.Count - 1; i++)
            {
                var curr = beats[i];
                var next = beats[i + 1];
                accum += ChartResolution.UmiguriTick * curr.Numerator / curr.Denominator * (next.Bar - curr.Bar);
                next.Tick = accum;
            }
        }

        foreach (var (bar, tick, bpm) in _pendingBpms)
            Ugc.Events.AppendChild(new umgr.BpmEvent { Tick = BarTickToAbsTick(bar, tick), Bpm = bpm });

        foreach (var (bar, tick, spd) in _pendingSpdMods)
            Ugc.Events.AppendChild(new umgr.NoteSpeedEvent { Tick = BarTickToAbsTick(bar, tick), Speed = spd });

        foreach (var (tilId, bar, tick, spd) in _pendingTils)
            Ugc.Events.AppendChild(new umgr.ScrollSpeedEvent
                { Timeline = tilId, Tick = BarTickToAbsTick(bar, tick), Speed = spd });
    }

    private void ProcessMeta()
    {
        if (string.IsNullOrWhiteSpace(Ugc.Meta.SortName))
        {
            Ugc.Meta.SortName = ChartPostProcessor.GetSortName(Ugc.Meta.Title);
            Diagnostic.Report(new Diagnostic(Severity.Information, Msg.Key(MsgKeys.Mg_No_sortname_provided)));
        }

        if (Ugc.Meta.IsCustomStage && !string.IsNullOrWhiteSpace(Ugc.Meta.FullBgiFilePath))
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

    private readonly record struct SourceLine(int Number, string Text);
}