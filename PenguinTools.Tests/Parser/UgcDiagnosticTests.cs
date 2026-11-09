using PenguinTools.Application;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class UgcDiagnosticTests
{
    [Fact]
    public async Task InvalidClickCount_WarnsAndKeepsTheLastValidSetting()
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path,
                "@VER\t9\n@CLKCNT\t2\n@CLKCNT\t-1\n@BPM\t0'0\t150\n#0'480:tG4\n", ct);
            var result = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(result.Succeeded, result.ToString());
            Assert.False(result.Diagnostics.HasError);
            Assert.Equal(2, result.Value!.Extras.ClickCount);
            Assert.Equal(480, Assert.Single(result.Value.Notes.Children).Tick.Original);
            var warning = Assert.Single(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Invalid_metadata);
            Assert.Equal(Severity.Warning, warning.Severity);
            Assert.Equal(path, warning.Path);
            Assert.Equal(3, warning.Line);
        }
        finally { File.Delete(path); }
    }

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
            Assert.Equal("#bad", Assert.IsType<SourceLineDiagnosticTarget>(warning.Target).Text);
            Assert.Equal("#bad", DiagnosticTargetSerializer.ToJsonElement(warning.Target)!.Value.GetProperty("text").GetString());
            var sortName = Assert.Single(result.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.Mg_No_sortname_provided);
            Assert.Equal(result.Value!.Meta.SortName, sortName.Message.Args!["sortName"]);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
