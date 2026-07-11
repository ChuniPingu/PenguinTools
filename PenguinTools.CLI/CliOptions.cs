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

    internal static void AddStageCommandOptions(Command command, StageCommandOptions options)
    {
        command.Options.Add(options.BackgroundPath);
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
        return new StageOverrides(
            parseResult.GetValue(options.BackgroundPath),
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
}