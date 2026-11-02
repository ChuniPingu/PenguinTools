using System.Buffers.Binary;
using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sEditedHeaderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeHeaderEditsSurviveInvalidatedNoteSnapshots(bool binary, bool editHeaders)
    {
        var input = TestTempPaths.Create(".c2s");
        var editor = TestTempPaths.Create(binary ? ".mgxc" : ".ugc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            // C2S accepts spaces as well as tabs; its default meter can differ
            // from the first explicit meter and must survive unrelated edits.
            await File.WriteAllTextAsync(input, """
                VERSION 1.15.00 1.15.00
                CREATOR Original
                BPM_DEF 120 120 120 120
                MET_DEF 8 7
                RESOLUTION 384
                TUTORIAL 0
                BPM 0 0 120
                MET 0 0 4 4
                STP 0 0 200
                HLD 0 0 0 4 100
                """, ct);
            var source = await new C2SParser(new(input)).ParseAsync(ct);
            Assert.True(source.Succeeded, source.ToString());
            var converted = new UgcChartConverter(new(source.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            var written = binary
                ? await new MgxcChartWriter(new(editor, converted.Value!)).WriteAsync(ct)
                : await new UgcChartWriter(editor, converted.Value!).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());

            if (binary)
            {
                var bytes = await File.ReadAllBytesAsync(editor, ct);
                var note = bytes.AsSpan().IndexOf("dat2"u8) + 8;
                bytes[note + 7] = 1;
                if (editHeaders)
                {
                    "Retitled"u8.CopyTo(bytes.AsSpan(bytes.AsSpan().IndexOf("Original"u8)));
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(bytes.AsSpan().IndexOf("beat"u8) + 10), 3);
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(bytes.AsSpan().IndexOf("ttrl"u8) + 6), 1);
                }
                await File.WriteAllBytesAsync(editor, bytes, ct);
            }
            else
            {
                var text = (await File.ReadAllTextAsync(editor, ct)).Replace("#0'0:hG4", "#0'0:hH4", StringComparison.Ordinal);
                if (editHeaders)
                {
                    text = text.Replace("@DESIGN\tOriginal", "@DESIGN\tRetitled", StringComparison.Ordinal)
                        .Replace("@BEAT\t0\t4\t4", "@BEAT\t0\t3\t4", StringComparison.Ordinal)
                        .Replace("@FLAG\tDIFFTTL\t0", "@FLAG\tDIFFTTL\t1", StringComparison.Ordinal);
                }
                await File.WriteAllTextAsync(editor, text, ct);
            }

            var parsed = binary
                ? await new MgxcParser(new(editor, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new(editor, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.False(parsed.Value!.Extras.BinarySnapshotValid);
            Assert.Equal(1, Assert.Single(parsed.Value.Notes.Children.OfType<U.Hold>()).Lane);
            var rows = await WriteNative(parsed.Value, output);

            Assert.Equal(["CREATOR", editHeaders ? "Retitled" : "Original"], rows["CREATOR"]);
            Assert.Equal(["MET_DEF", editHeaders ? "4" : "8", editHeaders ? "3" : "7"], rows["MET_DEF"]);
            Assert.Equal(["TUTORIAL", editHeaders ? "1" : "0"], rows["TUTORIAL"]);
            Assert.Equal(editHeaders ? "0" : "3", rows["T_JUDGE_HLD"][1]);
        }
        finally
        {
            File.Delete(input);
            File.Delete(editor);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task ChangedJudgeOptionsTakePrecedenceOverSavedSourceHeaders()
    {
        var input = TestTempPaths.Create(".c2s");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input, """
                VERSION 1.15.00 1.15.00
                RESOLUTION 384
                PROGJUDGE_BPM 240
                PROGJUDGE_AER 0.999
                BPM 0 0 120
                MET 0 0 4 4
                HLD 0 0 0 4 384
                """, ct);
            var parsed = await new C2SParser(new(input)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new UgcChartConverter(new(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            var editor = converted.Value!;
            editor.Extras.Headers["PROGJUDGE_BPM"] = "PROGJUDGE_BPM\t120";
            editor.Extras.Headers["PROGJUDGE_AER"] = "PROGJUDGE_AER\t0.5";
            var rows = await WriteNative(editor, output);
            Assert.Equal(["PROGJUDGE_BPM", "120"], rows["PROGJUDGE_BPM"]);
            Assert.Equal(["PROGJUDGE_AER", "0.5"], rows["PROGJUDGE_AER"]);
            Assert.Equal("4", rows["T_JUDGE_HLD"][1]);
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    private static async Task<Dictionary<string, string[]>> WriteNative(U.Chart chart, string path)
    {
        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var ct = TestContext.Current.CancellationToken;
        var written = await new C2SChartWriter(new(path, converted.Value!)).WriteAsync(ct);
        Assert.True(written.Succeeded, written.ToString());
        return (await File.ReadAllLinesAsync(path, ct)).Select(C2SRoundTrip.Parts)
            .Where(p => p.Length > 0).ToDictionary(p => p[0]);
    }
}
