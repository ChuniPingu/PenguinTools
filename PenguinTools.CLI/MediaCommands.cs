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
        var fileInput = new Argument<string>("input") { Description = "Input image file." };
        var fileOutput = new Argument<string>("output") { Description = "Output DDS file." };
        var convertFile = new Command("convert-file", "Convert an image directly to a jacket DDS.");
        convertFile.Arguments.Add(fileInput);
        convertFile.Arguments.Add(fileOutput);
        convertFile.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunAsync("jacket.convert-file", (app, ct) => app.ConvertJacketFileAsync(
                    new JacketFileConvertRequest(parseResult.GetRequiredValue(fileInput),
                        parseResult.GetRequiredValue(fileOutput)), ct),
                value => Msg.Create(MsgKeys.Cli_Msg_jacket_written, value.OutputPath),
                CliJsonSerializerContext.Default.JacketConvertResult, cancellationToken));
        root.Subcommands.Add(convertFile);
        return root;
    }

    private static Command BuildAudioExtractCommand()
    {
        var input = new Argument<string>("input") { Description = "Input ACB or AWB file." };
        var output = new Argument<string>("output") { Description = "Output directory for decoded WAV files." };
        var paired = new Option<string?>("--paired-input") { Description = "Explicit paired ACB/AWB path." };
        var key = new Option<ulong?>("--hca-key") { Description = "HCA decryption key override." };
        var command = new Command("extract", "Extract every CRI cue to PCM WAV.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Options.Add(paired);
        command.Options.Add(key);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("audio.extract", (app, progress, ct) => app.ExtractCriAudioAsync(
                    new CriAudioExtractRequest(parseResult.GetRequiredValue(input),
                        parseResult.GetRequiredValue(output), parseResult.GetValue(paired), parseResult.GetValue(key)),
                    progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_audio_exported, value.OutputDirectory),
                CliJsonSerializerContext.Default.CriAudioExtractResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress)));
        return command;
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
        root.Subcommands.Add(BuildAudioConvertFileCommand());
        root.Subcommands.Add(BuildAudioExtractCommand());
        return root;
    }

    private static Command BuildAudioConvertFileCommand()
    {
        var input = new Argument<string>("input") { Description = "Input WAV, OGG, or MP3 file." };
        var output = Output();
        var songId = new Option<int>("--song-id") { Description = "Song ID." };
        var previewStart = new Option<decimal>("--preview-start") { Description = "Preview start in seconds." };
        var previewStop = new Option<decimal>("--preview-stop") { Description = "Preview stop in seconds." };
        var manualOffset = new Option<decimal>("--manual-offset") { Description = "Manual audio offset in seconds." };
        var insertBlank = new Option<bool>("--insert-blank-measure")
        {
            Description = "Insert one blank measure before the song."
        };
        var initialBpm = new Option<decimal>("--initial-bpm")
        {
            Description = "Initial BPM.", DefaultValueFactory = _ => 120m
        };
        var numerator = new Option<int>("--initial-numerator")
        {
            Description = "Initial time-signature numerator.", DefaultValueFactory = _ => 4
        };
        var denominator = new Option<int>("--initial-denominator")
        {
            Description = "Initial time-signature denominator.", DefaultValueFactory = _ => 4
        };
        var key = new Option<ulong?>("--hca-key") { Description = "HCA encryption key." };
        var command = new Command("convert-file", "Convert an audio file using explicit song metadata.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Options.Add(songId);
        command.Options.Add(previewStart);
        command.Options.Add(previewStop);
        command.Options.Add(manualOffset);
        command.Options.Add(insertBlank);
        command.Options.Add(initialBpm);
        command.Options.Add(numerator);
        command.Options.Add(denominator);
        command.Options.Add(key);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunAsync("audio.convert-file", (app, ct) => app.ConvertAudioFileAsync(
                    new AudioFileConvertRequest(parseResult.GetRequiredValue(input),
                        parseResult.GetRequiredValue(output), new AudioFileSettings(
                            parseResult.GetValue(songId), parseResult.GetValue(previewStart),
                            parseResult.GetValue(previewStop), parseResult.GetValue(manualOffset),
                            parseResult.GetValue(insertBlank), parseResult.GetValue(initialBpm),
                            parseResult.GetValue(numerator), parseResult.GetValue(denominator),
                            parseResult.GetValue(key))), ct),
                value => Msg.Create(MsgKeys.Cli_Msg_audio_exported, value.OutputDirectory),
                CliJsonSerializerContext.Default.AudioConvertResult, cancellationToken));
        return command;
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
        var background = new Argument<string>("background") { Description = "Input stage background image." };
        var filesOutput = Output();
        var fileOptions = CommandLineOptions.CreateStageCommandOptions();
        var buildFiles = new Command("build-files", "Build a stage directly from image files and explicit metadata.");
        buildFiles.Arguments.Add(background);
        buildFiles.Arguments.Add(filesOutput);
        CommandLineOptions.AddStageCommandOptions(buildFiles, fileOptions, includeBackground: false);
        buildFiles.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunAsync("stage.build-files", (app, ct) => app.BuildStageFilesAsync(
                    new StageFilesBuildRequest(parseResult.GetRequiredValue(background),
                        parseResult.GetRequiredValue(filesOutput),
                        CommandLineOptions.GetStageFileOverrides(parseResult, fileOptions)), ct),
                value => Msg.Create(MsgKeys.Cli_Msg_built_stage, value.OutputDirectory),
                CliJsonSerializerContext.Default.StageBuildResult, cancellationToken));
        root.Subcommands.Add(buildFiles);
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
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("afb.extract", (app, progress, ct) => app.ExtractAfbAsync(
                    new AfbExtractRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_extracted_dds, value.OutputDirectory),
                CliJsonSerializerContext.Default.AfbExtractResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress)));
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
