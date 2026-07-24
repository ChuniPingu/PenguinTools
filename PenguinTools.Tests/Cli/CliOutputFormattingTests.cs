using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.Chart;
using PenguinTools.CLI;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Cli;

public class CliOutputFormattingTests
{
    [Fact]
    public void OptionScanConfig_SerializesHcaEncryptionKeyAsString()
    {
        var config = new OptionScanConfig(
            "A001",
            "option-id",
            true,
            ["mgxc"],
            true,
            true,
            true,
            32931609366120192UL,
            true,
            true,
            1,
            "title",
            1000001,
            1000002,
            8);
        var json = JsonSerializer.Serialize(config, CliJsonSerializerContext.Default.OptionScanConfig);
        using var document = JsonDocument.Parse(json);
        var key = document.RootElement.GetProperty("hcaEncryptionKey");
        Assert.Equal(JsonValueKind.String, key.ValueKind);
        Assert.Equal("32931609366120192", key.GetString());
    }

    [Fact]
    public void JsonEnvelope_UsesSchemaVersionThree()
    {
        var response = new CliResponse(CliOutput.ResultType, 3, "info", true, 0, Msg.Key("cli.msg.done"), null, []);
        var json = CliOutput.Serialize(response);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("result", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("cli.msg.done", document.RootElement.GetProperty("message").GetProperty("key").GetString());
        Assert.DoesNotContain(Environment.NewLine, json);
    }

    [Fact]
    public void ProgressEvent_UsesProgressTypeAndItemFields()
    {
        var payload = new CliProgressEvent(
            CliOutput.ProgressType,
            "music.build",
            "song.mgxc",
            "Ver seX",
            2,
            4,
            50);
        var json = CliOutput.SerializeProgress(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("progress", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("music.build", document.RootElement.GetProperty("operation").GetString());
        Assert.False(document.RootElement.TryGetProperty("phase", out _));
        Assert.False(document.RootElement.TryGetProperty("step", out _));
        Assert.False(document.RootElement.TryGetProperty("unit", out _));
        Assert.Equal("song.mgxc", document.RootElement.GetProperty("item").GetString());
        Assert.Equal("Ver seX", document.RootElement.GetProperty("label").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("completed").GetInt32());
        Assert.Equal(4, document.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(50, document.RootElement.GetProperty("percent").GetDouble());
        Assert.DoesNotContain(Environment.NewLine, json);
    }

    [Fact]
    public void ParseFailure_IncludesCommandLineErrors()
    {
        var message = Msg.Create(MsgKeys.Cli_Msg_command_line_parsing_failed,
            "Unrecognized command or argument '--no-progress'.");
        var json = CliOutput.Serialize(new CliResponse(
            CliOutput.ResultType,
            3,
            "parse",
            false,
            CliExitCodes.SyntaxError,
            message,
            null,
            CliDiagnostics.ToPayload(CliDiagnostics.SnapshotFromMessage(message))));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            "Unrecognized command or argument '--no-progress'.",
            document.RootElement.GetProperty("message").GetProperty("args").GetProperty("arg0").GetString());
    }

    [Theory]
    [InlineData("chart inspect file.mgxc")]
    [InlineData("chart convert input.mgxc output.c2s")]
    [InlineData("chart convert input.mgxc output.c2s --no-progress")]
    [InlineData("chart convert input.c2s output.ugc --debug-til")]
    [InlineData("option scan input --no-progress")]
    [InlineData("option scan input")]
    [InlineData("option build input output --no-progress")]
    [InlineData("music build input.mgxc output --no-progress")]
    [InlineData("music extract input output --no-progress")]
    [InlineData("music extract input output --debug-til --no-progress")]
    [InlineData("audio extract input.acb output --no-progress")]
    [InlineData("stage extract input.afb output --no-progress")]
    [InlineData("assets collect game --output assets.user.json --no-progress")]
    [InlineData("jacket convert input.mgxc output.dds")]
    [InlineData("audio convert input.mgxc output")]
    [InlineData("stage build input.mgxc output")]
    [InlineData("stage extract input.afb output")]
    [InlineData("assets collect game --output assets.user.json")]
    [InlineData("info")]
    public void CommandTree_Parses(string commandLine)
    {
        var result = RootCommands.BuildRootCommand().Parse(commandLine);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("info --format json")]
    [InlineData("info --no-pretty")]
    [InlineData("scan .")]
    [InlineData("music export input output")]
    [InlineData("option convert input output")]
    [InlineData("media audio input output")]
    public void LegacyOrRemovedOptions_AreRejected(string commandLine)
    {
        var result = RootCommands.BuildRootCommand().Parse(commandLine);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ChartInspectResult_SerializesMetadataEntries()
    {
        var metadata = new ChartConversionMetadata(
            3, "Master", "", "", 0, 0, 0, false, 120, 4, 4, 2, 0, "", "",
            false, 1001000, "", "",
            new ApplicationEntry(0, "Orange", "オレンジ"),
            new ApplicationEntry(8, "stage"),
            new ApplicationEntry(1000, "genre"),
            new ApplicationEntry(-1, "Invalid"),
            0,
            "N/A",
            "",
            null,
            "2026-01-01",
            0);
        var result = new ChartInspectResult(
            "input.ugc",
            new ChartSummary("1000", 1000, "Test", "", "Original", "Master", 0, 120, "input.ugc"),
            metadata);
        var element = JsonSerializer.SerializeToElement(result, CliJsonSerializerContext.Default.ChartInspectResult);
        Assert.Equal("Orange", element.GetProperty("metadata").GetProperty("notesFieldLine").GetProperty("name")
            .GetString());
    }

    [Fact]
    public void CliResponse_SerializesDiagnosticsWithEntryInMessageArgs()
    {
        var message = Msg.Create(MsgKeys.Error_Unhandled, ("detail", new Entry(1, "genre", "data")));
        var json = CliOutput.Serialize(new CliResponse(
            CliOutput.ResultType,
            3,
            "test",
            false,
            CliExitCodes.Failure,
            CliDiagnostics.SanitizeMessage(message),
            null,
            CliDiagnostics.ToPayload(CliDiagnostics.SnapshotFromMessage(message))));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("diag.error.unhandled", document.RootElement.GetProperty("message").GetProperty("key")
            .GetString());
        Assert.Equal("1 genre data",
            document.RootElement.GetProperty("message").GetProperty("args").GetProperty("detail").GetString());
    }

    [Fact]
    public void CliResponse_SerializesDiagnosticsWithStructuredTimePosition()
    {
        var calculator = new TimeCalculator(1920, []);
        var diagnostic = new TimedDiagnostic(Severity.Warning, Msg.Key("diag.test.timed"), 1920)
        {
            TimeCalculator = calculator
        };
        var json = CliOutput.Serialize(new CliResponse(
            CliOutput.ResultType,
            3,
            "test",
            true,
            CliExitCodes.Success,
            null,
            null,
            CliDiagnostics.ToPayload([diagnostic])));
        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal(1920, payload.GetProperty("time").GetInt32());
        var position = payload.GetProperty("timePosition");
        Assert.Equal(2, position.GetProperty("bar").GetInt32());
        Assert.Equal(1, position.GetProperty("beat").GetInt32());
        Assert.Equal(0, position.GetProperty("tickOffset").GetInt32());
        Assert.False(payload.TryGetProperty("formattedTime", out _));
    }

    [Fact]
    public void OptionScanDifficulty_SerializesEmbeddedDiagnosticTimePosition()
    {
        var calculator = new TimeCalculator(1920, []);
        var diagnostic = new TimedDiagnostic(Severity.Warning, Msg.Key("diag.test.timed"), 1920)
        {
            TimeCalculator = calculator
        };
        var position = DiagnosticTime.TryGetPosition(diagnostic);
        Assert.NotNull(position);

        var chart = new OptionScanDifficulty(
            "Master", null, 1, "Title", "Artist", "Designer", 14, 180, 0, true, "chart.ugc",
            new ApplicationEntry(0, "tag"), 0, "N/A", "", new ApplicationEntry(0, "genre"), null, "2026-01-01",
            "", "", 0, 0, 0, 0, false, 120, 4, 4, false, null, "",
            new ApplicationEntry(0, "line"), new ApplicationEntry(0, "stage"),
            [new ApplicationDiagnostic("Warning", Msg.Key("diag.test.timed"), "chart.ugc", null, 1920, position)]);
        var book = new OptionScanBook(
            1, "Title", "Artist", "Master", "", new ApplicationEntry(0, "genre"), null, "2026-01-01",
            false, null, "", new ApplicationEntry(0, "line"), new ApplicationEntry(0, "stage"),
            new ApplicationEntry(0, "tag"), 0, "N/A", "", "", 0, 0, 0, 0, false, 120, 4, 4, [chart]);
        var result = new OptionScanResult("input", ["ugc"], 1, [book], [], null, null);
        var element = JsonSerializer.SerializeToElement(result, CliJsonSerializerContext.Default.OptionScanResult);
        var embedded = element.GetProperty("books")[0].GetProperty("charts")[0].GetProperty("diagnostics")[0];
        Assert.Equal(1920, embedded.GetProperty("time").GetInt32());
        Assert.Equal(2, embedded.GetProperty("timePosition").GetProperty("bar").GetInt32());
    }

    [Fact]
    public void CliResponse_OmitsTimePositionWhenCalculatorMissing()
    {
        var diagnostic = new TimedDiagnostic(Severity.Warning, Msg.Key("diag.test.timed"), 1920);
        var json = CliOutput.Serialize(new CliResponse(
            CliOutput.ResultType,
            3,
            "test",
            true,
            CliExitCodes.Success,
            null,
            null,
            CliDiagnostics.ToPayload([diagnostic])));
        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal(1920, payload.GetProperty("time").GetInt32());
        Assert.False(payload.TryGetProperty("timePosition", out _));
    }

    [Fact]
    public async Task CommandRunner_UsesCancellationExitCode()
    {
        var exitCode = await CliCommandRunner.RunAsync("test.cancel",
            (_, _) => Task.FromCanceled<OperationResult<ApplicationInfo>>(new CancellationToken(true)),
            _ => Msg.Key("test.unreachable"), CliJsonSerializerContext.Default.ApplicationInfo,
            TestContext.Current.CancellationToken);
        Assert.Equal(CliExitCodes.Cancelled, exitCode);
    }
}