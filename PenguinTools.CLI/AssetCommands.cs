using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class AssetCommands
{
    internal static Command BuildAssetCommand()
    {
        var root = new Command("assets", "Optional game asset operations.");
        var gameRoot = new Argument<string>("game-root") { Description = "Game installation root." };
        var command = new Command("collect", "Collect optional assets from a game installation.");
        command.Arguments.Add(gameRoot);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("assets.collect", (app, progress, ct) => app.CollectAssetsAsync(
                    new AssetCollectRequest(parseResult.GetRequiredValue(gameRoot)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_assets_collected, value.OutputPath),
                CliJsonSerializerContext.Default.AssetCollectResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress)));
        root.Subcommands.Add(command);
        var list = new Command("list", "List known stage and notes-field assets.");
        list.SetAction((_, cancellationToken) =>
            CliCommandRunner.RunAsync("assets.list", (app, ct) => app.GetAssetCatalogAsync(ct),
                _ => null, CliJsonSerializerContext.Default.AssetCatalogResult, cancellationToken));
        root.Subcommands.Add(list);
        return root;
    }
}
