using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class MusicCommands
{
    internal static Command BuildMusicCommand()
    {
        var command = new Command("music", "Extract charts and media from a game music bundle.");
        command.Subcommands.Add(BuildMusicExtractCommand());
        return command;
    }

    private static Command BuildMusicExtractCommand()
    {
        var input = new Argument<string>("input") { Description = "Path to Music.xml." };
        var output = new Argument<string>("output")
        {
            Description = "Parent output folder; charts are written under a song-ID subfolder."
        };
        var jacket = new Option<string?>("--jacket") { Description = "Explicit jacket input." };
        var acb = new Option<string?>("--acb") { Description = "Explicit ACB input." };
        var awb = new Option<string?>("--awb") { Description = "Explicit AWB input." };
        var key = new Option<ulong?>("--hca-key") { Description = "HCA decryption key override." };
        var noAudio = new Option<bool>("--no-audio") { Description = "Allow output without audio." };
        var noJacket = new Option<bool>("--no-jacket") { Description = "Allow output without a jacket." };
        var debugTil = new Option<bool>("--debug-til")
        {
            Description =
                "Emit transparent height-0 AirCrush markers (TIL 0) for each original SLA region."
        };
        var chartFormat = new Option<ChartFormat>("--chart-format")
        {
            Description = "Output chart format.",
            DefaultValueFactory = _ => ChartFormat.Mgxc
        };
        chartFormat.AcceptOnlyFromAmong("mgxc", "ugc");
        var command = new Command("extract", "Extract MGXC or UGC charts and media from a game Music.xml bundle.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Options.Add(jacket);
        command.Options.Add(acb);
        command.Options.Add(awb);
        command.Options.Add(key);
        command.Options.Add(noAudio);
        command.Options.Add(noJacket);
        command.Options.Add(debugTil);
        command.Options.Add(chartFormat);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("music.extract", (app, progress, ct) => app.ExtractMusicAsync(
                    new MusicExtractRequest(parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                        parseResult.GetValue(jacket), parseResult.GetValue(acb), parseResult.GetValue(awb),
                        parseResult.GetValue(noAudio), parseResult.GetValue(noJacket), parseResult.GetValue(key),
                        parseResult.GetValue(debugTil), parseResult.GetValue(chartFormat)),
                    progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_exported_music, value.OutputDirectory),
                CliJsonSerializerContext.Default.MusicExtractResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress), parseResult));
        return command;
    }
}
