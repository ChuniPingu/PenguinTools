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

public sealed class C2sDcmConversionTests
{
    [Theory]
    [InlineData("DCM 0 0 96 2\nDCM 0 48 96 3", "0:2;480:3;720:1")]
    [InlineData("DCM 0 48 96 3\nDCM 0 0 96 2", "0:2;480:3;720:1")]
    [InlineData("DCM 0 0 96 2\nDCM 0 0 192 3", "0:2;480:3;960:1")]
    [InlineData("DCM 0 0 192 3\nDCM 0 0 96 2", "0:3;960:1")]
    [InlineData("DCM 0 0 96 2\nDCM 0 0 96 2", "0:2;480:1")]
    [InlineData("DCM 0 0 192 2\nDCM 0 48 48 3", "0:2;960:1")]
    [InlineData("DCM 0 0 0 2\nDCM 0 96 -48 3", "")]
    [InlineData("DCM 0 0 192 0\nDCM 0 48 48 3", "240:3;480:1")]
    [InlineData("DCM 0 0 96 1\nDCM 0 48 144 3", "480:3;960:1")]
    [InlineData("DCM 0 0 96 -2\nDCM 0 96 96 3", "0:-2;480:3;960:1")]
    public async Task ProjectionUsesFirstActiveNonzeroInterval(string rows, string expected)
    {
        var chart = await Read(rows);
        var converted = new UgcChartConverter(new UgcConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.Equal(expected, SpeedView(converted.Value!));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EditorRoundTripRetainsIntervalsUntilSpeedIsEdited(bool binary, bool edited)
    {
        var source = await Read("DCM 0 0 96 2\nDCM 0 48 96 3\nDCM 0 24 0 99\nDCM 0 0 192 0");
        var editor = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(editor.Succeeded, editor.ToString());
        var path = TestTempPaths.Create(binary ? ".mgxc" : ".ugc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = binary
                ? await new MgxcChartWriter(new MgxcWriteRequest(path, editor.Value!)).WriteAsync(ct)
                : await new UgcChartWriter(path, editor.Value!).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = binary
                ? await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal("0:2;480:3;720:1", SpeedView(parsed.Value!));
            if (edited)
            {
                parsed.Value!.Events.Children.OfType<U.NoteSpeedEvent>().Single(e => e.Tick.Original == 0).Speed = 4;
            }
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            var saved = await new C2SChartWriter(new C2SWriteRequest(output, converted.Value!)).WriteAsync(ct);
            Assert.True(saved.Succeeded, saved.ToString());
            var native = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(native.Succeeded, native.ToString());
            var dcm = native.Value!.Events.OfType<C.Dcm>().ToArray();
            if (edited)
            {
                Assert.Equal([(0, 480, 4m), (480, 240, 3m)],
                    dcm.Select(e => (e.Tick.Original, e.Length.Original, e.Speed)));
            }
            else
            {
                Assert.Equal(source.Events.OfType<C.Dcm>().OrderBy(e => e.Tick).Select(e => (e.Tick.Original, e.Length.Original, e.Speed)),
                    dcm.Select(e => (e.Tick.Original, e.Length.Original, e.Speed)));
            }
        }
        finally
        {
            File.Delete(path);
            File.Delete(output);
        }
    }

    private static string SpeedView(U.Chart chart) => string.Join(';', chart.Events.Children.OfType<U.NoteSpeedEvent>()
        .OrderBy(e => e.Tick).Select(e => FormattableString.Invariant($"{e.Tick.Original}:{e.Speed}")));

    private static async Task<C.Chart> Read(string rows)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path,
                "VERSION 1.15.00 1.15.00\nRESOLUTION 384\nBPM 0 0 120\nMET 0 0 4 4\nTAP 2 0 0 4\n" + rows, ct);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
