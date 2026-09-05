using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class UgcDiagnosticTests
{
    [Fact]
    public async Task InvalidHeader_DiagnosticIncludesFileAndLine()
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(tmp, "@VER\t7\n@TICKS\t480\n@BPM\t0'0\t120.0\n", ct);

            var result = await new UgcParser(
                new UgcParseRequest(tmp, TestAssets.Load()),
                TestMediaTool.Instance).ParseAsync(ct);

            Assert.False(result.Succeeded);
            var diagnostic = Assert.Single(result.Diagnostics.Diagnostics);
            Assert.Equal(Severity.Error, diagnostic.Severity);
            Assert.Equal(tmp, diagnostic.Path);
            Assert.Equal(1, diagnostic.Line);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task MalformedNote_DiagnosticKeepsOriginalSourceLine()
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(
                tmp,
                "@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n\n#bad\n",
                ct);

            var result = await new UgcParser(
                new UgcParseRequest(tmp, TestAssets.Load()),
                TestMediaTool.Instance).ParseAsync(ct);

            Assert.True(result.Succeeded, result.ToString());
            var warning = Assert.Single(result.Diagnostics.Diagnostics, d => d.Severity == Severity.Warning);
            Assert.Equal(tmp, warning.Path);
            Assert.Equal(6, warning.Line);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
