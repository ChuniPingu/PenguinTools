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

public sealed class C2sMeterProjectionTests
{
    public static TheoryData<string, bool> MeterCases
    {
        get
        {
            var cases = new TheoryData<string, bool>();
            foreach (var rows in new[] { "", "MET 1 0 4 4", "MET 1 0 4 3", "MET 0 0 4 4\nMET 0 0 4 3" })
            {
                foreach (var firstBinary in new[] { false, true })
                {
                    cases.Add(rows, firstBinary);
                }
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(MeterCases))]
    public async Task EditorFormatChangesDoNotReplaceUneditedMeterList(string rows, bool firstBinary)
    {
        var source = await Read(rows);
        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var editor = await EditorRoundTrip(converted.Value!, firstBinary);
        editor = await EditorRoundTrip(editor, !firstBinary);
        var native = await ToNative(editor);

        Assert.Equal(source.Events.OfType<C.Met>().Select(e => (e.Tick.Original, e.Numerator, e.Denominator)),
            native.Events.OfType<C.Met>().Select(e => (e.Tick.Original, e.Numerator, e.Denominator)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditingTheMeterInvalidatesItsOriginalList(bool binary)
    {
        var source = await Read("MET 1 0 4 4");
        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var editor = await EditorRoundTrip(converted.Value!, binary);
        editor.Events.Children.OfType<U.BeatEvent>().Single().Numerator = 3;
        editor = await EditorRoundTrip(editor, binary);
        var native = await ToNative(editor);

        Assert.Contains(native.Events.OfType<C.Met>(), e => e.Tick.Original == 1920 && e.Numerator == 3 && e.Denominator == 4);
        Assert.DoesNotContain(native.Events.OfType<C.Met>(), e => e.Tick.Original == 1920 && e.Numerator == 4);
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

    private static async Task<C.Chart> ToNative(U.Chart editor)
    {
        var converted = new C2SChartConverter(new C2SConvertRequest(editor)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }

    private static async Task<C.Chart> Read(string rows)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path,
                ("VERSION 1.15.00 1.15.00\nRESOLUTION 384\nBPM 0 0 120\nMET_DEF 4 4\nTAP 2 0 0 4\n" + rows).Replace(' ', '\t'), ct);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }
}
