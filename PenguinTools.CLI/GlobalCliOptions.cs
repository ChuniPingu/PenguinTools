using System.CommandLine;

namespace PenguinTools.CLI;

internal static class GlobalCliOptions
{
    internal static Option<bool> CreateNoProgressOption()
    {
        return new Option<bool>("--no-progress")
        {
            Description = "Suppress progress events on stdout."
        };
    }

    internal static Option<bool> AddNoProgressOption(Command command)
    {
        var option = CreateNoProgressOption();
        command.Options.Add(option);
        return option;
    }

    internal static bool IsNoProgress(ParseResult parseResult, Option<bool> option)
    {
        return parseResult.GetValue(option);
    }
}
