using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Media;

public class AudioConverter
{
    public AudioConverter(AudioConvertRequest request, IMediaTool mediaTool)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mediaTool);
        ArgumentNullException.ThrowIfNull(request.Meta);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingAudioPath);

        MediaTool = mediaTool;
        Meta = request.Meta;
        OutFolder = request.OutFolder;
        WorkingAudioPath = request.WorkingAudioPath;
        HcaEncryptionKey = request.HcaEncryptionKey;
    }

    private IMediaTool MediaTool { get; }
    private ulong HcaEncryptionKey { get; }
    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private Meta Meta { get; }
    private string OutFolder { get; }
    private string WorkingAudioPath { get; }

    public async Task<OperationResult> ConvertAsync(CancellationToken ct = default)
    {
        if (!Validate()) return OperationResult.Failure().WithDiagnostics(Diagnostic);

        var songId = Meta.Id ?? throw new DiagnosticException(MsgKeys.Error_Song_id_is_not_set);

        if (Meta.BgmPreviewStart > 120)
            Diagnostic.Report(new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_Preview_later_than_120)));

        var srcPath = Meta.FullBgmFilePath;
        var wavPath = WorkingAudioPath;

        var ret = await MediaTool.NormalizeAudioAsync(srcPath, wavPath, Meta.BgmRealOffset, ct);
        if (ret.IsNoOperation) wavPath = srcPath;

        ct.ThrowIfCancellationRequested();

        var xml = new CueFileXml(songId);
        var outputDir = await xml.SaveDirectoryAsync(OutFolder);

        var pvStart = Meta.BgmPreviewStart;
        var pvStop = Meta.BgmPreviewStop;
        if (Meta.BgmEnableBarOffset)
        {
            pvStart += Meta.BgmBarOffset;
            pvStop += Meta.BgmBarOffset;
        }

        var maxSeconds = Math.Floor(uint.MaxValue / 1000m);
        var originalPvStart = pvStart;
        var originalPvStop = pvStop;
        pvStart = Math.Clamp(pvStart, 0, maxSeconds);
        pvStop = Math.Clamp(pvStop, 0, maxSeconds);

        if (originalPvStart > maxSeconds)
        {
            MessageDescriptor msg = Msg.Create(MsgKeys.Hint_Preview_value_clamped, nameof(Meta.BgmPreviewStart),
                originalPvStart,
                maxSeconds);
            Diagnostic.Report(new Diagnostic(Severity.Information, msg));
        }

        if (originalPvStop > maxSeconds)
        {
            MessageDescriptor msg = Msg.Create(MsgKeys.Hint_Preview_value_clamped, nameof(Meta.BgmPreviewStop),
                originalPvStop,
                maxSeconds);
            Diagnostic.Report(new Diagnostic(Severity.Information, msg));
        }

        var acbPath = Path.Combine(outputDir, xml.AcbFile);
        var awbPath = Path.Combine(outputDir, xml.AwbFile);

        await MediaTool.ConvertCriAsync(
            wavPath,
            acbPath,
            awbPath,
            xml.DataName,
            (long)(pvStart * 1000m),
            (long)(pvStop * 1000m),
            HcaEncryptionKey,
            ct);

        return OperationResult.Success().WithDiagnostics(Diagnostic);
    }

    private bool Validate()
    {
        var hasError = false;
        if (Meta.Id is null)
        {
            Diagnostic.Report(new Diagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Song_id_is_not_set)));
            hasError = true;
        }

        if (Meta.BgmPreviewStop < Meta.BgmPreviewStart)
        {
            Diagnostic.Report(new Diagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Preview_stop_greater_than_start)));
            hasError = true;
        }

        var path = Meta.FullBgmFilePath;
        if (!File.Exists(path))
        {
            Diagnostic.Report(new PathDiagnostic(Severity.Error, Msg.Key(MsgKeys.Error_Audio_file_not_found), path));
            hasError = true;
        }

        return !hasError;
    }
}