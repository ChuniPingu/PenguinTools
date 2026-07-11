using System.CommandLine;
using PenguinTools.Application;

namespace PenguinTools.CLI;

internal static class MediaCommands
{
    internal static Command BuildJacketCommand()
    {
        var root = new Command("jacket", "Jacket operations.");
        var input = InputChart();
        var output = Output();
        var source = new Option<string?>("--jacket-input") { Description = "Override jacket source." };
        var command = new Command("convert", "Convert a jacket to DDS.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Options.Add(source);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunAsync("jacket.convert", (app, ct) => app.ConvertJacketAsync(
                    new JacketConvertRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                        parseResult.GetValue(source)), ct),
                value => Msg.Create(MsgKeys.Cli_Msg_jacket_written, value.OutputPath),
                CliJsonSerializerContext.Default.JacketConvertResult, cancellationToken));
        root.Subcommands.Add(command);
        return root;
    }

    internal static Command BuildAudioCommand()
    {
        var root = new Command("audio", "Audio operations.");
        var input = InputChart();
        var output = Output();
        var options = CommandLineOptions.CreateAudioCommandOptions();
        var command = new Command("convert", "Convert chart audio.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        CommandLineOptions.AddAudioCommandOptions(command, options);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunAsync("audio.convert", (app, ct) => app.ConvertAudioAsync(
                    new AudioConvertRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                        CommandLineOptions.GetAudioOverrides(parseResult, options)), ct),
                value => Msg.Create(MsgKeys.Cli_Msg_audio_exported, value.OutputDirectory),
                CliJsonSerializerContext.Default.AudioConvertResult, cancellationToken));
        root.Subcommands.Add(command);
        return root;
    }

    internal static Command BuildStageCommand()
    {
        var root = new Command("stage", "Stage operations.");
        var input = InputChart();
        var output = Output();
        var options = CommandLineOptions.CreateStageCommandOptions();
        var command = new Command("build", "Build stage assets.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        CommandLineOptions.AddStageCommandOptions(command, options);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunAsync("stage.build", (app, ct) => app.BuildStageAsync(new StageBuildRequest(
                    parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                    CommandLineOptions.GetStageOverrides(parseResult, options)), ct),
                value => Msg.Create(MsgKeys.Cli_Msg_built_stage, value.OutputDirectory),
                CliJsonSerializerContext.Default.StageBuildResult, cancellationToken));
        root.Subcommands.Add(command);
        return root;
    }

    internal static Command BuildAfbCommand()
    {
        var root = new Command("afb", "AFB operations.");
        var input = new Argument<string>("input") { Description = "Input AFB file." };
        var output = Output();
        var command = new Command("extract", "Extract DDS textures from an AFB file.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("afb.extract", (app, progress, ct) => app.ExtractAfbAsync(
                    new AfbExtractRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_extracted_dds, value.OutputDirectory),
                CliJsonSerializerContext.Default.AfbExtractResult, cancellationToken));
        root.Subcommands.Add(command);
        return root;
    }

    private static Argument<string> InputChart()
    {
        return new Argument<string>("input") { Description = "Input chart file." };
    }

    private static Argument<string> Output()
    {
        return new Argument<string>("output") { Description = "Output file or directory." };
    }
}