using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Media;

public class JacketConverter
{
    public JacketConverter(JacketConvertRequest request, IMediaTool mediaTool)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mediaTool);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutPath);

        MediaTool = mediaTool;
        InPath = request.InPath;
        OutPath = request.OutPath;
    }

    private IMediaTool MediaTool { get; }
    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private string InPath { get; }
    private string OutPath { get; }

    public async Task<OperationResult> ConvertAsync(CancellationToken ct = default)
    {
        if (!Validate()) return OperationResult.Failure().WithDiagnostics(Diagnostic);

        ct.ThrowIfCancellationRequested();
        await MediaTool.ConvertJacketAsync(InPath, OutPath, ct);
        ct.ThrowIfCancellationRequested();
        return OperationResult.Success().WithDiagnostics(Diagnostic);
    }

    private bool Validate()
    {
        if (File.Exists(InPath)) return true;

        Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Jacket_file_not_found), InPath));
        return false;
    }
}