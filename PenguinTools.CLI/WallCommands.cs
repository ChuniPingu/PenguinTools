using System.CommandLine;
using PenguinTools.Application;
using PenguinTools.Core.Metadata;

namespace PenguinTools.CLI;

internal sealed class WallCommandOptions
{
    private Option<string?> Input { get; } = new("--wall-input")
    { Description = "Use this wall image." };
    private Option<int?> Id { get; } = new("--wall-id")
    { Description = "Wall ID (default: 1000000 + song ID)." };
    private Option<WallWidth?> Width { get; } = new("--wall-width")
    { Description = "Wall width: normal or wide." };

    internal void AddTo(Command command, bool includeInput = true)
    {
        if (includeInput)
        {
            command.Options.Add(Input);
        }
        Width.AcceptOnlyFromAmong("normal", "wide");
        command.Options.Add(Id);
        command.Options.Add(Width);
    }

    internal WallOverrides GetOverrides(ParseResult result) =>
        new(result.GetValue(Input), result.GetValue(Id), result.GetValue(Width));
}

internal static class WallCommands
{
    internal static Command BuildWallCommand()
    {
        var root = new Command("wall", "Export walls for songs.");
        var input = new Argument<string>("chart") { Description = "Input chart file." };
        var output = new Argument<string>("output") { Description = "Output folder." };
        var songId = new Option<int?>("--song-id") { Description = "Override the chart's song ID." };
        var options = new WallCommandOptions();
        var build = new Command("build", "Export the wall set in a chart.");
        build.Arguments.Add(input);
        build.Arguments.Add(output);
        build.Options.Add(songId);
        options.AddTo(build);
        build.SetAction((result, ct) => CliCommandRunner.RunAsync("wall.build",
            (app, token) => app.BuildWallAsync(new WallBuildRequest(result.GetRequiredValue(input),
                result.GetRequiredValue(output), options.GetOverrides(result), result.GetValue(songId)), token),
            value => Msg.Create(MsgKeys.Cli_Msg_built_wall, value.OutputDirectory),
            CliJsonSerializerContext.Default.WallBuildResult, ct, result));
        root.Subcommands.Add(build);

        var image = new Argument<string>("image") { Description = "Input wall image." };
        var filesOutput = new Argument<string>("output") { Description = "Output folder." };
        var filesSongId = new Option<int>("--song-id") { Description = "Song ID.", Required = true };
        var fileOptions = new WallCommandOptions();
        var files = new Command("build-files", "Export a wall from an image.");
        files.Arguments.Add(image);
        files.Arguments.Add(filesOutput);
        files.Options.Add(filesSongId);
        fileOptions.AddTo(files, includeInput: false);
        files.SetAction((result, ct) => CliCommandRunner.RunAsync("wall.build-files",
            (app, token) => app.BuildWallFilesAsync(new WallFilesBuildRequest(result.GetRequiredValue(image),
                result.GetRequiredValue(filesOutput), result.GetValue(filesSongId), fileOptions.GetOverrides(result)), token),
            value => Msg.Create(MsgKeys.Cli_Msg_built_wall, value.OutputDirectory),
            CliJsonSerializerContext.Default.WallBuildResult, ct, result));
        root.Subcommands.Add(files);
        return root;
    }
}
