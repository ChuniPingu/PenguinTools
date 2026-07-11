using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class InfoCommands
{
    internal static Command BuildInfoCommand()
    {
        var command = new Command("info", "Show runtime paths and version.");
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunAsync("info",
                (app, ct) => app.GetInfoAsync(new ApplicationInfoRequest(), ct),
                _ => Msg.Key(MsgKeys.Cli_Msg_info_complete),
                CliJsonSerializerContext.Default.ApplicationInfo, cancellationToken));
        return command;
    }
}