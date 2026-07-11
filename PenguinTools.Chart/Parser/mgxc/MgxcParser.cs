using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;

namespace PenguinTools.Chart.Parser.mgxc;

using umgr = Models.umgr;

public partial class MgxcParser
{
    private const string HeaderMgxc = "MGXC"; // 4D 47 58 43
    private const string HeaderMeta = "meta"; // 6D 65 74 61
    private const string HeaderEvnt = "evnt"; // 65 76 6E 74
    private const string HeaderDat2 = "dat2"; // 64 61 74 32

    public MgxcParser(MgxcParseRequest request, IMediaTool mediaTool)
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
    private umgr.Chart Mgxc { get; } = new();

    private void ReportAtPosition(Severity severity, MessageDescriptor message, long position, object? target = null)
    {
        Diagnostic.Report(new LocationDiagnostic(severity, message, checked((int)position), Path)
        {
            Target = target
        });
    }

    private void ReportAtPosition(Severity severity, MessageDescriptor message, int tick, long position,
        object? target = null)
    {
        Diagnostic.Report(new TimedLocationDiagnostic(severity, message, checked((int)position), tick, Path)
        {
            Target = target
        });
    }

    private void ThrowAtPosition(MessageDescriptor message, long position, object? target = null, int? tick = null)
    {
        if (tick is { } resolvedTick)
            throw new TimedLocationDiagnosticException(message, checked((int)position), resolvedTick, Path, target);

        throw new LocationDiagnosticException(message, checked((int)position), Path, target);
    }

    public async Task<OperationResult<umgr.Chart>> ParseAsync(CancellationToken ct = default)
    {
        try
        {
            Mgxc.Meta.FilePath = Path;

            await using var fs = File.OpenRead(Path);
            using var br = new BinaryReader(fs);

            var header = br.ReadUtf8String(4);
            if (header != HeaderMgxc)
                ThrowAtPosition(Msg.Create(MsgKeys.Error_Invalid_Header, header, HeaderMgxc), fs.Position - 4);

            br.ReadInt32(); // MGXC Block Size
            br.ReadInt32(); // unknown

            br.ReadBlock(HeaderMeta, ParseMeta);

            br.ReadBlock(HeaderEvnt, ParseEvent);

            Diagnostic.TimeCalculator = Mgxc.GetCalculator();

            br.ReadBlock(HeaderDat2, ParseNote);

            var post = new ChartPostProcessor(Mgxc, Diagnostic, Assets);
            post.Run();
            ProcessMeta();

            await Task.WhenAll(Tasks);
            return OperationResult<umgr.Chart>.Success(Mgxc).WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.Report(ex);
            return OperationResult<umgr.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
    }

    private void ProcessMeta()
    {
        if (string.IsNullOrWhiteSpace(Mgxc.Meta.SortName))
        {
            Mgxc.Meta.SortName = ChartPostProcessor.GetSortName(Mgxc.Meta.Title);
            Diagnostic.Report(new Diagnostic(Severity.Information, Msg.Key(MsgKeys.Mg_No_sortname_provided)));
        }

        if (Mgxc.Meta.IsCustomStage && !string.IsNullOrWhiteSpace(Mgxc.Meta.FullBgiFilePath))
            QueueValidation(
                MediaTool.CheckImageValidAsync(Mgxc.Meta.FullBgiFilePath),
                Mgxc.Meta.FullBgiFilePath,
                MsgKeys.Error_Invalid_bg_image,
                () =>
                {
                    Mgxc.Meta.IsCustomStage = false;
                    Mgxc.Meta.BgiFilePath = string.Empty;
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
}