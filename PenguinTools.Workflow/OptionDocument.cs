using System.Text.Json;
using System.Text.Json.Serialization;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Xml;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

public sealed class OptionDocument
{
    public string OptionName { get; set; } = "AXXX";

    public string OptionId
    {
        get => string.IsNullOrWhiteSpace(field) ? field = CreateOptionId() : field;
        set => field = string.IsNullOrWhiteSpace(value) ? CreateOptionId() : value.Trim();
    } = CreateOptionId();

    public bool ConvertChart { get; set; } = true;

    [JsonConverter(typeof(ChartFileDiscoveryJsonConverter))]
    public List<ChartFileFormat> ChartFileDiscovery { get; set; } =
        [ChartFileFormat.Mgxc, ChartFileFormat.Ugc];

    public bool ConvertAudio { get; set; } = true;

    public bool ConvertJacket { get; set; } = true;

    public bool ConvertBackground { get; set; } = true;

    public ulong HcaEncryptionKey { get; set; } = AudioConvertRequest.DefaultHcaEncryptionKey;

    public bool GenerateEventXml { get; set; } = true;

    public bool CustomReleaseTagXml { get; set; }

    public int SelectedReleaseTagId { get; set; } = ReleaseTag.CustomDefaultId;

    public int CustomReleaseTagId { get; set; } = ReleaseTag.CustomDefaultId;

    public string CustomReleaseTagTitleName
    {
        get => string.IsNullOrWhiteSpace(field) ? ReleaseTag.CustomDefaultTitleName : field;
        set => field = string.IsNullOrWhiteSpace(value)
            ? ReleaseTag.CustomDefaultTitleName
            : value.Trim();
    } = ReleaseTag.CustomDefaultTitleName;

    public bool CustomGenre { get; set; }

    public int SelectedGenreId { get; set; } = GenreDefaults.SelectedDefaultId;

    public int CustomGenreId { get; set; } = GenreDefaults.CustomDefaultId;

    public string CustomGenreName
    {
        get => string.IsNullOrWhiteSpace(field) ? GenreDefaults.CustomDefaultName : field;
        set => field = string.IsNullOrWhiteSpace(value)
            ? GenreDefaults.CustomDefaultName
            : value.Trim();
    } = GenreDefaults.CustomDefaultName;

    public bool OverrideChartGenre { get; set; } = true;

    public int UltimaEventId { get; set; } = 1000001;

    public int WeEventId { get; set; } = 1000002;

    public int BatchSize { get; set; } = 8;

    public OptionConversionCache ConversionCache { get; set; } = new();

    public bool HasExportableWork()
    {
        return ConvertChart || ConvertAudio || ConvertJacket || ConvertBackground || GenerateEventXml;
    }

    public OptionExportSettings ToExportSettings()
    {
        ConversionCache ??= new OptionConversionCache();

        return new OptionExportSettings(
            ConvertChart,
            ConvertJacket,
            ConvertAudio,
            ConvertBackground,
            CustomReleaseTagXml,
            SelectedReleaseTagId,
            CustomReleaseTagId,
            CustomReleaseTagTitleName,
            CustomGenre,
            SelectedGenreId,
            CustomGenreId,
            CustomGenreName,
            OverrideChartGenre,
            GenerateEventXml,
            UltimaEventId,
            WeEventId,
            BatchSize,
            ConversionCache,
            HcaEncryptionKey);
    }

    private static string CreateOptionId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

public static class OptionDocumentJson
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
