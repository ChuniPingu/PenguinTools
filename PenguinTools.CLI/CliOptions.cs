using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class CommandLineOptions
{
    internal static Option<string?> CreateChartFileDiscoveryOption(string description)
    {
        return new Option<string?>("--chart-file-discovery")
        {
            Description = description
        };
    }

    internal static AudioCommandOptions CreateAudioCommandOptions()
    {
        return new AudioCommandOptions(
            new Option<string?>("--working-audio")
            {
                Description = "Override the intermediate WAV path used during audio conversion."
            },
            new Option<ulong?>("--hca-key")
            {
                Description = "Override the HCA encryption key."
            });
    }

    internal static void AddAudioCommandOptions(Command command, AudioCommandOptions options)
    {
        command.Options.Add(options.WorkingAudioPath);
        command.Options.Add(options.HcaEncryptionKey);
    }

    internal static AudioOverrides GetAudioOverrides(ParseResult parseResult, AudioCommandOptions options)
    {
        return new AudioOverrides(
            parseResult.GetValue(options.WorkingAudioPath),
            parseResult.GetValue(options.HcaEncryptionKey));
    }

    internal static StageCommandOptions CreateStageCommandOptions()
    {
        return new StageCommandOptions(
            new Option<string?>("--background")
            {
                Description = "Override the stage background image path."
            },
            new Option<string?>("--effect-1")
            {
                Description = "Optional first stage effect image path."
            },
            new Option<string?>("--effect-2")
            {
                Description = "Optional second stage effect image path."
            },
            new Option<string?>("--effect-3")
            {
                Description = "Optional third stage effect image path."
            },
            new Option<string?>("--effect-4")
            {
                Description = "Optional fourth stage effect image path."
            },
            new Option<int?>("--stage-id")
            {
                Description = "Override the custom stage ID."
            },
            new Option<int?>("--notes-field-line-id")
            {
                Description = "Override the notes field line entry ID."
            },
            new Option<string?>("--notes-field-line-name")
            {
                Description = "Override the notes field line entry name."
            },
            new Option<string?>("--notes-field-line-data")
            {
                Description = "Override the notes field line entry data value."
            });
    }

    internal static void AddStageCommandOptions(Command command, StageCommandOptions options,
        bool includeBackground = true)
    {
        if (includeBackground) command.Options.Add(options.BackgroundPath);
        command.Options.Add(options.Effect1Path);
        command.Options.Add(options.Effect2Path);
        command.Options.Add(options.Effect3Path);
        command.Options.Add(options.Effect4Path);
        command.Options.Add(options.StageId);
        command.Options.Add(options.NoteFieldLaneId);
        command.Options.Add(options.NoteFieldLaneName);
        command.Options.Add(options.NoteFieldLaneData);
    }

    internal static StageOverrides GetStageOverrides(ParseResult parseResult, StageCommandOptions options)
    {
        return GetStageOverrides(parseResult, options, true);
    }

    internal static StageOverrides GetStageFileOverrides(ParseResult parseResult, StageCommandOptions options)
    {
        return GetStageOverrides(parseResult, options, false);
    }

    private static StageOverrides GetStageOverrides(ParseResult parseResult, StageCommandOptions options,
        bool includeBackground)
    {
        return new StageOverrides(
            includeBackground ? parseResult.GetValue(options.BackgroundPath) : null,
            [
                parseResult.GetValue(options.Effect1Path),
                parseResult.GetValue(options.Effect2Path),
                parseResult.GetValue(options.Effect3Path),
                parseResult.GetValue(options.Effect4Path)
            ],
            parseResult.GetValue(options.StageId),
            parseResult.GetValue(options.NoteFieldLaneId),
            parseResult.GetValue(options.NoteFieldLaneName),
            parseResult.GetValue(options.NoteFieldLaneData));
    }

    internal sealed record AudioCommandOptions(
        Option<string?> WorkingAudioPath,
        Option<ulong?> HcaEncryptionKey);

    internal sealed record StageCommandOptions(
        Option<string?> BackgroundPath,
        Option<string?> Effect1Path,
        Option<string?> Effect2Path,
        Option<string?> Effect3Path,
        Option<string?> Effect4Path,
        Option<int?> StageId,
        Option<int?> NoteFieldLaneId,
        Option<string?> NoteFieldLaneName,
        Option<string?> NoteFieldLaneData);

    internal static MusicBuildMetaOptions CreateMusicBuildMetaOptions()
    {
        return new MusicBuildMetaOptions(
            new Option<int?>("--song-id") { Description = "Override the chart song ID." },
            new Option<string?>("--title") { Description = "Override the song title." },
            new Option<string?>("--artist") { Description = "Override the song artist." },
            new Option<string?>("--designer") { Description = "Override the chart designer." },
            new Option<int?>("--difficulty-id") { Description = "Override the difficulty ID (0-5)." },
            new Option<decimal?>("--level") { Description = "Override the chart level." },
            new Option<decimal?>("--display-bpm") { Description = "Override the displayed BPM." },
            new Option<bool?>("--insert-blank-measure")
            {
                Description = "Enable insertion of a blank measure."
            },
            new Option<bool>("--no-insert-blank-measure")
            {
                Description = "Disable insertion of a blank measure."
            },
            new Option<int?>("--genre-id") { Description = "Override the genre entry ID." },
            new Option<string?>("--genre-name") { Description = "Override the genre entry name." },
            new Option<int?>("--we-tag-id") { Description = "Override the World's End tag entry ID." },
            new Option<string?>("--we-tag-name") { Description = "Override the World's End tag entry name." },
            new Option<int?>("--we-difficulty-id") { Description = "Override the World's End star difficulty ID." },
            new Option<bool?>("--custom-stage") { Description = "Override whether a custom stage is used." },
            new Option<decimal?>("--preview-start") { Description = "Override the BGM preview start time." },
            new Option<decimal?>("--preview-stop") { Description = "Override the BGM preview stop time." },
            new Option<decimal?>("--manual-offset") { Description = "Override the BGM manual offset." },
            new Option<decimal?>("--initial-bpm") { Description = "Override the initial BPM used for sync." },
            new Option<int?>("--initial-numerator")
            {
                Description = "Override the initial time signature numerator."
            },
            new Option<int?>("--initial-denominator")
            {
                Description = "Override the initial time signature denominator."
            },
            new Option<int?>("--stage-entry-id") { Description = "Override the background stage entry ID." },
            new Option<string?>("--stage-entry-name") { Description = "Override the background stage entry name." },
            new Option<string?>("--sort-name") { Description = "Override the song sort name." },
            new Option<int?>("--unlock-event-id") { Description = "Override the unlock event ID." },
            new Option<string?>("--release-date") { Description = "Override the release date (yyyy-MM-dd)." },
            new Option<int?>("--main-til") { Description = "Override the main timeline ID." });
    }

    internal static void AddMusicBuildMetaOptions(Command command, MusicBuildMetaOptions options)
    {
        command.Options.Add(options.SongId);
        command.Options.Add(options.Title);
        command.Options.Add(options.Artist);
        command.Options.Add(options.Designer);
        command.Options.Add(options.DifficultyId);
        command.Options.Add(options.Level);
        command.Options.Add(options.MainBpm);
        command.Options.Add(options.InsertBlankMeasure);
        command.Options.Add(options.NoInsertBlankMeasure);
        command.Options.Add(options.GenreId);
        command.Options.Add(options.GenreName);
        command.Options.Add(options.WeTagId);
        command.Options.Add(options.WeTagName);
        command.Options.Add(options.WeDifficultyId);
        command.Options.Add(options.CustomStage);
        command.Options.Add(options.PreviewStart);
        command.Options.Add(options.PreviewStop);
        command.Options.Add(options.ManualOffset);
        command.Options.Add(options.InitialBpm);
        command.Options.Add(options.InitialNumerator);
        command.Options.Add(options.InitialDenominator);
        command.Options.Add(options.StageEntryId);
        command.Options.Add(options.StageEntryName);
        command.Options.Add(options.SortName);
        command.Options.Add(options.UnlockEventId);
        command.Options.Add(options.ReleaseDate);
        command.Options.Add(options.MainTil);
    }

    internal static MusicBuildOverrides GetMusicBuildOverrides(
        ParseResult parseResult,
        MusicBuildMetaOptions options,
        StageCommandOptions stageOptions)
    {
        return new MusicBuildOverrides(
            parseResult.GetValue(options.SongId),
            parseResult.GetValue(options.Title),
            parseResult.GetValue(options.Artist),
            parseResult.GetValue(options.Designer),
            parseResult.GetValue(options.DifficultyId),
            parseResult.GetValue(options.Level),
            parseResult.GetValue(options.MainBpm),
            parseResult.GetValue(options.NoInsertBlankMeasure)
                ? false
                : parseResult.GetValue(options.InsertBlankMeasure),
            parseResult.GetValue(options.GenreId),
            parseResult.GetValue(options.GenreName),
            parseResult.GetValue(options.WeTagId),
            parseResult.GetValue(options.WeTagName),
            parseResult.GetValue(options.WeDifficultyId),
            parseResult.GetValue(options.CustomStage),
            parseResult.GetValue(stageOptions.StageId),
            parseResult.GetValue(stageOptions.NoteFieldLaneId),
            parseResult.GetValue(stageOptions.NoteFieldLaneName),
            parseResult.GetValue(stageOptions.NoteFieldLaneData),
            parseResult.GetValue(options.StageEntryId),
            parseResult.GetValue(options.StageEntryName),
            parseResult.GetValue(options.PreviewStart),
            parseResult.GetValue(options.PreviewStop),
            parseResult.GetValue(options.ManualOffset),
            parseResult.GetValue(options.InitialBpm),
            parseResult.GetValue(options.InitialNumerator),
            parseResult.GetValue(options.InitialDenominator),
            parseResult.GetValue(options.SortName),
            parseResult.GetValue(options.UnlockEventId),
            parseResult.GetValue(options.ReleaseDate),
            parseResult.GetValue(options.MainTil));
    }

    internal sealed record MusicBuildMetaOptions(
        Option<int?> SongId,
        Option<string?> Title,
        Option<string?> Artist,
        Option<string?> Designer,
        Option<int?> DifficultyId,
        Option<decimal?> Level,
        Option<decimal?> MainBpm,
        Option<bool?> InsertBlankMeasure,
        Option<bool> NoInsertBlankMeasure,
        Option<int?> GenreId,
        Option<string?> GenreName,
        Option<int?> WeTagId,
        Option<string?> WeTagName,
        Option<int?> WeDifficultyId,
        Option<bool?> CustomStage,
        Option<decimal?> PreviewStart,
        Option<decimal?> PreviewStop,
        Option<decimal?> ManualOffset,
        Option<decimal?> InitialBpm,
        Option<int?> InitialNumerator,
        Option<int?> InitialDenominator,
        Option<int?> StageEntryId,
        Option<string?> StageEntryName,
        Option<string?> SortName,
        Option<int?> UnlockEventId,
        Option<string?> ReleaseDate,
        Option<int?> MainTil);
}
