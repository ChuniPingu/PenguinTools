using System.CommandLine;
using PenguinTools.Application;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.CLI;

internal static class OptionCommands
{
    internal static Command BuildOptionCommand()
    {
        var command = new Command("option", "Scan charts and build option bundles.");
        command.Subcommands.Add(BuildScanCommand());
        command.Subcommands.Add(BuildBuildCommand());
        return command;
    }

    private static Command BuildScanCommand()
    {
        var input = new Argument<string>("input") { Description = "Input chart directory." };
        var discovery = CommandLineOptions.CreateChartFileDiscoveryOption();
        var batchSize = new Option<int>("--batch-size")
        {
            Description = "Maximum parallel operations; -1 uses automatic parallelism.",
            DefaultValueFactory = _ => 8
        };
        var saveConfig = new Option<bool>("--save-config")
        { Description = "Save the scan settings to options.json before scanning." };
        var command = new Command("scan", "Scan a directory and report chart metadata and diagnostics.");
        command.Arguments.Add(input);
        command.Options.Add(discovery);
        command.Options.Add(batchSize);
        command.Options.Add(saveConfig);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("option.scan", (app, progress, ct) => app.ScanOptionAsync(
                    new OptionScanRequest(
                        parseResult.GetRequiredValue(input), ParseDiscovery(parseResult, discovery),
                        parseResult.GetValue(batchSize), SaveConfig: parseResult.GetValue(saveConfig)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_option_scan_complete, value.Books.Count,
                    value.Books.Sum(x => x.Charts.Count)),
                CliJsonSerializerContext.Default.OptionScanResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress), parseResult));
        return command;
    }

    private static Command BuildBuildCommand()
    {
        var input = new Argument<string>("input") { Description = "Input chart directory." };
        var output = new Argument<string>("output") { Description = "Output directory." };
        var options = new BuildOptions();
        var command = new Command("build", "Build an option bundle.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        options.AddTo(command);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("option.build", (app, progress, ct) => app.BuildOptionAsync(
                    new OptionBuildRequest(
                        parseResult.GetRequiredValue(input), parseResult.GetRequiredValue(output),
                        parseResult.GetValue(options.Config), parseResult.GetValue(options.NoConfig),
                        parseResult.GetValue(options.SaveConfig), options.CreateOverrides(parseResult),
                        parseResult.GetValue(options.IgnoreCache)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_option_build_complete, value.OptionName, value.OutputDirectory),
                CliJsonSerializerContext.Default.OptionBuildResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress), parseResult));
        return command;
    }

    private static List<ChartFormat>? ParseDiscovery(ParseResult result, Option<string?> option)
    {
        if (result.GetValue(option) is not { Length: > 0 } text)
        {
            return null;
        }

        var trimmed = text.Trim().TrimStart('[').TrimEnd(']');
        var tokens = trimmed.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new DiagnosticException(MsgKeys.Error_Chart_format_specify_at_least_one);
        }

        List<ChartFormat> formats = [];
        foreach (var token in tokens)
        {
            var normalized = token.TrimStart('.');
            if (!Enum.TryParse<ChartFormat>(normalized, true, out var format) ||
                format is not (ChartFormat.Mgxc or ChartFormat.Ugc or ChartFormat.Sus))
            {
                throw new DiagnosticException(Msg.Create(MsgKeys.Error_Chart_format_unsupported, token));
            }

            if (!formats.Contains(format))
            {
                formats.Add(format);
            }
        }

        return formats;
    }

    private sealed class BuildOptions
    {
        internal Option<string?> Config { get; } = new("--config") { Description = "Explicit options JSON path." };

        internal Option<bool> NoConfig { get; } = new("--no-config")
        { Description = "Ignore auto-discovered options.json." };

        internal Option<bool> SaveConfig { get; } = new("--save-config")
        { Description = "Write options.json to the input directory after a successful build." };

        internal Option<bool> IgnoreCache { get; } = new("--ignore-cache")
        {
            Description =
                "Force reconversion of jacket, stage, and audio even when the conversion cache matches."
        };

        private Option<string?> OptionName { get; } = new("--option-name")
        { Description = "Four-character option name; required if not set in the config." };

        private Option<string?> Discovery { get; } =
            CommandLineOptions.CreateChartFileDiscoveryOption();

        private Option<int?> BatchSize { get; } = new("--batch-size")
        { Description = "Maximum parallel operations; -1 uses automatic parallelism." };
        private Option<bool?> ConvertChart { get; } = new("--convert-chart")
        { Description = "Convert charts to C2S." };
        private Option<bool?> ConvertAudio { get; } = new("--convert-audio")
        { Description = "Convert audio." };
        private Option<bool?> ConvertJacket { get; } = new("--convert-jacket")
        { Description = "Convert jackets." };
        private Option<bool?> ConvertBackground { get; } = new("--convert-background")
        { Description = "Build stage backgrounds." };
        private Option<ulong?> HcaKey { get; } = new("--hca-key")
        { Description = "HCA encryption key." };
        private Option<bool?> GenerateEventXml { get; } = new("--generate-event-xml")
        { Description = "Generate ULTIMA and WORLD'S END event XML." };
        private Option<bool?> CustomReleaseTagXml { get; } = new("--custom-release-tag-xml")
        { Description = "Generate a custom release tag XML." };
        private Option<int?> SelectedReleaseTagId { get; } = new("--selected-release-tag-id")
        { Description = "Existing release tag ID." };
        private Option<int?> CustomReleaseTagId { get; } = new("--custom-release-tag-id")
        { Description = "Custom release tag ID." };
        private Option<string?> CustomReleaseTagTitleName { get; } = new("--custom-release-tag-title-name")
        { Description = "Custom release tag title." };
        private Option<bool?> CustomGenre { get; } = new("--custom-genre")
        { Description = "Use a custom genre." };
        private Option<int?> SelectedGenreId { get; } = new("--selected-genre-id")
        { Description = "Existing genre ID." };
        private Option<int?> CustomGenreId { get; } = new("--custom-genre-id")
        { Description = "Custom genre ID." };
        private Option<string?> CustomGenreName { get; } = new("--custom-genre-name")
        { Description = "Custom genre name." };
        private Option<bool?> OverrideChartGenre { get; } = new("--override-chart-genre")
        { Description = "Use the option genre instead of chart genres." };
        private Option<int?> UltimaEventId { get; } = new("--ultima-event-id")
        { Description = "ULTIMA event ID." };
        private Option<int?> WeEventId { get; } = new("--we-event-id")
        { Description = "WORLD'S END event ID." };
        private Option<string[]?> MainDifficulties { get; } = new("--main-difficulty")
        {
            Description = "Set a song's main difficulty, for example 1234:Master.",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };

        internal void AddTo(Command command)
        {
            command.Options.Add(Config);
            command.Options.Add(NoConfig);
            command.Options.Add(SaveConfig);
            command.Options.Add(IgnoreCache);
            command.Options.Add(OptionName);
            command.Options.Add(Discovery);
            command.Options.Add(BatchSize);
            command.Options.Add(ConvertChart);
            command.Options.Add(ConvertAudio);
            command.Options.Add(ConvertJacket);
            command.Options.Add(ConvertBackground);
            command.Options.Add(HcaKey);
            command.Options.Add(GenerateEventXml);
            command.Options.Add(CustomReleaseTagXml);
            command.Options.Add(SelectedReleaseTagId);
            command.Options.Add(CustomReleaseTagId);
            command.Options.Add(CustomReleaseTagTitleName);
            command.Options.Add(CustomGenre);
            command.Options.Add(SelectedGenreId);
            command.Options.Add(CustomGenreId);
            command.Options.Add(CustomGenreName);
            command.Options.Add(OverrideChartGenre);
            command.Options.Add(UltimaEventId);
            command.Options.Add(WeEventId);
            command.Options.Add(MainDifficulties);
        }

        internal OptionBuildOverrides CreateOverrides(ParseResult result)
        {
            return new OptionBuildOverrides(
                result.GetValue(OptionName), ParseDiscovery(result, Discovery), result.GetValue(BatchSize),
                result.GetValue(ConvertChart), result.GetValue(ConvertAudio), result.GetValue(ConvertJacket),
                result.GetValue(ConvertBackground), result.GetValue(HcaKey), result.GetValue(GenerateEventXml),
                result.GetValue(CustomReleaseTagXml), result.GetValue(SelectedReleaseTagId),
                result.GetValue(CustomReleaseTagId), result.GetValue(CustomReleaseTagTitleName),
                result.GetValue(CustomGenre), result.GetValue(SelectedGenreId),
                result.GetValue(CustomGenreId), result.GetValue(CustomGenreName),
                result.GetValue(OverrideChartGenre),
                result.GetValue(UltimaEventId), result.GetValue(WeEventId),
                ParseMainDifficulties(result.GetValue(MainDifficulties)));
        }

        private static List<OptionMainDifficultyOverride>? ParseMainDifficulties(string[]? values)
        {
            if (values is not { Length: > 0 })
            {
                return null;
            }

            List<OptionMainDifficultyOverride> overrides = [];
            foreach (var value in values)
            {
                var separator = value.IndexOf(':');
                if (separator <= 0 || separator >= value.Length - 1)
                {
                    throw new DiagnosticException(Msg.Create(MsgKeys.Error_Main_difficulty_invalid, value));
                }

                if (!int.TryParse(value[..separator], out var songId))
                {
                    throw new DiagnosticException(Msg.Create(MsgKeys.Error_Main_difficulty_invalid, value));
                }

                overrides.Add(new OptionMainDifficultyOverride(songId, value[(separator + 1)..]));
            }

            return overrides;
        }
    }
}
