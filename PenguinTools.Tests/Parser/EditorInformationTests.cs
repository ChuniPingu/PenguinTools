using System.Text;
using PenguinTools.Chart;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core.Metadata;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class EditorInformationTests
{
    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task EditorInformation_SurvivesThreeUgcMgxcCycles(ChartDialect dialect)
    {
        var source = TestTempPaths.Create(".ugc");
        var ugc = TestTempPaths.Create(".ugc");
        var mgxc = TestTempPaths.Create(".mgxc");
        try
        {
            await File.WriteAllTextAsync(source,
                "'User annotation\n@VER\t8\n@EXVER\t1\n@TICKS\t480\n@BPM\t0'0\t120\n" +
                "@DIFF\t4\n@LEVEL\t4\n@WEATTR\tCustom WE attribute\n@GENRE\tCustom genre\n@RLDATE\t20200102\n" +
                "@BGMPRV\t1.25\t12.5\n@BGIMG\tbackground.png\n@BGSCENE\tscene\n@BGMODE\tPASSIVE\tTRUE\n" +
                "@FLDCOL\t7\n@FLDIMG\tfield.png\n@FLDSCENE\tfield-scene\n" +
                "@ATINFO\tAUTHORS\tAuthor A\tAuthor B\n@ATINFO\tSITES\thttps://example.org\n" +
                "@DLURL\thttps://example.org/download\n@SPDDEF\t1\t0\t0.125\n@CUSTOM_EXT\tretained value\n" +
                "@FLAG\tCUSTOM_FLAG\tTRUE\n@T_JUDGE_ALL\t999\n" +
                "#0'0:f04A\n#0'480:f44L\n#0'960:f84R\n", TestContext.Current.CancellationToken);
            var parsed = await new UgcParser(new UgcParseRequest(source, TestAssets.Load(), dialect), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var chart = parsed.Value!;
            chart.Meta.FieldColorName = "legacy color name";
            chart.Meta.Comment = "User comment\n第二行\t保留制表符";
            chart.Meta.Copyright = "Copyright author\n第二行版权";
            chart.Events.AppendChild(new U.BookmarkEvent { Id = "mark-a", Tick = 240, Tag = "ordinary bookmark", Rgb = "123456" });
            chart.Events.AppendChild(new U.BreakingMarker { Tick = 720 });
            var image = ImageEvent();
            chart.Extras.EditorImageEvents.Add(image);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Assert.True((await new UgcChartWriter(ugc, chart, dialect).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var text = await File.ReadAllTextAsync(ugc, TestContext.Current.CancellationToken);
                Assert.Contains("@LEVEL\t4", text);
                Assert.Contains("'User annotation", text);
                Assert.Equal(1, text.Split('\n').Count(l => l.StartsWith("@CUSTOM_EXT\t", StringComparison.Ordinal)));
                Assert.Equal(1, text.Split('\n').Count(l => l.StartsWith("@RLDATE\t", StringComparison.Ordinal)));
                Assert.Contains("@FLAG\tCUSTOM_FLAG\tTRUE", text);
                Assert.DoesNotContain("@T_JUDGE_ALL", text);
                var readUgc = await new UgcParser(new UgcParseRequest(ugc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(readUgc.Succeeded, readUgc.ToString());
                chart = readUgc.Value!;
                AssertInformation(chart, image);
                Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxc, chart, dialect))
                    .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var readMgxc = await new MgxcParser(new MgxcParseRequest(mgxc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(readMgxc.Succeeded, readMgxc.ToString());
                chart = readMgxc.Value!;
                AssertInformation(chart, image);
            }
        }
        finally
        {
            File.Delete(source);
            File.Delete(ugc);
            File.Delete(mgxc);
        }
    }

    private static void AssertInformation(U.Chart chart, byte[] image)
    {
        var m = chart.Meta;
        Assert.Equal(Difficulty.WorldsEnd, m.Difficulty);
        Assert.Equal(StarDifficulty.S4, m.WeDifficulty);
        Assert.Equal("Custom WE attribute", m.WeTag.Str);
        Assert.Equal("Custom genre", m.Genre?.Str);
        Assert.Equal(new DateTime(2020, 1, 2), m.ReleaseDate);
        Assert.Equal(1.25m, m.BgmPreviewStart);
        Assert.Equal(12.5m, m.BgmPreviewStop);
        Assert.Equal("background.png", m.BgiFilePath);
        Assert.Equal("scene", m.BackgroundScene);
        Assert.False(m.BackgroundSync);
        Assert.Equal(7, m.FieldColorIndex);
        Assert.Equal("legacy color name", m.FieldColorName);
        Assert.Equal("field.png", m.FieldImage);
        Assert.Equal("field-scene", m.FieldScene);
        Assert.Equal("Author A\tAuthor B", m.Authors);
        Assert.Equal("https://example.org", m.AuthorSites);
        Assert.Equal("https://example.org/download", m.DownloadUrl);
        Assert.Equal("User comment\n第二行\t保留制表符", m.Comment);
        Assert.Equal("Copyright author\n第二行版权", m.Copyright);
        Assert.Equal(['A', 'L', 'R'], chart.Notes.Children.OfType<U.Flick>().Select(f => f.AutoplayDirection));
        var bookmark = Assert.Single(chart.Events.Children.OfType<U.BookmarkEvent>());
        Assert.Equal(("mark-a", 240, "ordinary bookmark", "123456"), (bookmark.Id, bookmark.Tick.Original, bookmark.Tag, bookmark.Rgb));
        Assert.Equal(720, Assert.Single(chart.Events.Children.OfType<U.BreakingMarker>()).Tick.Original);
        Assert.Equal(image, Assert.Single(chart.Extras.EditorImageEvents));
        Assert.Contains("@CUSTOM_EXT\tretained value", chart.Extras.EditorHeaders);
        Assert.Contains("@SPDDEF\t1\t0\t0.125", chart.Extras.EditorHeaders);
    }

    private static byte[] ImageEvent()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("rimg"u8);
        foreach (var coordinate in new[] { 123, 456 })
        {
            writer.Write((short)2);
            writer.Write((short)0);
            writer.Write(coordinate);
        }
        var text = Encoding.UTF8.GetBytes("reference.png");
        writer.Write(4);
        writer.Write(text.Length);
        writer.Write(text);
        writer.Write(0);
        writer.Flush();
        return stream.ToArray();
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task RetainedUserText_RestoresNativeSaveWithoutOverwritingEdits(ChartDialect dialect)
    {
        var chart = new U.Chart();
        const string comment = "Original comment\n第二行\t制表符";
        const string copyright = "Original copyright\n第二行";
        chart.Meta.Comment = comment;
        chart.Meta.Copyright = copyright;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        var ugc = TestTempPaths.Create(".ugc");
        var mgxc = TestTempPaths.Create(".mgxc");
        try
        {
            Assert.True((await new UgcChartWriter(ugc, chart, dialect)
                .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            // A native editor reads the single-line view and retains the extension payload.
            chart.Meta.Comment = comment.Replace('\n', ' ').Replace('\t', ' ') + "\r\n";
            chart.Meta.Copyright = copyright.Replace('\n', ' ');
            foreach (var edited in new[] { false, true })
            {
                if (edited)
                {
                    chart.Meta.Comment = "Edited comment";
                    chart.Meta.Copyright = "Edited copyright";
                }
                Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxc, chart, dialect))
                    .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var parsed = await new MgxcParser(new MgxcParseRequest(mgxc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(parsed.Succeeded, parsed.ToString());
                Assert.Equal(edited ? "Edited comment" : comment, parsed.Value!.Meta.Comment);
                Assert.Equal(edited ? "Edited copyright" : copyright, parsed.Value.Meta.Copyright);
            }
        }
        finally
        {
            File.Delete(ugc);
            File.Delete(mgxc);
        }
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task RetainedUserText_DoesNotOverwriteEdits(ChartDialect dialect)
    {
        var chart = new U.Chart();
        chart.Meta.Comment = "Original comment\n第二行";
        chart.Meta.Copyright = "Original copyright\n第二行";
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = 0, Width = 4 });
        var path = TestTempPaths.Create(".ugc");
        try
        {
            Assert.True((await new UgcChartWriter(path, chart, dialect)
                .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            text = text.Replace("@CMT\tOriginal comment 第二行", "@CMT\tEdited comment", StringComparison.Ordinal)
                .Replace("Original copyright 第二行", "Edited copyright", StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal("Edited comment", parsed.Value!.Meta.Comment);
            Assert.Equal("Edited copyright", parsed.Value.Meta.Copyright);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
