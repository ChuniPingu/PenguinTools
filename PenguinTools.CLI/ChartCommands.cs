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
                CliJsonSerializerContext.Default.ChartInspectResult, cancellationToken));
        return command;
    }

    private static Command BuildConvertCommand()
    {
        var input = InputArgument();
        var output = new Argument<string>("output") { Description = "Path to the output .c2s or .ugc file." };
        var command = new Command("convert", "Convert MGXC/UGC/SUS to C2S, or C2S to UGC v8.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("chart.convert", (app, progress, ct) => app.ConvertChartAsync(
                    new ChartConvertRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_chart_written, value.OutputPath),
                CliJsonSerializerContext.Default.ChartConvertResult, cancellationToken));
        return command;
    }

    private static Argument<string> InputArgument()
    {
        return new Argument<string>("input") { Description = "Path to the source chart (.mgxc, .ugc, .sus, or .c2s)." };
    }
}
