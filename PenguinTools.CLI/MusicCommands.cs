using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class MusicCommands
{
    internal static Command BuildMusicCommand()
    {
        var command = new Command("music", "Export an MGXC, UGC, or SUS chart with jacket and audio.");
        command.Subcommands.Add(BuildMusicBuildCommand());
        command.Subcommands.Add(BuildMusicExtractCommand());
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
        var meta = CommandLineOptions.CreateMusicBuildMetaOptions();
        var command = new Command("build", "Build a complete music bundle.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Options.Add(jacket);
        CommandLineOptions.AddAudioCommandOptions(command, audio);
        CommandLineOptions.AddStageCommandOptions(command, stage);
        CommandLineOptions.AddMusicBuildMetaOptions(command, meta);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("music.build", (app, progress, ct) => app.BuildMusicAsync(
                    new MusicBuildRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                        parseResult.GetValue(jacket), CommandLineOptions.GetAudioOverrides(parseResult, audio),
                        CommandLineOptions.GetStageOverrides(parseResult, stage),
                        CommandLineOptions.GetMusicBuildOverrides(parseResult, meta, stage)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_exported_music, value.OutputDirectory),
                CliJsonSerializerContext.Default.MusicBuildResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress), parseResult));
        return command;
    }

    private static Command BuildMusicExtractCommand()
    {
        var input = new Argument<string>("input") { Description = "Path to Music.xml." };
        var output = new Argument<string>("output")
        {
            Description = "Parent output folder; UGC files are written under a song-ID subfolder."
        };
        var jacket = new Option<string?>("--jacket") { Description = "Explicit jacket input." };
        var acb = new Option<string?>("--acb") { Description = "Explicit ACB input." };
        var awb = new Option<string?>("--awb") { Description = "Explicit AWB input." };
        var key = new Option<ulong?>("--hca-key") { Description = "HCA decryption key override." };
        var noAudio = new Option<bool>("--no-audio") { Description = "Allow output without audio." };
        var noJacket = new Option<bool>("--no-jacket") { Description = "Allow output without a jacket." };
        var command = new Command("extract", "Convert a game Music.xml bundle to playable UGC charts.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Options.Add(jacket);
        command.Options.Add(acb);
        command.Options.Add(awb);
        command.Options.Add(key);
        command.Options.Add(noAudio);
        command.Options.Add(noJacket);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("music.extract", (app, progress, ct) => app.ExtractMusicAsync(
                    new MusicExtractRequest(parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                        parseResult.GetValue(jacket), parseResult.GetValue(acb), parseResult.GetValue(awb),
                        parseResult.GetValue(noAudio), parseResult.GetValue(noJacket), parseResult.GetValue(key)),
                    progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_exported_music, value.OutputDirectory),
                CliJsonSerializerContext.Default.MusicExtractResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress), parseResult));
        return command;
    }
}
