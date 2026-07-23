using System.Text.Json.Serialization;
using PenguinTools.Core;
using PenguinTools.Core.Asset;

namespace PenguinTools.Application;

public sealed record PenguinToolsApplicationOptions(
    string? ExternalAssetsDirectory = null,
    string? UserAssetsPath = null);

public sealed record ApplicationArtifact(string Kind, string Path);

public sealed record ApplicationDiagnostic(
    string Severity,
    MessageDescriptor Message,
    string? Path = null,
    int? Line = null,
    int? Time = null);

public enum ChartFormat
{
    Mgxc,
    Ugc,
    Sus,
    C2s
}

public sealed record ApplicationEntry(int Id, string Name, string? Data = null)
{
    internal static ApplicationEntry From(Entry entry)
    {
        return new ApplicationEntry(entry.Id, entry.Str, string.IsNullOrWhiteSpace(entry.Data) ? null : entry.Data);
    }
}

public sealed record ChartSummary(
    string? MgxcId,
    int? SongId,
    string Title,
    string Artist,
    string Designer,
    string Difficulty,
    decimal Level,
    decimal MainBpm,
    string FilePath);

public sealed record ChartConversionMetadata(
    int DifficultyId,
    string Difficulty,
    string BgmFilePath,
    string FullBgmFilePath,
    decimal BgmPreviewStart,
    decimal BgmPreviewStop,
    decimal BgmManualOffset,
    bool BgmEnableBarOffset,
    decimal BgmInitialBpm,
    int BgmInitialNumerator,
    int BgmInitialDenominator,
    decimal BgmBarOffset,
    decimal BgmRealOffset,
    string JacketFilePath,
    string FullJacketFilePath,
    bool IsCustomStage,
    int? StageId,
    string BgiFilePath,
    string FullBgiFilePath,
    ApplicationEntry NotesFieldLine,
    ApplicationEntry Stage,
    ApplicationEntry Genre,
    ApplicationEntry WeTag,
    int WeDifficultyId,
    string WeDifficulty,
    string SortName,
    int? UnlockEventId,
    string ReleaseDate,
    int MainTil);

public sealed record ChartInspectRequest(string InputPath);

public sealed record ChartInspectResult(
    string InputPath,
    ChartSummary Chart,
    ChartConversionMetadata Metadata);

public sealed record ChartConvertOverrides(
    int? SongId = null,
    string? Designer = null,
    int? DifficultyId = null,
    decimal? MainBpm = null,
    bool? InsertBlankMeasure = null,
    bool DebugTil = false);

public sealed record ChartConvertRequest(
    string InputPath,
    string OutputPath,
    ChartConvertOverrides? Overrides = null);

