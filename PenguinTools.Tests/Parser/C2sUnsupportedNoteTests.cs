using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class C2sUnsupportedNoteTests
{
    [Fact]
    public async Task HeavenHold_AirStaysBetweenTheOriginalSegments()
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.15.00	1.15.00
                BPM	0	0	120
                HHD	0	0	0	4	5	96	2	4	5	0
                AIR	0	96	2	4	HHD
                HHD	0	96	2	4	5	96	4	4	5	0
                """, ct);
            var parsed = await new C2SParser(new(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Diagnostics.Diagnostics);
            var written = await new C2SChartWriter(new C2SWriteRequest(path, parsed.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var rows = await File.ReadAllLinesAsync(path, ct);
            Assert.Equal(["HHD", "AIR", "HHD"], rows.Select(row => row.Split('\t')[0])
                .Where(tag => tag is "HHD" or "AIR"));
            Assert.Contains("T_JUDGE_ALL\t5", rows);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("ASO", "ASO\t0\t0\t0\t4\t5\t6\t192\t4\t4\t10\t11\tDEF", 2, 4)]
    [InlineData("HHD", "HHD\t0\t0\t0\t4\t5\t192\t0\t4\t5\t0", 6, 0)]
    [InlineData("HHX", "HHX\t0\t0\t0\t4\t5\t192\t0\t4\t5\t0\tTrace", 6, 0)]
    public async Task NativeNotes_PreserveAttachmentsAndPreventLossInEditorConversion(
        string noteType, string note, int combo, int endLane)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path,
                "VERSION\t1.15.00\t1.15.00\nBPM\t0\t0\t120\nTAP\t0\t0\t8\t4\n" + note +
                $"\nAIR\t0\t192\t{endLane}\t4\t{noteType}\n", ct);

            var result = await new C2SParser(new(path)).ParseAsync(ct);

            Assert.True(result.Succeeded, result.ToString());
            var chart = result.Value!;
            var native = Assert.Single(chart.Notes, parsedNote => parsedNote.Id == noteType);
            Assert.Same(native, Assert.Single(chart.Notes.OfType<Chart.Models.c2s.Air>()).Parent);
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var rows = await File.ReadAllLinesAsync(path, ct);
            Assert.Single(rows, row => row.StartsWith(noteType + "\t", StringComparison.Ordinal));
            Assert.Contains($"AIR\t0\t192\t{endLane}\t4\t{noteType}\tDEF", rows);
            Assert.Contains($"T_JUDGE_ALL\t{combo}", rows);

            var converted = new UgcChartConverter(new UgcConvertRequest(chart)).Convert();
            Assert.False(converted.Succeeded);
            var diagnostic = Assert.Single(converted.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.C2s_Unsupported_note_type);
            Assert.Equal(Severity.Error, diagnostic.Severity);
            Assert.Equal(path, diagnostic.Path);
            Assert.Equal(4, diagnostic.Line);
            Assert.Equal(noteType, diagnostic.Message.Args!["noteType"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
