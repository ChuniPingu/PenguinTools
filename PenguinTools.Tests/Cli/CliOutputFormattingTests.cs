using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.CLI;
using PenguinTools.Core;
using Xunit;

namespace PenguinTools.Tests.Cli;

public class CliOutputFormattingTests
{
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

    [Theory]
    [InlineData("chart inspect file.mgxc")]
    [InlineData("chart convert input.mgxc output.c2s")]
    [InlineData("option scan input")]
    [InlineData("option build input output")]
    [InlineData("music build input.mgxc output")]
    [InlineData("jacket convert input.mgxc output.dds")]
    [InlineData("audio convert input.mgxc output")]
    [InlineData("stage build input.mgxc output")]
    [InlineData("afb extract input.afb output")]
    [InlineData("assets collect game")]
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
    public async Task CommandRunner_UsesCancellationExitCode()
    {
        var exitCode = await CliCommandRunner.RunAsync("test.cancel",
            (_, _) => Task.FromCanceled<OperationResult<ApplicationInfo>>(new CancellationToken(true)),
            _ => Msg.Key("test.unreachable"), CliJsonSerializerContext.Default.ApplicationInfo,
            TestContext.Current.CancellationToken);
        Assert.Equal(CliExitCodes.Cancelled, exitCode);
    }
}