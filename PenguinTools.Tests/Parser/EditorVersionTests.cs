using System.Buffers.Binary;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.CLI;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class EditorVersionTests
{
    [Theory]
    [InlineData(".mgxc", 3, 8, 2)]
    [InlineData(".mgxc", 4, 8, 2)]
    [InlineData(".mgxc", int.MaxValue, 8, 2)]
    [InlineData(".ugc", 9, 2, 8)]
    [InlineData(".ugc", 10, 2, 8)]
    [InlineData(".ugc", int.MaxValue, 2, 8)]
    public async Task NewerVersion_IsRejectedBeforePayload_InEveryDialect(
        string extension, int version, int location, int maximumVersion)
    {
        var ct = TestContext.Current.CancellationToken;
        var input = TestTempPaths.Create(extension);
        try
        {
            await WriteInputAsync(input, version, ct);
            foreach (var dialect in new[] { ChartDialect.Auto, ChartDialect.Original, ChartDialect.Extended })
            {
                var result = extension == ".mgxc"
                    ? await new MgxcParser(new MgxcParseRequest(input, TestAssets.Load(), dialect),
                        TestMediaTool.Instance).ParseAsync(ct)
                    : await new UgcParser(new UgcParseRequest(input, TestAssets.Load(), dialect),
                        TestMediaTool.Instance).ParseAsync(ct);

                Assert.False(result.Succeeded);
                Assert.Null(result.Value);
                var diagnostic = Assert.Single(result.Diagnostics.Diagnostics);
                Assert.Equal(Severity.Error, diagnostic.Severity);
                Assert.Equal(MsgKeys.Error_Chart_version_too_new, diagnostic.Message.Key);
                Assert.Equal(input, diagnostic.Path);
                Assert.Equal(location, diagnostic.Line);
                Assert.Equal(version, diagnostic.Message.Args!["arg1"]);
                Assert.Equal(maximumVersion, diagnostic.Message.Args["arg2"]);
            }
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Theory]
    [InlineData(".mgxc", 4, false)]
    [InlineData(".mgxc", 4, true)]
    [InlineData(".ugc", 9, false)]
    [InlineData(".ugc", 9, true)]
    public async Task NewerVersion_ConversionFailsWithoutWritingOutput(string extension, int version, bool existingOutput)
    {
        var ct = TestContext.Current.CancellationToken;
        var input = TestTempPaths.Create(extension);
        var output = TestTempPaths.Create(".c2s");
        byte[] original = [0, 255, 13, 10, 42];
        try
        {
            await WriteInputAsync(input, version, ct);
            if (existingOutput)
            {
                await File.WriteAllBytesAsync(output, original, ct);
            }
            var parsed = RootCommands.BuildRootCommand().Parse(
                ["chart", "convert", input, output, "--no-progress"]);
            Assert.Empty(parsed.Errors);
            Assert.NotEqual(CliExitCodes.Success, await parsed.InvokeAsync(cancellationToken: ct));
            if (existingOutput)
            {
                Assert.Equal(original, await File.ReadAllBytesAsync(output, ct));
            }
            else
            {
                Assert.False(File.Exists(output));
            }
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    private static Task WriteInputAsync(string path, int version, CancellationToken ct)
    {
        if (System.IO.Path.GetExtension(path) == ".ugc")
        {
            return File.WriteAllTextAsync(path,
                $"' Created with Margrete v1.9.3\n@VER\t{version}\n#invalid-payload\n", ct);
        }
        // A header-only binary proves rejection happens before reading any blocks.
        var header = new byte[12];
        "MGXC"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), version);
        return File.WriteAllBytesAsync(path, header, ct);
    }
}
