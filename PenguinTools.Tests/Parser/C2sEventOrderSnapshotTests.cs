using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sEventOrderSnapshotTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task SameTickBpmReorderInvalidatesSavedEventOrder(bool binary, bool reencode, bool legacyHash)
    {
        var input = TestTempPaths.Create(".c2s");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(input,
                "VERSION\t1.15.00\t1.15.00\nRESOLUTION\t384\nBPM\t0\t0\t120\nBPM\t0\t0\t180\n" +
                "MET\t0\t0\t4\t4\nTAP\t1\t0\t0\t4\n", ct);
            var source = await new C2SParser(new C2SParseRequest(input)).ParseAsync(ct);
            Assert.True(source.Succeeded, source.ToString());
            var converted = new UgcChartConverter(new UgcConvertRequest(source.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            var editor = converted.Value!;
            editor.Extras.HasSpeedSnapshot = false;
            if (legacyHash)
            {
                editor.Extras.SourceSnapshot = string.Join('\n', editor.Extras.SourceSnapshot.Split('\n')
                    .Where(line => !line.StartsWith("VIEWSEQHASH\t", StringComparison.Ordinal)));
            }
            editor = await EditorRoundTrip(editor, binary);
            var bpms = editor.Events.Children.OfType<U.BpmEvent>().ToArray();
            editor.Events.InsertBefore(bpms[1], bpms[0]);
            if (reencode)
            {
                editor = await EditorRoundTrip(editor, binary);
            }
            var native = new C2SChartConverter(new C2SConvertRequest(editor)).Convert();
            Assert.True(native.Succeeded, native.ToString());
            var saved = await new C2SChartWriter(new C2SWriteRequest(output, native.Value!)).WriteAsync(ct);
            Assert.True(saved.Succeeded, saved.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal([180m, 120m], parsed.Value!.Events.OfType<C.Bpm>().Select(e => e.Value));
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    private static async Task<U.Chart> EditorRoundTrip(U.Chart source, bool binary)
    {
        var path = TestTempPaths.Create(binary ? ".mgxc" : ".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = binary
                ? await new MgxcChartWriter(new MgxcWriteRequest(path, source)).WriteAsync(ct)
                : await new UgcChartWriter(path, source).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = binary
                ? await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }
}
