using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class C2sUnsupportedNoteTests
{
    [Theory]
    [InlineData("ASO", "ASO\t0\t0\t0\t4\t5\t6\t192\t4\t4\t10\t11\tDEF")]
    [InlineData("HHD", "HHD\t0\t0\t0\t4\t5\t192\t0\t4\t5\t0")]
    [InlineData("HHX", "HHX\t0\t0\t0\t4\t5\t192\t0\t4\t5\t0\tUP")]
    public async Task UnsupportedObjects_WarnAndPreserveSupportedObjects(string noteType, string note)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path,
                "VERSION\t1.15.00\t1.15.00\nBPM\t0\t0\t120\nTAP\t0\t0\t8\t4\n" + note +
                "\nTAP\t0\t96\t4\t2\n", ct);

            var result = await new C2SParser(new(path)).ParseAsync(ct);

            Assert.True(result.Succeeded, result.ToString());
            Assert.Collection(result.Value!.Notes,
                note => Assert.IsType<Chart.Models.c2s.Tap>(note),
                note => Assert.IsType<Chart.Models.c2s.Tap>(note));
            var diagnostic = Assert.Single(result.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.C2s_Unsupported_note_type);
            Assert.Equal(Severity.Warning, diagnostic.Severity);
            Assert.Equal(path, diagnostic.Path);
            Assert.Equal(4, diagnostic.Line);
            Assert.Equal(noteType, diagnostic.Message.Args!["noteType"]);
            Assert.Equal(note, Assert.IsType<SourceLineDiagnosticTarget>(diagnostic.Target).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
