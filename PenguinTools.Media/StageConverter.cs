using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Media;

public class StageConverter
{
    public StageConverter(StageBuildRequest request, IMediaTool mediaTool)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mediaTool);
        ArgumentNullException.ThrowIfNull(request.Assets);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BackgroundPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutFolder);

        MediaTool = mediaTool;
        Assets = request.Assets;
        BackgroundPath = request.BackgroundPath;
        EffectPaths = request.EffectPaths;
        StageId = request.StageId;
        OutFolder = request.OutFolder;
        NoteFieldLane = request.NoteFieldLane;
    }

    private IMediaTool MediaTool { get; }
    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private AssetManager Assets { get; }
    private string BackgroundPath { get; }
    private string?[]? EffectPaths { get; }
    private int? StageId { get; }
    private string OutFolder { get; }
    private Entry NoteFieldLane { get; }

    public async Task<OperationResult<Entry>> BuildAsync(CancellationToken ct = default)
    {
        if (!await ValidateAsync(ct))
            return OperationResult<Entry>.Failure().WithDiagnostics(Diagnostic);
        if (StageId is not { } stageId)
            return OperationResult<Entry>.Failure().WithDiagnostics(Diagnostic);

        var xml = new StageXml(stageId, NoteFieldLane);
        var outputDir = await xml.SaveDirectoryAsync(OutFolder);

        var nfPath = Path.Combine(outputDir, xml.NotesFieldFile);
        var stPath = Path.Combine(outputDir, xml.BaseFile);
        await MediaTool.ConvertStageAsync(BackgroundPath, stPath, nfPath, EffectPaths, ct);

        return OperationResult<Entry>.Success(xml.Name).WithDiagnostics(Diagnostic);
    }

    private async Task<bool> ValidateAsync(CancellationToken ct = default)
    {
        var hasError = false;
        var duplicates = Assets.StageNames.Where(p => p.Id == StageId);
        foreach (var d in duplicates)
            Diagnostic.Report(new Diagnostic(Severity.Warning,
                Msg.Create(MsgKeys.Warn_Stage_already_exists, d, StageId)));

        if (StageId is null)
        {
            Diagnostic.Report(new Diagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Stage_id_is_not_set)));
            hasError = true;
        }

        if (!File.Exists(BackgroundPath))
        {
            Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Background_file_not_found),
                BackgroundPath));
            hasError = true;
        }
        else
        {
            var ret = await MediaTool.CheckImageValidAsync(BackgroundPath, ct);
            if (ret.IsFailure)
            {
                Diagnostic.Report(
                    new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Invalid_bg_image), BackgroundPath)
                    {
                        Target = ret
                    });
                hasError = true;
            }
        }

        if (EffectPaths is not null)
            foreach (var p in EffectPaths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;

                if (!File.Exists(p))
                {
                    Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Effect_file_not_found),
                        p));
                    hasError = true;
                    continue;
                }

                var ret = await MediaTool.CheckImageValidAsync(p, ct);
                if (ret.IsFailure)
                {
                    Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Invalid_bg_fx_image), p)
                    {
                        Target = ret
                    });
                    hasError = true;
                }
            }

        return !hasError;
    }
}