public sealed record ChartConvertResult(
    string InputPath,
    string OutputPath,
    ChartFormat SourceFormat,
    ChartFormat TargetFormat,
    ChartSummary Chart,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record OptionScanRequest(
    string InputDirectory,
    IReadOnlyList<ChartFormat>? ChartFileDiscovery = null,
    int BatchSize = 8,
    string? WorkingDirectory = null,
    bool SaveConfig = false);

public sealed record OptionScanDifficulty(
    string Difficulty,
    string? MgxcId,
    int? SongId,
    string Title,
    string Artist,
    string Designer,
    decimal Level,
    decimal MainBpm,
    int MainTil,
    bool IsMain,
    string FilePath,
    ApplicationEntry WeTag,
    int WeDifficultyId,
    string WeDifficulty,
    string SortName,
    ApplicationEntry Genre,
    int? UnlockEventId,
    string ReleaseDate,
    string JacketFilePath,
    string BgmFilePath,
    decimal BgmPreviewStart,
    decimal BgmPreviewStop,
    decimal BgmManualOffset,
    decimal BgmRealOffset,
    bool BgmEnableBarOffset,
    decimal BgmInitialBpm,
    int BgmInitialNumerator,
    int BgmInitialDenominator,
    bool IsCustomStage,
    int? StageId,
    string BgiFilePath,
    ApplicationEntry NotesFieldLine,
    ApplicationEntry Stage,
    IReadOnlyList<ApplicationDiagnostic> Diagnostics);

public sealed record OptionScanBook(
    int? SongId,
    string Title,
    string Artist,
    string? MainDifficulty,
    string SortName,
    ApplicationEntry Genre,
    int? UnlockEventId,
    string ReleaseDate,
    bool IsCustomStage,
    int? StageId,
    string BgiFilePath,
    ApplicationEntry NotesFieldLine,
    ApplicationEntry Stage,
    ApplicationEntry WeTag,
    int WeDifficultyId,
    string WeDifficulty,
    string JacketFilePath,
    string BgmFilePath,
    decimal BgmPreviewStart,
    decimal BgmPreviewStop,
    decimal BgmManualOffset,
    decimal BgmRealOffset,
    bool BgmEnableBarOffset,
    decimal BgmInitialBpm,
    int BgmInitialNumerator,
    int BgmInitialDenominator,
    IReadOnlyList<OptionScanDifficulty> Charts);

public sealed record OptionMainDifficultyOverride(int SongId, string MainDifficulty);

public sealed record OptionScanConfig(
    string OptionName,
    string OptionId,
    bool ConvertChart,
    IReadOnlyList<string> ChartFileDiscovery,
    bool ConvertAudio,
    bool ConvertJacket,
    bool ConvertBackground,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    ulong HcaEncryptionKey,
    bool GenerateEventXml,
    bool GenerateReleaseTagXml,
    int ReleaseTagId,
    string ReleaseTagTitleName,
    int UltimaEventId,
    int WeEventId,
    int BatchSize);

public sealed record OptionScanResult(
    string InputDirectory,
    IReadOnlyList<string> ChartFileDiscovery,
    int BatchSize,
    IReadOnlyList<OptionScanBook> Books,
    IReadOnlyList<ApplicationDiagnostic> UnmatchedDiagnostics,
    string? ConfigPath = null,
    OptionScanConfig? Config = null);

public sealed record OptionBuildOverrides(
    string? OptionName = null,
    IReadOnlyList<ChartFormat>? ChartFileDiscovery = null,
    int? BatchSize = null,
    bool? ConvertChart = null,
    bool? ConvertAudio = null,
    bool? ConvertJacket = null,
    bool? ConvertBackground = null,
    ulong? HcaEncryptionKey = null,
    bool? GenerateEventXml = null,
    bool? GenerateReleaseTagXml = null,
    int? ReleaseTagId = null,
    string? ReleaseTagTitleName = null,
    int? UltimaEventId = null,
    int? WeEventId = null,
    IReadOnlyList<OptionMainDifficultyOverride>? MainDifficulties = null);

public sealed record OptionBuildRequest(
    string InputDirectory,
    string OutputDirectory,
    string? ConfigPath = null,
    bool SkipConfig = false,
    bool SaveConfig = false,
    OptionBuildOverrides? Overrides = null,
    bool IgnoreCache = false);

public sealed record OptionBuildResult(
    string InputDirectory,
    string OutputDirectory,
    string? ConfigPath,
    string OptionName,
    int BookCount,
    int ChartCount,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record AudioOverrides(
    string? WorkingAudioPath = null,
    ulong? HcaEncryptionKey = null);

public sealed record StageOverrides(
    string? BackgroundPath = null,
    IReadOnlyList<string?>? EffectPaths = null,
    int? StageId = null,
    int? NoteFieldLaneId = null,
    string? NoteFieldLaneName = null,
    string? NoteFieldLaneData = null);

public sealed record MusicBuildOverrides(
    int? SongId = null,
    string? Title = null,
    string? Artist = null,
    string? Designer = null,
    int? DifficultyId = null,
    decimal? Level = null,
    decimal? MainBpm = null,
    bool? InsertBlankMeasure = null,
    int? GenreId = null,
    string? GenreName = null,
    int? WeTagId = null,
    string? WeTagName = null,
    int? WeDifficultyId = null,
    bool? IsCustomStage = null,
    int? StageId = null,
    int? NotesFieldLineId = null,
    string? NotesFieldLineName = null,
    string? NotesFieldLineData = null,
    int? StageEntryId = null,
    string? StageEntryName = null,
    decimal? BgmPreviewStart = null,
    decimal? BgmPreviewStop = null,
    decimal? BgmManualOffset = null,
    decimal? BgmInitialBpm = null,
    int? BgmInitialNumerator = null,
    int? BgmInitialDenominator = null,
    string? SortName = null,
    int? UnlockEventId = null,
    string? ReleaseDate = null,
    int? MainTil = null);

public sealed record MusicBuildRequest(
    string InputPath,
    string OutputDirectory,
    string? JacketInputPath = null,
    AudioOverrides? Audio = null,
    StageOverrides? Stage = null,
    MusicBuildOverrides? Overrides = null);

public sealed record MusicBuildResult(
    string InputPath,
    string OutputDirectory,
    ChartSummary Chart,
    int? StageId,
    string? StageName,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record JacketConvertRequest(string InputPath, string OutputPath, string? JacketInputPath = null);

public sealed record JacketFileConvertRequest(string InputPath, string OutputPath);

public sealed record JacketConvertResult(
    string InputPath,
    string SourcePath,
    string OutputPath,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record AudioConvertRequest(string InputPath, string OutputDirectory, AudioOverrides? Overrides = null);

public sealed record AudioFileSettings(
    int SongId,
    decimal PreviewStart,
    decimal PreviewStop,
    decimal ManualOffset,
    bool InsertBlankMeasure,
    decimal InitialBpm,
    int InitialNumerator,
    int InitialDenominator,
    ulong? HcaEncryptionKey = null);

public sealed record AudioFileConvertRequest(
    string InputPath,
    string OutputDirectory,
    AudioFileSettings Settings);

public sealed record AudioConvertResult(
    string InputPath,
    string SourcePath,
    string OutputDirectory,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record CriAudioExtractRequest(
    string SourcePath,
    string OutputDirectory,
    string? PairedPath = null,
    ulong? HcaKey = null);

public sealed record CriCueSummary(
    int CueId,
    string? Name,
    string WavPath,
    ushort Channels,
    uint SampleRate,
    ushort BitsPerSample,
    uint SampleFrames,
    uint? PreviewStartMs = null,
    uint? PreviewStopMs = null);

public sealed record CriAudioExtractResult(
    string SourcePath,
    string OutputDirectory,
    IReadOnlyList<CriCueSummary> Cues,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record MusicExtractRequest(
    string MusicXmlPath,
    string OutputDirectory,
    string? JacketPath = null,
    string? AcbPath = null,
    string? AwbPath = null,
    bool NoAudio = false,
    bool NoJacket = false,
    ulong? HcaKey = null,
    bool DebugTil = false);

public sealed record MusicExtractChartSummary(
    int SongId,
    int DifficultyId,
    string Difficulty,
    decimal Level,
    string Designer,
    decimal Bpm,
    string OutputPath);

public sealed record MusicExtractResult(
    string MusicXmlPath,
    string OutputDirectory,
    int SongId,
    string Title,
    string Artist,
    IReadOnlyList<MusicExtractChartSummary> Charts,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record StageBuildRequest(string InputPath, string OutputDirectory, StageOverrides? Overrides = null);

public sealed record StageFilesBuildRequest(
    string BackgroundPath,
    string OutputDirectory,
    StageOverrides Overrides);

public sealed record StageBuildResult(
    string InputPath,
    string? SourcePath,
    string OutputDirectory,
    int? StageId,
    string? StageName,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record AfbExtractRequest(string InputPath, string OutputDirectory);

public sealed record AfbExtractResult(
    string InputPath,
    string OutputDirectory,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record AssetCollectRequest(string GameRoot, string OutputPath);

public sealed record AssetCollectResult(
    string GameRoot,
    string OutputPath,
    IReadOnlyList<ApplicationArtifact> Artifacts);

public sealed record ApplicationInfoRequest;

public sealed record ApplicationInfo(
    string ApplicationName,
    string Version,
    DateTime? BuildDateUtc,
    string BaseDirectory,
    string TempWorkPath,
    string InfrastructureAssetsPath);

public interface IPenguinToolsApplication : IDisposable
{
    Task<OperationResult<ChartInspectResult>> InspectChartAsync(ChartInspectRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<ChartConvertResult>> ConvertChartAsync(ChartConvertRequest request,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<OptionScanResult>> ScanOptionAsync(OptionScanRequest request,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<OptionBuildResult>> BuildOptionAsync(OptionBuildRequest request,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<MusicBuildResult>> BuildMusicAsync(MusicBuildRequest request,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<JacketConvertResult>> ConvertJacketAsync(JacketConvertRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<JacketConvertResult>> ConvertJacketFileAsync(JacketFileConvertRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AudioConvertResult>> ConvertAudioAsync(AudioConvertRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AudioConvertResult>> ConvertAudioFileAsync(AudioFileConvertRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<CriAudioExtractResult>> ExtractCriAudioAsync(CriAudioExtractRequest request,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<MusicExtractResult>> ExtractMusicAsync(MusicExtractRequest request,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<StageBuildResult>> BuildStageAsync(StageBuildRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<StageBuildResult>> BuildStageFilesAsync(StageFilesBuildRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AfbExtractResult>> ExtractAfbAsync(AfbExtractRequest request,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<AssetCollectResult>> CollectAssetsAsync(AssetCollectRequest request,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<ApplicationInfo>> GetInfoAsync(ApplicationInfoRequest request,
        CancellationToken cancellationToken = default);
}
