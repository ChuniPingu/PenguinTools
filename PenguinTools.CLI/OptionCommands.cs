using System.CommandLine;
using PenguinTools.Application;

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
        var discovery = CommandLineOptions.CreateChartFileDiscoveryOption("Ordered chart formats to discover.");
        var batchSize = new Option<int>("--batch-size") { DefaultValueFactory = _ => 8 };
        var command = new Command("scan", "Scan a directory and report chart metadata and diagnostics.");
        command.Arguments.Add(input);
        command.Options.Add(discovery);
        command.Options.Add(batchSize);
        var noProgress = GlobalCliOptions.AddNoProgressOption(command);
        command.SetAction((parseResult, cancellationToken) =>
            CliCommandRunner.RunWithProgressAsync("option.scan", (app, progress, ct) => app.ScanOptionAsync(
                    new OptionScanRequest(
                        parseResult.GetRequiredValue(input), ParseDiscovery(parseResult, discovery),
                        parseResult.GetValue(batchSize)), progress, ct),
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
                        options.CreateOverrides(parseResult)), progress, ct),
                value => Msg.Create(MsgKeys.Cli_Msg_option_build_complete, value.OptionName, value.OutputDirectory),
                CliJsonSerializerContext.Default.OptionBuildResult, cancellationToken,
                GlobalCliOptions.IsNoProgress(parseResult, noProgress), parseResult));
        return command;
    }

    private static IReadOnlyList<ChartFormat>? ParseDiscovery(ParseResult result, Option<string?> option)
    {
        if (result.GetValue(option) is not { Length: > 0 } text) return null;
        var trimmed = text.Trim().TrimStart('[').TrimEnd(']');
        var tokens = trimmed.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) throw new ArgumentException("At least one chart format is required.");
        List<ChartFormat> formats = [];
        foreach (var token in tokens)
        {
            var normalized = token.TrimStart('.');
            if (!Enum.TryParse<ChartFormat>(normalized, true, out var format))
                throw new ArgumentException($"Unsupported chart format: {token}");
            if (!formats.Contains(format)) formats.Add(format);
        }

        return formats;
    }

    private sealed class BuildOptions
    {
        internal Option<string?> Config { get; } = new("--config") { Description = "Explicit options JSON path." };

        internal Option<bool> NoConfig { get; } = new("--no-config")
            { Description = "Ignore auto-discovered options.json." };

        private Option<string?> OptionName { get; } = new("--option-name");

        private Option<string?> Discovery { get; } =
            CommandLineOptions.CreateChartFileDiscoveryOption("Ordered chart formats.");

        private Option<int?> BatchSize { get; } = new("--batch-size");
        private Option<bool?> ConvertChart { get; } = new("--convert-chart");
        private Option<bool?> ConvertAudio { get; } = new("--convert-audio");
        private Option<bool?> ConvertJacket { get; } = new("--convert-jacket");
        private Option<bool?> ConvertBackground { get; } = new("--convert-background");
        private Option<ulong?> HcaKey { get; } = new("--hca-key");
        private Option<bool?> GenerateEventXml { get; } = new("--generate-event-xml");
        private Option<bool?> GenerateReleaseTagXml { get; } = new("--generate-release-tag-xml");
        private Option<int?> ReleaseTagId { get; } = new("--release-tag-id");
        private Option<string?> ReleaseTagTitleName { get; } = new("--release-tag-title-name");
        private Option<int?> UltimaEventId { get; } = new("--ultima-event-id");
        private Option<int?> WeEventId { get; } = new("--we-event-id");

        internal void AddTo(Command command)
        {
            command.Options.Add(Config);
            command.Options.Add(NoConfig);
            command.Options.Add(OptionName);
            command.Options.Add(Discovery);
            command.Options.Add(BatchSize);
            command.Options.Add(ConvertChart);
            command.Options.Add(ConvertAudio);
            command.Options.Add(ConvertJacket);
            command.Options.Add(ConvertBackground);
            command.Options.Add(HcaKey);
            command.Options.Add(GenerateEventXml);
            command.Options.Add(GenerateReleaseTagXml);
            command.Options.Add(ReleaseTagId);
            command.Options.Add(ReleaseTagTitleName);
            command.Options.Add(UltimaEventId);
            command.Options.Add(WeEventId);
        }

        internal OptionBuildOverrides CreateOverrides(ParseResult result)
        {
            return new OptionBuildOverrides(
                result.GetValue(OptionName), ParseDiscovery(result, Discovery), result.GetValue(BatchSize),
                result.GetValue(ConvertChart), result.GetValue(ConvertAudio), result.GetValue(ConvertJacket),
                result.GetValue(ConvertBackground), result.GetValue(HcaKey), result.GetValue(GenerateEventXml),
                result.GetValue(GenerateReleaseTagXml), result.GetValue(ReleaseTagId),
                result.GetValue(ReleaseTagTitleName), result.GetValue(UltimaEventId), result.GetValue(WeEventId));
        }
    }
}