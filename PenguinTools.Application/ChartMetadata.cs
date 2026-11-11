using System.Globalization;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
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
            meta.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            meta.MainTil)
        {
            WallFilePath = meta.WallFilePath,
            FullWallFilePath = meta.FullWallFilePath,
            WallId = meta.EffectiveWallId,
            ExplicitWallId = meta.WallId,
            WallWidth = meta.WallWidth
        };
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
        if (overrides is null)
        {
            return;
        }

        ResourceIds.ValidateSong(overrides.SongId);
        if (overrides.DifficultyId is { } difficulty && !Enum.IsDefined((Difficulty)difficulty))
        {
            throw new DiagnosticException(MsgKeys.Error_Difficulty_id_invalid, difficulty);
        }

        if (overrides.SongId is { } songId)
        {
            meta.Id = songId;
            meta.MgxcId = songId.ToString(CultureInfo.InvariantCulture);
        }

        if (overrides.Designer is not null)
        {
            meta.Designer = overrides.Designer;
        }

        if (overrides.DifficultyId is { } difficultyId)
        {
            meta.Difficulty = (Difficulty)difficultyId;
        }

        if (overrides.MainBpm is { } mainBpm)
        {
            meta.MainBpm = mainBpm;
        }

        if (overrides.InsertBlankMeasure is { } insertBlankMeasure)
        {
            meta.BgmEnableBarOffset = insertBlankMeasure;
        }
    }
}
