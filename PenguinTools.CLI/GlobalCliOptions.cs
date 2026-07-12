using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class GlobalCliOptions
{
    internal static readonly Option<string?> UserAssets = new("--user-assets")
    {
        Description = "Optional path to collected user assets JSON (plus tier). When omitted, only hardened assets.json is used.",
        Recursive = true
    };

    internal static readonly Option<bool> NoProgress = new("--no-progress")
    {
        Description = "Suppress progress events on stdout."
    };

    internal static void AddRootOptions(RootCommand rootCommand)
    {
        rootCommand.Options.Add(UserAssets);
    }

    internal static Option<bool> AddNoProgressOption(Command command)
    {
        command.Options.Add(NoProgress);
        return NoProgress;
    }

    internal static bool IsNoProgress(ParseResult parseResult, Option<bool> option)
    {
        return parseResult.GetValue(option);
    }

    internal static PenguinToolsApplicationOptions CreateApplicationOptions(ParseResult parseResult)
    {
        var userAssets = parseResult.GetValue(UserAssets);
        return new PenguinToolsApplicationOptions(
            UserAssetsPath: string.IsNullOrWhiteSpace(userAssets) ? null : userAssets);
    }
}
