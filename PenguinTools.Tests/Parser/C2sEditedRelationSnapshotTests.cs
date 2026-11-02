using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sEditedRelationSnapshotTests
{
    [Fact]
    public async Task EditedOverlappingAirColorsAreNotReplacedByLegacyColorSnapshots()
    {
        var chart = await ReadC2s("""
            SLC 0 0 0 2 96 4 2 SLD
            SLC 0 0 12 2 96 4 2 SLD
            ASD 0 96 4 2 SLD 1 96 8 2 2 GRN
            ASD 0 96 4 2 SLD 1 96 8 2 2 PNK
            """);
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(path, ct);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(":S", StringComparison.Ordinal))
                {
                    lines[i] = lines[i][..^1] + (lines[i][^1] == 'I' ? 'N' : 'I');
                }
            }
            await File.WriteAllLinesAsync(path, lines, ct);
            var edited = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(edited.Succeeded, edited.ToString());
            Assert.False(edited.Value!.Extras.BinarySnapshotValid);
            Assert.Equal([Color.PNK, Color.DEF], edited.Value.Notes.Children.OfType<U.AirSlide>().Select(n => n.Color));

            var restored = await ThroughC2s(edited.Value);
            Assert.Equal([Color.PNK, Color.DEF], restored.Notes.Children.OfType<U.AirSlide>().Select(n => n.Color));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(".mgxc", false)]
    [InlineData(".ugc", false)]
    [InlineData(".mgxc", true)]
    [InlineData(".ugc", true)]
    public async Task MovingOneAirToAnOverlappingSlide_PreservesItsNewParent(string extension, bool saveAfterEdit)
    {
        var chart = await ReadC2s("""
            SLC 0 0 0 2 48 1 2 SLD
            SLC 0 0 0 2 48 13 2 SLD
            SLD 0 48 1 2 48 7 2 SLD
            SLD 0 48 13 2 48 7 2 SLD
            AIR 0 96 7 2 SLD DEF
            """);
        chart = await ThroughEditor(chart, extension);
        Assert.Contains("ORDER\t", chart.Extras.SourceSnapshot, StringComparison.Ordinal);
        var air = Assert.Single(chart.Notes.Children.OfType<U.Air>());
        Assert.Equal(1, Assert.IsType<U.Slide>(air.PairNote!.Parent).Children[0].Lane);
        var intended = chart.Notes.Children.OfType<U.Slide>().Single(s => s.Children[0].Lane == 13);
        Assert.IsType<U.SlideJoint>(intended.LastChild).MakePair(air);

        if (saveAfterEdit)
        {
            chart = await ThroughEditor(chart, extension);
        }

        var result = await ThroughC2s(chart);
        Assert.Equal(2, result.Notes.Children.OfType<U.Slide>().Count());
        var actual = Assert.Single(result.Notes.Children.OfType<U.Air>());
        Assert.Equal(13, Assert.IsType<U.Slide>(actual.PairNote!.Parent).Children[0].Lane);
    }

    [Theory]
    [InlineData(".mgxc", false)]
    [InlineData(".ugc", false)]
    [InlineData(".mgxc", true)]
    [InlineData(".ugc", true)]
    public async Task SwappingContinuationsAtASharedControl_PreservesTheEditedPaths(string extension, bool saveAfterEdit)
    {
        var chart = await ReadC2s("""
            SLC 0 0 0 2 96 7 2 SLD
            SLC 0 0 12 2 96 7 2 SLD
            SLD 0 96 7 2 96 0 2 SLD
            SLD 0 96 7 2 96 12 2 SLD
            AIR 0 192 0 2 SLD DEF
            AUL 0 192 12 2 SLD DEF
            """);
        chart = await ThroughEditor(chart, extension);
        Assert.Contains("ORDER\t", chart.Extras.SourceSnapshot, StringComparison.Ordinal);
        var paths = chart.Notes.Children.OfType<U.Slide>().OrderBy(s => s.Lane).ToArray();
        var firstTail = paths[0].LastChild!;
        var secondTail = paths[1].LastChild!;
        paths[0].AppendChild(secondTail);
        paths[1].AppendChild(firstTail);

        if (saveAfterEdit)
        {
            chart = await ThroughEditor(chart, extension);
        }

        var result = await ThroughC2s(chart);
        var actual = result.Notes.Children.OfType<U.Slide>().OrderBy(s => s.Lane).ToArray();
        Assert.Equal(2, actual.Length);
        Assert.Equal([12, 0], actual.Select(s => s.LastChild!.Lane));
        Assert.All(actual, s => Assert.NotNull(Assert.IsType<U.SlideJoint>(s.LastChild).PairNote));
    }

    private static async Task<U.Chart> ReadC2s(string body)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "VERSION 1.15.00 1.15.00\nRESOLUTION 384\nBPM_DEF 120 120 120 120\nMET_DEF 4 4\n" + body, ct);
            return await ReadC2sFile(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<U.Chart> ReadC2sFile(string path)
    {
        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        return converted.Value!;
    }

    private static async Task<U.Chart> ThroughEditor(U.Chart chart, string extension)
    {
        var path = TestTempPaths.Create(extension);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = extension == ".mgxc"
                ? await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)
                : await new UgcChartWriter(path, chart).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = extension == ".mgxc"
                ? await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed.Value!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<U.Chart> ThroughC2s(U.Chart chart)
    {
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            return await ReadC2sFile(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
