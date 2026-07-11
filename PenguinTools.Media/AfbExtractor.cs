using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Media;

public class AfbExtractor
{
    public AfbExtractor(AfbExtractRequest request, IMediaTool mediaTool)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mediaTool);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutFolder);

        MediaTool = mediaTool;
        InPath = request.InPath;
        OutFolder = request.OutFolder;
    }

    private IMediaTool MediaTool { get; }
    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private string InPath { get; }
    private string OutFolder { get; }

    public async Task<OperationResult> ExtractAsync(CancellationToken ct = default)
    {
        if (!Validate()) return OperationResult.Failure().WithDiagnostics(Diagnostic);

        await MediaTool.ExtractDdsAsync(InPath, OutFolder, ct);
        ct.ThrowIfCancellationRequested();
        return OperationResult.Success().WithDiagnostics(Diagnostic);
    }

    private bool Validate()
    {
        if (File.Exists(InPath)) return true;

        Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_File_not_found), InPath));
        return false;
    }
}