using PenguinTools.Core.Asset;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Application;

internal static class ChartMetadata
{
    internal static ChartSummary CreateChartSummary(Meta meta)
    {
        return new ChartSummary(
            meta.MgxcId, meta.Id, meta.Title, meta.Artist, meta.Designer, meta.Difficulty.ToString(), meta.Level,
            meta.MainBpm, meta.FilePath);
    }

    internal static ChartConversionMetadata CreateChartConversionMetadata(Meta meta)
    {
        return new ChartConversionMetadata(
            (int)meta.Difficulty,
            meta.Difficulty.ToString(),
            meta.BgmFilePath,
            meta.FullBgmFilePath,
            meta.BgmPreviewStart,
            meta.BgmPreviewStop,
            meta.BgmManualOffset,
            meta.BgmEnableBarOffset,
            meta.BgmInitialBpm,
            meta.BgmInitialNumerator,
            meta.BgmInitialDenominator,
            meta.BgmBarOffset,
            meta.BgmRealOffset,
            meta.JacketFilePath,
            meta.FullJacketFilePath,
            meta.IsCustomStage,
            meta.StageId,
            meta.BgiFilePath,
            meta.FullBgiFilePath,
            ApplicationEntry.From(meta.NotesFieldLine),
            ApplicationEntry.From(meta.Stage),
            ApplicationEntry.From(meta.Genre),
            ApplicationEntry.From(meta.WeTag),
            (int)meta.WeDifficulty,
            GetStarDifficultyLabel(meta.WeDifficulty),
            meta.SortName,
            meta.UnlockEventId,
            meta.ReleaseDate.ToString("yyyy-MM-dd"),
            meta.MainTil);
    }

    internal static string GetStarDifficultyLabel(StarDifficulty value)
    {
        return value switch
        {
            StarDifficulty.S1 => "⭐",
            StarDifficulty.S2 => "⭐⭐",
            StarDifficulty.S3 => "⭐⭐⭐",
            StarDifficulty.S4 => "⭐⭐⭐⭐",
            StarDifficulty.S5 => "⭐⭐⭐⭐⭐",
            _ => "N/A"
        };
    }

    internal static void ApplyChartOverrides(Meta meta, ChartConvertOverrides? overrides)
    {
        ApplyMusicBuildOverrides(meta, overrides is null
            ? null
            : new MusicBuildOverrides(
                overrides.SongId,
                Designer: overrides.Designer,
                DifficultyId: overrides.DifficultyId,
                MainBpm: overrides.MainBpm,
                InsertBlankMeasure: overrides.InsertBlankMeasure));
    }

    internal static void ApplyMusicBuildOverrides(Meta meta, MusicBuildOverrides? overrides)
    {
        if (overrides is null) return;
        if (overrides.SongId is { } songId) meta.Id = songId;
        if (overrides.Title is not null) meta.Title = overrides.Title;
        if (overrides.Artist is not null) meta.Artist = overrides.Artist;
        if (overrides.Designer is not null) meta.Designer = overrides.Designer;
        if (overrides.DifficultyId is { } difficultyId)
        {
            if (!Enum.IsDefined(typeof(Difficulty), difficultyId))
                throw new ArgumentOutOfRangeException(nameof(overrides), difficultyId, "Unknown difficulty ID.");
            meta.Difficulty = (Difficulty)difficultyId;
        }
        if (overrides.Level is { } level) meta.Level = level;
        if (overrides.MainBpm is { } mainBpm) meta.MainBpm = mainBpm;
        if (overrides.InsertBlankMeasure is { } insertBlankMeasure)
            meta.BgmEnableBarOffset = insertBlankMeasure;
        if (overrides.GenreId is not null || overrides.GenreName is not null)
            meta.Genre = new Entry(
                overrides.GenreId ?? meta.Genre?.Id ?? GenreDefaults.CustomDefaultId,
                overrides.GenreName ?? meta.Genre?.Str ?? GenreDefaults.CustomDefaultName);
        if (overrides.WeTagId is not null || overrides.WeTagName is not null)
            meta.WeTag = new Entry(overrides.WeTagId ?? meta.WeTag.Id, overrides.WeTagName ?? meta.WeTag.Str);
        if (overrides.WeDifficultyId is { } weDifficultyId &&
            Enum.IsDefined(typeof(StarDifficulty), weDifficultyId))
            meta.WeDifficulty = (StarDifficulty)weDifficultyId;
        if (overrides.IsCustomStage is { } isCustomStage) meta.IsCustomStage = isCustomStage;
        if (overrides.StageId is { } stageId) meta.StageId = stageId;
        if (overrides.NotesFieldLineId is not null || overrides.NotesFieldLineName is not null ||
            overrides.NotesFieldLineData is not null)
            meta.NotesFieldLine = new Entry(
                overrides.NotesFieldLineId ?? meta.NotesFieldLine.Id,
                overrides.NotesFieldLineName ?? meta.NotesFieldLine.Str,
                overrides.NotesFieldLineData ?? meta.NotesFieldLine.Data);
        if (overrides.StageEntryId is not null || overrides.StageEntryName is not null)
            meta.Stage = new Entry(overrides.StageEntryId ?? meta.Stage.Id, overrides.StageEntryName ?? meta.Stage.Str);
        if (overrides.BgmPreviewStart is { } previewStart) meta.BgmPreviewStart = previewStart;
        if (overrides.BgmPreviewStop is { } previewStop) meta.BgmPreviewStop = previewStop;
        if (overrides.BgmManualOffset is { } manualOffset) meta.BgmManualOffset = manualOffset;
        if (overrides.BgmInitialBpm is { } initialBpm) meta.BgmInitialBpm = initialBpm;
        if (overrides.BgmInitialNumerator is { } numerator) meta.BgmInitialNumerator = numerator;
        if (overrides.BgmInitialDenominator is { } denominator) meta.BgmInitialDenominator = denominator;
        if (overrides.SortName is not null) meta.SortName = overrides.SortName;
        if (overrides.UnlockEventId is { } unlockEventId) meta.UnlockEventId = unlockEventId;
        if (overrides.ReleaseDate is { } releaseDate &&
            DateTime.TryParse(releaseDate, out var parsedReleaseDate))
            meta.ReleaseDate = parsedReleaseDate;
        if (overrides.MainTil is { } mainTil) meta.MainTil = mainTil;
    }
}
