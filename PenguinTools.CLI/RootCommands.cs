using System.CommandLine;

namespace PenguinTools.CLI;

internal static class RootCommands
{
    internal static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Command-line tools for chart conversion and asset export.");
        GlobalCliOptions.AddRootOptions(rootCommand);
        rootCommand.Subcommands.Add(ChartCommands.BuildChartCommand());
        rootCommand.Subcommands.Add(MusicCommands.BuildMusicCommand());
        rootCommand.Subcommands.Add(OptionCommands.BuildOptionCommand());
        rootCommand.Subcommands.Add(MediaCommands.BuildJacketCommand());
        rootCommand.Subcommands.Add(MediaCommands.BuildAudioCommand());
        rootCommand.Subcommands.Add(MediaCommands.BuildStageCommand());
        rootCommand.Subcommands.Add(WallCommands.BuildWallCommand());
        rootCommand.Subcommands.Add(AssetCommands.BuildAssetCommand());
        rootCommand.Subcommands.Add(InfoCommands.BuildInfoCommand());
        rootCommand.Subcommands.Add(SessionCommands.BuildSessionCommand());
        return rootCommand;
    }
}
