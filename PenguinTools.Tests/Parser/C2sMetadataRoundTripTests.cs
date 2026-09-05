using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Models.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Core.Metadata;
using PenguinTools.Core;
using System.Text;
using Xunit;
using C2sChart = PenguinTools.Chart.Models.c2s.Chart;

namespace PenguinTools.Tests.Parser;

public sealed class C2sMetadataRoundTripTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(13.0, "13")]
    [InlineData(15.4, "15")]
    [InlineData(15.5, "15+")]
    [InlineData(15.6, "15+")]
    [InlineData(8.5, "8+")]
    [InlineData(16.0, "16")]
    public void FormatPlayLevel_MatchesMagreteConvention(decimal level, string expected)
    {
        Assert.Equal(expected, MgxcChartWriter.FormatPlayLevel(level));
    }

    [Fact]
    public async Task MgxcWriter_WritesPlayLevelAndConstant()
    {
        var source = new C2sChart();
        source.Meta.Id = 2999;
        source.Meta.Title = "Melodiniq";
        source.Meta.Difficulty = Difficulty.Ultima;
        source.Meta.Level = 16.0m;
        source.Meta.Designer = "test";
        source.Meta.MainBpm = 193m;
        source.Notes.Add(new Tap { Tick = 0, Lane = 0, Width = 4 });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "plvl.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            var ascii = System.Text.Encoding.ASCII.GetString(bytes);
            Assert.Contains("plvl", ascii);
            Assert.Contains("cnst", ascii);
            // Play level "16" is written as a UTF-8 string field after plvl.
            var plvlIndex = ascii.IndexOf("plvl", StringComparison.Ordinal);
            Assert.True(plvlIndex >= 0);
            var plvlSlice = System.Text.Encoding.UTF8.GetString(bytes, plvlIndex, Math.Min(32, bytes.Length - plvlIndex));
            Assert.Contains("16", plvlSlice);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task C2sWriter_WritesHeaderFromMeta()
    {
        var source = new C2sChart();
        source.Meta.Id = 2999;
        source.Meta.Difficulty = Difficulty.Master;
        source.Meta.Level = 15.6m;
        source.Meta.Designer = "Memoir";
        source.Meta.MainBpm = 193m;
        source.Meta.BgmInitialDenominator = 4;
        source.Meta.BgmInitialNumerator = 4;
        source.Events.Add(new Bpm { Tick = 0, Value = 193m });
        source.Events.Add(new Bpm { Tick = 480, Value = 240m });
        source.Events.Add(new Bpm { Tick = 960, Value = 120.625m });
        source.Notes.Add(new Tap { Tick = 0, Lane = 0, Width = 4 });

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "header.c2s");
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var header = (await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken))
                .Take(12).ToArray();
            Assert.Equal("VERSION\t1.14.00\t1.14.00", header[0]);
            Assert.Equal("MUSIC\t2999", header[1]);
            Assert.Equal("DIFFICULT\t03", header[3]);
            Assert.Equal("LEVEL\t15.6", header[4]);
            Assert.Equal("CREATOR\tMemoir", header[5]);
            Assert.Equal("BPM_DEF\t193.000\t193.000\t240.000\t120.625", header[6]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_StoresLargeRoundTripMetadataInBookmarks()
    {
        var snapshot = new string('a', 40_000);

        var chart = new PenguinTools.Chart.Models.umgr.Chart
        {
            Meta =
            {
                Comment = "User comment",
                C2sJudgeTap = 1,
                C2sJudgeHld = 2,
                C2sJudgeSld = 3,
                C2sJudgeAir = 4,
                C2sJudgeFlk = 5,
                C2sJudgeAll = 15,
                C2sSlaSnapshot = snapshot
            }
        };

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "large-roundtrip-metadata.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var rawMgxc = Encoding.UTF8.GetString(
                await File.ReadAllBytesAsync(
                    path,
                    TestContext.Current.CancellationToken));

            Assert.Contains("#meta c2ssla " + snapshot, rawMgxc);

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(
                        path,
                        TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(
                parsed.Succeeded,
                parsed.ToString());

            Assert.Equal(
                "User comment",
                parsed.Value!.Meta.Comment);

            Assert.Equal(
                snapshot,
                parsed.Value.Meta.C2sSlaSnapshot);

            Assert.Equal(1, parsed.Value.Meta.C2sJudgeTap);
            Assert.Equal(2, parsed.Value.Meta.C2sJudgeHld);
            Assert.Equal(3, parsed.Value.Meta.C2sJudgeSld);
            Assert.Equal(4, parsed.Value.Meta.C2sJudgeAir);
            Assert.Equal(5, parsed.Value.Meta.C2sJudgeFlk);
            Assert.Equal(15, parsed.Value.Meta.C2sJudgeAll);

            Assert.DoesNotContain(
                parsed.Value.Events.Children
                    .OfType<PenguinTools.Chart.Models.umgr.BookmarkEvent>(),
                bookmark =>
                    C2sRoundTripComment.IsRoundTripLine(bookmark.Tag));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcMetadataSnapshots_PreserveWithoutJudgeSummary()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();

        chart.Meta.Comment = "Original comment text";
        chart.Meta.C2sMeterDefDenominator = 4;
        chart.Meta.C2sMeterDefNumerator = 4;
        chart.Meta.C2sSlpSnapshot = "0,0,480,1.25";
        chart.Meta.C2sSlaSnapshot = "0,1,4,2,480";

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "metadata-without-judge-summary.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var meta = parsed.Value!.Meta;

            Assert.Equal(
                "Original comment text",
                meta.Comment);
            Assert.DoesNotContain(
                "#meta c2ssla",
                meta.Comment);

            Assert.Equal(4, meta.C2sMeterDefDenominator);
            Assert.Equal(4, meta.C2sMeterDefNumerator);
            Assert.Equal("0,0,480,1.25", meta.C2sSlpSnapshot);
            Assert.Equal("0,1,4,2,480", meta.C2sSlaSnapshot);

            Assert.False(
                meta.TryGetC2sJudgeSummary(
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FormatBookmarks_EmitsOneBookmarkPerConverterTag()
    {
        var snapshot = new string('a', 40_000);
        var meta = new Meta
        {
            C2sJudgeTap = 1,
            C2sJudgeHld = 2,
            C2sJudgeSld = 3,
            C2sJudgeAir = 4,
            C2sJudgeFlk = 5,
            C2sJudgeAll = 15,
            C2sSlaSnapshot = snapshot
        };

        var bookmarks = C2sRoundTripComment.FormatBookmarks(meta);

        Assert.Equal(2, bookmarks.Count);
        Assert.Equal("#meta c2sjudge 1 2 3 4 5 15", bookmarks[0]);
        Assert.Equal("#meta c2ssla " + snapshot, bookmarks[1]);
    }

    [Fact]
    public async Task MgxcWriter_PreservesUserBookmarksAndUserMetaComment()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();
        chart.Meta.Comment = "#meta date 20260813\nChart notes";
        chart.Meta.C2sJudgeTap = 1;
        chart.Meta.C2sJudgeHld = 2;
        chart.Meta.C2sJudgeSld = 3;
        chart.Meta.C2sJudgeAir = 4;
        chart.Meta.C2sJudgeFlk = 5;
        chart.Meta.C2sJudgeAll = 15;

        chart.Events.AppendChild(new PenguinTools.Chart.Models.umgr.BookmarkEvent
        {
            Id = "95B9B99580D6425BBEEF17C820542F6C",
            Tick = 1920,
            Tag = "BOOKMARK AT BAR 2",
            Rgb = "FF00AA"
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "user-bookmarks.mgxc");

        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());

            var meta = parsed.Value!.Meta;
            Assert.Contains("#meta date 20260813", meta.Comment);
            Assert.Contains("Chart notes", meta.Comment);
            Assert.DoesNotContain("#meta c2sjudge", meta.Comment);
            Assert.Equal(1, meta.C2sJudgeTap);
            Assert.Equal(15, meta.C2sJudgeAll);

            var bookmark = Assert.Single(
                parsed.Value.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BookmarkEvent>());
            Assert.Equal("BOOKMARK AT BAR 2", bookmark.Tag);
            Assert.Equal(1920, bookmark.Tick.Original);
            Assert.Equal("FF00AA", bookmark.Rgb);
            Assert.Equal("95B9B99580D6425BBEEF17C820542F6C", bookmark.Id);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_DoesNotOverflowBookmarksOnDenseTilCharts()
    {
        var source = new C2sChart();
        source.Meta.MainBpm = 120m;

        for (var i = 0; i < 4000; i++)
        {
            source.Notes.Add(new Tap
            {
                Tick = i * 48,
                Lane = i % 16,
                Width = 1,
                Timeline = 1
            });
        }

        source.Notes.Add(new Sla
        {
            Tick = 0,
            Timeline = 1,
            Lane = 0,
            Width = 16,
            Length = 4000 * 48
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.NotNull(converted.Value!.Meta.C2sSlaEditKey);

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "dense-til.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            Assert.True(File.Exists(path));

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(converted.Value.Meta.C2sSlaEditKey, parsed.Value!.Meta.C2sSlaEditKey);
            Assert.Equal(converted.Value.Meta.C2sSlaSnapshot, parsed.Value.Meta.C2sSlaSnapshot);
            Assert.DoesNotContain("#meta c2s", parsed.Value.Meta.Comment);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
