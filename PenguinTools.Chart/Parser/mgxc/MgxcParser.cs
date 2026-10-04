using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Parser.mgxc;

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
    private DiagnosticCollector Diagnostic { get; } = new();
    private string Path { get; }
    private AssetManager Assets { get; }
    private List<Task> Tasks { get; } = [];
    private UmgrModel.Chart Mgxc { get; } = new();

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
        {
            throw new TimedLocationDiagnosticException(message, checked((int)position), resolvedTick, Path, target);
        }

        throw new LocationDiagnosticException(message, checked((int)position), Path, target);
    }

    public async Task<OperationResult<UmgrModel.Chart>> ParseAsync(CancellationToken ct = default)
    {
        try
        {
            Mgxc.Meta.FilePath = Path;

            await using var fs = File.OpenRead(Path);
            using var br = new BinaryReader(fs);

            var header = br.ReadUtf8String(4);
            if (header != HeaderMgxc)
            {
                ThrowAtPosition(Msg.Create(MsgKeys.Error_Invalid_Header, header, HeaderMgxc), fs.Position - 4);
            }

            br.ReadInt32(); // MGXC Block Size
            br.ReadInt32(); // unknown

            br.ReadBlock(HeaderMeta, ParseMeta);
            if (_privateMetadata.Length > 0)
            {
                var clickEnabled = Mgxc.Extras.ClickEnabled;
                var tutorial = Mgxc.Extras.Tutorial;
                Mgxc.Extras = ChartExtras.FromCopyright(_copyright + _privateMetadata);
                Mgxc.Extras.ClickEnabled = clickEnabled;
                Mgxc.Extras.Tutorial = tutorial;
            }

            if (ChartMetaCommands.IsIgnored(Mgxc.Meta.Comment))
            {
                await Task.WhenAll(Tasks);
                return ChartMetaCommands.SkipParse(Diagnostic, Path);
            }

            using var payload = new MemoryStream();
            void Capture(BinaryReader reader, Action<BinaryReader> parse, bool isEvent)
            {
                var start = reader.BaseStream.Position;
                parse(reader);
                var end = reader.BaseStream.Position;
                if (isEvent && _lastEventWasExtras)
                {
                    return;
                }

                reader.BaseStream.Position = start;
                payload.Write(reader.ReadBytes(checked((int)(end - start))));
            }
            br.ReadBlock(HeaderEvnt, reader => Capture(reader, ParseEvent, true));
            PenguinTools.Core.Metadata.C2sRoundTripComment.Absorb(Mgxc.Meta, Mgxc.Extras.RoundTripBookmarks);

            Diagnostic.TimeCalculator = Mgxc.GetCalculator();

            br.ReadBlock(HeaderDat2, reader => Capture(reader, ParseNote, false));
            Mgxc.Extras.BinarySnapshotValid = Mgxc.Extras.BinaryContentKey ==
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload.ToArray()));
            if (!Mgxc.Extras.BinarySnapshotValid)
            {
                Mgxc.Extras.HasSpeedSnapshot = false;
            }

            if (Mgxc.Extras.BinarySnapshotValid)
            {
                foreach (var crash in Mgxc.Notes.Children.OfType<UmgrModel.AirCrash>()
                             .Where(crash => Mgxc.Extras.TraceCrashes.Contains(ChartExtras.CrashKey(crash))))
                {
                    crash.Attr = Models.AirLadderAttr.Trace;
                }
            }

            Mgxc.Extras.CheckEventView(Mgxc);
            var post = new ChartPostProcessor(Mgxc, Diagnostic, Assets);
            post.Run();
            if (Mgxc.Extras.BinarySnapshotValid)
            {
                Mgxc.Extras.RestoreAppearance(Mgxc);
            }

            Mgxc.Extras.SpeedModelKey = ChartExtras.SpeedKey(Mgxc);
            Mgxc.Extras.ParsedEventModelKey = C2SRoundTrip.ViewHash(ChartExtras.EventView(Mgxc).Split('\n'));
            Mgxc.Extras.AirModelKey = C2sRoundTripKeys.FormatAirEditKey(Mgxc);
            Mgxc.Extras.SlaModelKey = C2sRoundTripKeys.FormatSlaEditKey(Mgxc);
            ProcessMeta();

            await Task.WhenAll(Tasks);
            return OperationResult<UmgrModel.Chart>.Success(Mgxc).WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.TimeCalculator ??= Mgxc.GetCalculator();
            Diagnostic.BackfillTimeCalculator();
            Diagnostic.Report(ex);
            return OperationResult<UmgrModel.Chart>.Failure().WithDiagnostics(Diagnostic);
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
        {
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
    }

    private void QueueValidation(Task<MediaValidationResult> validationTask, string path, string messageKey,
        Action onFailure)
    {
        Tasks.Add(MediaValidation.ReportAsync(validationTask, path, messageKey, onFailure, Diagnostic));
    }

}
