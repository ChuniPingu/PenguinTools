using System.Globalization;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sLengthDiagnosticTests
{
    [Theory]
    [InlineData("SLD", 2, false, false, 0, 1)]
    [InlineData("SLD", 3, false, false, 1, 0)]
    [InlineData("SLD", 2, true, false, 55, 0)]
    [InlineData("SLD", 3, true, false, 55, 1)]
    [InlineData("SLD", 2, true, true, 0, 1)]
    [InlineData("SLD", 3, true, true, 1, 0)]
    [InlineData("HLD", 2, true, false, 55, 0)]
    [InlineData("HLD", 3, true, false, 55, 1)]
    public async Task LongNoteWarningMatchesSerializedLengthAfterApplyingOrigin(
        string kind, int start, bool blank, bool fixedOrigin, int writtenStart, int writtenLength)
    {
        var source = CreateChart(blank, fixedOrigin);
        U.Note note;
        if (kind == "SLD")
        {
            note = new U.Slide { Tick = start, Lane = 0, Width = 4 };
            note.AppendChild(new U.SlideJoint { Tick = start + 1, Lane = 4, Width = 4, Joint = Joint.D });
        }
        else
        {
            note = new U.Hold { Tick = start, Lane = 0, Width = 4 };
            note.AppendChild(new U.HoldJoint { Tick = start + 1 });
        }
        source.Notes.AppendChild(note);

        var result = new C2SChartConverter(new C2SConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());
        var converted = Assert.Single(result.Value!.Notes.OfType<C.LongNote>());
        AssertWarning(result.Diagnostics.Diagnostics, converted, writtenLength);
        await AssertSerializedLength(result.Value, kind, writtenStart, writtenLength);
        Assert.Equal(start, note.Tick.Original);
        Assert.Equal(start + 1, note.Children[0].Tick.Original);
    }

    [Theory]
    [InlineData(2, false, 0, 1)]
    [InlineData(3, false, 1, 0)]
    [InlineData(2, true, 55, 0)]
    [InlineData(3, true, 55, 1)]
    public async Task RestoredSlaWarningUsesQuantizedAbsoluteEndpoints(
        int start, bool blank, int writtenStart, int writtenLength)
    {
        var source = CreateChart(blank, fixedOrigin: false);
        source.Meta.C2sSlaSnapshot = $"{start},1,0,4,1";

        var result = new C2SChartConverter(new C2SConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());
        var converted = Assert.Single(result.Value!.Notes.OfType<C.Sla>());
        AssertWarning(result.Diagnostics.Diagnostics, converted, writtenLength);
        await AssertSerializedLength(result.Value, "SLA", writtenStart, writtenLength);
    }

    private static U.Chart CreateChart(bool blank, bool fixedOrigin)
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = blank;
        chart.Meta.BgmInitialNumerator = 1;
        chart.Meta.BgmInitialDenominator = 7;
        chart.Extras.FixedZeroOrigin = fixedOrigin;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Tick = 0, Numerator = 1, Denominator = 7 });
        return chart;
    }

    private static void AssertWarning(IEnumerable<Diagnostic> diagnostics, C.Note note, int writtenLength)
    {
        var warnings = diagnostics.Where(d => d.Message.Key == MsgKeys.Mg_Length_smaller_than_unit).ToArray();
        if (writtenLength > 0)
        {
            Assert.Empty(warnings);
            return;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Equal(note.Tick.Original, warning.Time);
        Assert.Same(note, warning.Target);
        Assert.Equal(0, warning.Message.Args!["arg0"]);
    }

    private static async Task AssertSerializedLength(C.Chart chart, string kind, int start, int length)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var row = Assert.Single((await File.ReadAllLinesAsync(path, ct))
                .Select(line => line.Split('\t')), fields => fields[0] == kind);
            Assert.Equal(start, Int(row[1]) * 384 + Int(row[2]));
            Assert.Equal(length, Int(row[5]));
        }
        finally { File.Delete(path); }
    }

    private static int Int(string value) => int.Parse(value, CultureInfo.InvariantCulture);
}
