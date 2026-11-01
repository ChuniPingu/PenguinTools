using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
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
        _requestedDialect = request.Dialect;
        Mgxc.Meta.ExVersion = 0;
        Mgxc.Meta.ExLongEnabled = false;
    }

    private readonly PenguinTools.Core.Metadata.ChartDialect _requestedDialect;
    private int _version;
    private bool _hasFixedZeroOriginMarker;
    private bool _hasPrerollBar1Marker;
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

            ReadHeader(br);
            ReadMetadata(br);

            if (ChartMetaCommands.IsIgnored(Mgxc.Meta.Comment))
            {
                await Task.WhenAll(Tasks);
                return ChartMetaCommands.SkipParse(Diagnostic, Path);
            }

            ReadPayload(br);
            PostProcessChart();

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

    private void ReadHeader(BinaryReader br)
    {
        var header = br.ReadUtf8String(4);
        if (header != HeaderMgxc)
        {
            ThrowAtPosition(Msg.Create(MsgKeys.Error_Invalid_Header, header, HeaderMgxc), br.BaseStream.Position - 4);
        }

        br.ReadInt32(); // MGXC Block Size
        _version = br.ReadInt32();
        if (_version > 4)
        {
            ThrowAtPosition(Msg.Create(MsgKeys.Error_Chart_version_too_new, "MGXC", _version, 4), br.BaseStream.Position - 4);
        }

        if (_version < 1)
        {
            ThrowAtPosition(Msg.Create(MsgKeys.Error_Invalid_Header, _version, "MGXC v1-v4"), br.BaseStream.Position - 4);
        }
    }

    private void ReadMetadata(BinaryReader br)
    {
        br.ReadBlock(HeaderMeta, ParseMeta);
        if (_privateMetadata.Length > 0 || _copyright.Contains("PT_C2S_", StringComparison.Ordinal))
        {
            var clickEnabled = Mgxc.Extras.ClickEnabled;
            var tutorial = Mgxc.Extras.Tutorial;
            Mgxc.Extras = ChartExtras.FromCopyright(_copyright + _privateMetadata);
            Mgxc.Extras.ClickEnabled = clickEnabled;
            Mgxc.Extras.Tutorial = tutorial;
        }
    }

    private void ReadPayload(BinaryReader br)
    {
        using var payload = new MemoryStream();
        br.ReadBlock(HeaderEvnt, reader => CapturePayload(reader, payload, ParseEvent, true));
        ApplyEventMetadata();

        var legacyEncoding = Mgxc.Extras.EncodingDialect == ChartDialect.Auto && Mgxc.Extras.HasCarrierMarkers;
        Mgxc.Extras.EncodingDialect = ResolveEncodingDialect(legacyEncoding);
        br.ReadBlock(HeaderDat2, reader => CapturePayload(reader, payload, ParseNote, false));
        if (_version < 3 && _requestedDialect == ChartDialect.Auto && !legacyEncoding && UsesExtendedCoordinates())
        {
            Mgxc.Extras.EncodingDialect = ChartDialect.Extended;
        }

        RestoreBinarySnapshot(payload);
    }

    private void CapturePayload(BinaryReader reader, Stream payload, Action<BinaryReader> parse, bool isEvent)
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

    private void ApplyEventMetadata()
    {
        Mgxc.Extras.FixedZeroOrigin |= _hasFixedZeroOriginMarker || ChartExtras.HasFixedZeroOrigin(_copyright);
        Mgxc.Extras.PrerollBar1Applied |= _hasPrerollBar1Marker || ChartExtras.HasPrerollBar1(_copyright);
        Mgxc.Extras.ApplyEditorMode(_version >= 3 ? ChartDialect.Auto : _requestedDialect, Mgxc.Meta);
        Mgxc.Extras.EditorImageEvents = _imageEvents;
        Mgxc.Extras.RestoreEditorMetadata(Mgxc.Meta);
        C2sRoundTripComment.Absorb(Mgxc.Meta, Mgxc.Extras.RoundTripBookmarks);

        if (!Mgxc.Events.Children.OfType<UmgrModel.BpmEvent>().Any(b => b.Tick.Original == 0) && Mgxc.Meta.MainBpm > 0)
        {
            Mgxc.Events.AppendChild(new UmgrModel.BpmEvent { Tick = 0, Bpm = Mgxc.Meta.MainBpm });
        }

        Diagnostic.TimeCalculator = Mgxc.GetCalculator();
    }

    private ChartDialect ResolveEncodingDialect(bool legacyEncoding)
    {
        if (_version >= 3)
        {
            return ChartDialect.Original;
        }

        if (_requestedDialect != ChartDialect.Auto)
        {
            return _requestedDialect;
        }

        if (Mgxc.Extras.EncodingDialect != ChartDialect.Auto)
        {
            return Mgxc.Extras.EncodingDialect;
        }

        return legacyEncoding ? ChartDialect.Extended : ChartDialect.Original;
    }

    private bool UsesExtendedCoordinates() =>
        Mgxc.Events.Children.OfType<UmgrModel.BeatEvent>().Any(b => b.Numerator == 0) ||
        Mgxc.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children)).Any(n =>
            n is UmgrModel.Slide { NoLine: true } or UmgrModel.SlideJoint { NoLine: true } ||
            n.Lane < 0 || (long)n.Lane + n.Width > 16);

    private void RestoreBinarySnapshot(MemoryStream payload)
    {
        Mgxc.Extras.BinarySnapshotValid = Mgxc.Extras.BinaryContentKey ==
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload.ToArray()));
        if (!Mgxc.Extras.BinarySnapshotValid)
        {
            Mgxc.Extras.HasSpeedSnapshot = false;
            return;
        }

        foreach (var crash in Mgxc.Notes.Children.OfType<UmgrModel.AirCrash>()
                     .Where(crash => Mgxc.Extras.TraceCrashes.Contains(ChartExtras.CrashKey(crash))))
        {
            crash.Attr = Models.AirLadderAttr.Trace;
        }
    }

    private void PostProcessChart()
    {
        Mgxc.Extras.CheckEventView(Mgxc);
        var post = new ChartPostProcessor(Mgxc, Diagnostic, Assets);
        post.Run();
        Mgxc.Extras.RestoreUserText(Mgxc.Meta);
        if (Mgxc.Extras.BinarySnapshotValid)
        {
            Mgxc.Extras.RestoreAppearance(Mgxc);
        }

        EditorProjection.Restore(Mgxc);
        Mgxc.Extras.EditorFormatVersion = _version;
        Mgxc.Extras.SpeedModelKey = ChartExtras.SpeedKey(Mgxc);
        Mgxc.Extras.ParsedEventModelKey = C2SRoundTrip.ViewHash(ChartExtras.EventView(Mgxc).Split('\n'));
        Mgxc.Extras.AirModelKey = C2sRoundTripKeys.FormatAirEditKey(Mgxc);
        Mgxc.Extras.SlaModelKey = C2sRoundTripKeys.FormatSlaEditKey(Mgxc);
        ProcessMeta();
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
