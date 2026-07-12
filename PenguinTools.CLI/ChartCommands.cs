using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class ChartCommands
{
    internal static Command BuildChartCommand()
    {
        var command = new Command("chart", "Chart parsing and conversion commands.");
        command.Subcommands.Add(BuildInspectCommand());
        command.Subcommands.Add(BuildConvertCommand());
        return command;
    }

    private static Command BuildInspectCommand()
    {
        var input = InputArgument();
        var command = new Command("inspect", "Inspect a chart and print its metadata.");
        command.Arguments.Add(input);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunAsync("chart.inspect", (app, ct) => app.InspectChartAsync(
                    new ChartInspectRequest(parseResult.GetRequiredValue(input)), ct),
                value => Msg.Create(MsgKeys.Cli_Msg_chart_inspect_complete, value.Chart.Title,
                    value.Chart.Difficulty, value.Chart.Level),
                CliJsonSerializerContext.Default.ChartInspectResult, cancellationToken, parseResult));
        return command;
    }

    private static Command BuildConvertCommand()
    {
        var input = InputArgument();
        var output = new Argument<string>("output") { Description = "Path to the output .c2s or .ugc file." };
        var songId = new Option<int?>("--song-id") { Description = "Override the chart song ID." };
        var designer = new Option<string?>("--designer") { Description = "Override the chart designer." };
        var difficulty = new Option<int?>("--difficulty-id") { Description = "Override the difficulty ID (0-5)." };
        var mainBpm = new Option<decimal?>("--display-bpm") { Description = "Override the displayed BPM." };
        var insertBlank = new Option<bool?>("--insert-blank-measure")
        {
            Description = "Enable insertion of a blank measure."
        };
        var noInsertBlank = new Option<bool>("--no-insert-blank-measure")
        {
            Description = "Disable insertion of a blank measure."
        };
        var command = new Command("convert", "Convert MGXC/UGC/SUS to C2S, or C2S to UGC v8.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Options.Add(songId);
        command.Options.Add(designer);
        command.Options.Add(difficulty);
        command.Options.Add(mainBpm);
        command.Options.Add(insertBlank);
        command.Options.Add(noInsertBlank);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("chart.convert", (app, progress, ct) => app.ConvertChartAsync(
                    new ChartConvertRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                        new ChartConvertOverrides(
                            parseResult.GetValue(songId), parseResult.GetValue(designer),
                            parseResult.GetValue(difficulty), parseResult.GetValue(mainBpm),
                            parseResult.GetValue(noInsertBlank)
                                ? false
                                : parseResult.GetValue(insertBlank))), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_chart_written, value.OutputPath),
                CliJsonSerializerContext.Default.ChartConvertResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress), parseResult));
        return command;
    }

    private static Argument<string> InputArgument()
    {
        return new Argument<string>("input") { Description = "Path to the source chart (.mgxc, .ugc, .sus, or .c2s)." };
    }
}
