using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class MusicCommands
{
    internal static Command BuildMusicCommand()
    {
        var command = new Command("music", "Export an MGXC, UGC, or SUS chart with jacket and audio.");
        command.Subcommands.Add(BuildMusicBuildCommand());
        return command;
    }

    private static Command BuildMusicBuildCommand()
    {
        var input = new Argument<string>("input") { Description = "Path to the source chart (.mgxc, .ugc, or .sus)." };
        var output = new Argument<string>("output")
            { Description = "Base folder for the exported music bundle files." };
        var jacket = new Option<string?>("--jacket-input")
            { Description = "Override the jacket source path used for export." };
        var audio = CommandLineOptions.CreateAudioCommandOptions();
        var stage = CommandLineOptions.CreateStageCommandOptions();
        var command = new Command("build", "Build a complete music bundle.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Options.Add(jacket);
        CommandLineOptions.AddAudioCommandOptions(command, audio);
        CommandLineOptions.AddStageCommandOptions(command, stage);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("music.build", (app, progress, ct) => app.BuildMusicAsync(
                    new MusicBuildRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                        parseResult.GetValue(jacket), CommandLineOptions.GetAudioOverrides(parseResult, audio),
                        CommandLineOptions.GetStageOverrides(parseResult, stage)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_exported_music, value.OutputDirectory),
                CliJsonSerializerContext.Default.MusicBuildResult, cancellationToken));
        return command;
    }
}