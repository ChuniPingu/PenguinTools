using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class MgxcDiagnosticTests
{
    [Fact]
    public async Task InvalidHeader_DiagnosticIncludesFileAndOffset()
    {
        var ct = TestContext.Current.CancellationToken;
        var tmp = Path.GetTempFileName() + ".mgxc";
        try
        {
            await File.WriteAllBytesAsync(tmp, "NOPE"u8.ToArray(), ct);

            var result = await new MgxcParser(
                new MgxcParseRequest(tmp, TestAssets.Load()),
                TestMediaTool.Instance).ParseAsync(ct);

            Assert.False(result.Succeeded);
            var diagnostic = Assert.Single(result.Diagnostics.Diagnostics);
            Assert.Equal(Severity.Error, diagnostic.Severity);
            Assert.Equal(tmp, diagnostic.Path);
            Assert.Equal(0, diagnostic.Line);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Meta_Ignore_SkipsParse_AndSuppressesOtherDiagnostics()
    {
        var ct = TestContext.Current.CancellationToken;
        var tmpUgc = Path.GetTempFileName() + ".ugc";
        var tmpMgxc = Path.GetTempFileName() + ".mgxc";
        try
        {
            await File.WriteAllTextAsync(
                tmpUgc,
                "@VER\t8\n@TICKS\t480\n@TITLE\tHello\n@SONGID\t1\n@BPM\t0'0\t120.0\n@BEAT\t0\t4\t4\n",
                ct);
            var parsed = await new UgcParser(
                new UgcParseRequest(tmpUgc, TestAssets.Load()),
                TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            parsed.Value!.Meta.Comment = "#meta ignore";

            var written = await new MgxcChartWriter(new MgxcWriteRequest(tmpMgxc, parsed.Value)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());

            var result = await new MgxcParser(
                new MgxcParseRequest(tmpMgxc, TestAssets.Load()),
                TestMediaTool.Instance).ParseAsync(ct);

            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            var diagnostic = Assert.Single(result.Diagnostics.Diagnostics);
            Assert.Equal(Severity.Information, diagnostic.Severity);
            Assert.Equal(MsgKeys.Mg_Meta_Ignored, diagnostic.Message.Key);
        }
        finally
        {
            File.Delete(tmpUgc);
            File.Delete(tmpMgxc);
        }
    }
}
