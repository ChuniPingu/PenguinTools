using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.sus;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Media;
using UmgrChart = PenguinTools.Chart.Models.umgr.Chart;
using static PenguinTools.Application.RequestPaths;

namespace PenguinTools.Application;

internal sealed class ChartOperations(AssetManager assets, IMediaTool mediaTool)
{
    internal async Task<OperationResult<ChartInspectResult>> InspectAsync(
        ChartInspectRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = FullPath(request.InputPath);
        var parsed = await ParseChartAsync(input, cancellationToken);
        return parsed.Succeeded && parsed.Value is { } chart
            ? OperationResult<ChartInspectResult>
                .Success(new ChartInspectResult(input, ChartMetadata.CreateChartSummary(chart.Meta),
                    ChartMetadata.CreateChartConversionMetadata(chart.Meta)))
                .WithDiagnostics(parsed.Diagnostics)
            : OperationResult<ChartInspectResult>.Failure().WithDiagnostics(parsed.Diagnostics);
    }

    internal async Task<OperationResult<ChartConvertResult>> ConvertAsync(
        ChartConvertRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = FullPath(request.InputPath);
        var output = FullPath(request.OutputPath);
        var sourceFormat = GetChartFormat(input);
        var targetFormat = GetChartFormat(output);
        var supported = sourceFormat == ChartFormat.C2s
            ? targetFormat == ChartFormat.Mgxc
            : sourceFormat is ChartFormat.Mgxc or ChartFormat.Ugc or ChartFormat.Sus &&
              targetFormat == ChartFormat.C2s;
        if (!supported)
            return ApplicationDiagnostics.Failure<ChartConvertResult>(
                Msg.Create(MsgKeys.Error_Chart_conversion_unsupported, $"{sourceFormat} -> {targetFormat}"));
        progress?.Report(new ProgressReport(Item: Path.GetFileName(input), Completed: 0, Total: 1));
        if (sourceFormat == ChartFormat.C2s)
        {
            var parsedC2s = await new C2SParser(new C2SParseRequest(input)).ParseAsync(cancellationToken);
            if (!parsedC2s.Succeeded)
                return OperationResult<ChartConvertResult>.Failure().WithDiagnostics(parsedC2s.Diagnostics);
            var c2s = parsedC2s.Value;
            ChartMetadata.ApplyChartOverrides(c2s.Meta, request.Overrides);
            progress?.Report(new ProgressReport(
                Item: Path.GetFileName(input),
                Label: string.IsNullOrWhiteSpace(c2s.Meta.Title) ? null : c2s.Meta.Title,
                Completed: 0,
                Total: 1));
            var convertedUmgr = new UgcChartConverter(new UgcConvertRequest(c2s, request.Overrides?.DebugTil ?? false)).Convert();
            if (!convertedUmgr.Succeeded)
                return OperationResult<ChartConvertResult>.Failure().WithDiagnostics(
                    parsedC2s.Diagnostics.Merge(convertedUmgr.Diagnostics));
            var writtenReverse = await new MgxcChartWriter(new MgxcWriteRequest(output, convertedUmgr.Value))
                .WriteAsync(cancellationToken);
            progress?.Report(new ProgressReport(
                Item: Path.GetFileName(input),
                Label: string.IsNullOrWhiteSpace(c2s.Meta.Title) ? null : c2s.Meta.Title,
                Completed: 1,
                Total: 1));
            var reverseValue = new ChartConvertResult(input, output, sourceFormat, targetFormat,
                ChartMetadata.CreateChartSummary(c2s.Meta), [new ApplicationArtifact("chart.mgxc", output)]);
            return ApplicationDiagnostics.Merge(reverseValue,
                parsedC2s.Diagnostics.Merge(convertedUmgr.Diagnostics), writtenReverse);
        }

        var parsed = await ParseChartAsync(input, cancellationToken);
        if (!parsed.Succeeded)
            return OperationResult<ChartConvertResult>.Failure().WithDiagnostics(parsed.Diagnostics);
        var chart = parsed.Value;

        ChartMetadata.ApplyChartOverrides(chart.Meta, request.Overrides);
        progress?.Report(new ProgressReport(
            Item: Path.GetFileName(input),
            Label: string.IsNullOrWhiteSpace(chart.Meta.Title) ? null : chart.Meta.Title,
            Completed: 0,
            Total: 1));

        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        if (!converted.Succeeded)
            return OperationResult<ChartConvertResult>.Failure()
                .WithDiagnostics(parsed.Diagnostics.Merge(converted.Diagnostics));

        EnsureParentDirectory(output);
        var written = await new C2SChartWriter(new C2SWriteRequest(output, converted.Value, chart.GetCalculator()))
            .WriteAsync(cancellationToken);
        progress?.Report(new ProgressReport(
            Item: Path.GetFileName(input),
            Label: string.IsNullOrWhiteSpace(chart.Meta.Title) ? null : chart.Meta.Title,
            Completed: 1,
            Total: 1));
        var value = new ChartConvertResult(input, output, sourceFormat, targetFormat, ChartMetadata.CreateChartSummary(chart.Meta),
            [new ApplicationArtifact("chart.c2s", output)]);
        return ApplicationDiagnostics.Merge(value, parsed.Diagnostics.Merge(converted.Diagnostics), written);
    }

    internal async Task<OperationResult<UmgrChart>> ParseChartAsync(string input, CancellationToken cancellationToken)
    {
        if (!File.Exists(input))
            return ApplicationDiagnostics.Failure<UmgrChart>(Msg.Key(MsgKeys.App_Chart_file_not_found), input);
        var extension = Path.GetExtension(input);
        if (extension.Equals(".ugc", StringComparison.OrdinalIgnoreCase))
            return await new UgcParser(new UgcParseRequest(input, assets), mediaTool)
                .ParseAsync(cancellationToken);
        if (extension.Equals(".mgxc", StringComparison.OrdinalIgnoreCase))
            return await new MgxcParser(new MgxcParseRequest(input, assets), mediaTool)
                .ParseAsync(cancellationToken);
        if (extension.Equals(".sus", StringComparison.OrdinalIgnoreCase))
            return await new SusParser(new SusParseRequest(input, assets), mediaTool)
                .ParseAsync(cancellationToken);
        if (extension.Equals(".c2s", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = await new C2SParser(new C2SParseRequest(input)).ParseAsync(cancellationToken);
            if (!parsed.Succeeded)
                return OperationResult<UmgrChart>.Failure().WithDiagnostics(parsed.Diagnostics);
            var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
            return converted.WithDiagnostics(parsed.Diagnostics.Merge(converted.Diagnostics));
        }
        return ApplicationDiagnostics.Failure<UmgrChart>(Msg.Key(MsgKeys.App_Unsupported_chart_extension), input);
    }

    private static ChartFormat GetChartFormat(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mgxc" => ChartFormat.Mgxc,
            ".ugc" => ChartFormat.Ugc,
            ".sus" => ChartFormat.Sus,
            ".c2s" => ChartFormat.C2s,
            _ => throw new DiagnosticException(Msg.Key(MsgKeys.App_Unsupported_chart_extension), path)
        };
    }
}
