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
                _ => null,
                CliJsonSerializerContext.Default.ApplicationInfo, cancellationToken, parseResult));
        return command;
    }
}