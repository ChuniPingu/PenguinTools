using PenguinTools.Core;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

public sealed record OptionExportSettings(
    bool ConvertChart,
    bool ConvertJacket,
    bool ConvertAudio,
    bool ConvertBackground,
    bool CustomReleaseTagXml,
    int SelectedReleaseTagId,
    int CustomReleaseTagId,
    string CustomReleaseTagTitleName,
    bool CustomGenre,
    int SelectedGenreId,
    int CustomGenreId,
    string CustomGenreName,
    bool OverrideChartGenre,
    bool GenerateEventXml,
    int UltimaEventId,
    int WeEventId,
    int BatchSize,
    OptionConversionCache? ConversionCache = null,
    ulong HcaEncryptionKey = AudioConvertRequest.DefaultHcaEncryptionKey,
    bool IgnoreCache = false,
    bool ConvertWall = true)
{
    public bool InputComplete { get; init; } = true;
    public OptionChartConversionCache? ChartCache { get; init; }

    public void ValidateIds()
    {
        ResourceIds.Require(UltimaEventId, MsgKeys.Error_Event_id_invalid);
        ResourceIds.Require(WeEventId, MsgKeys.Error_Event_id_invalid);
    }
}
