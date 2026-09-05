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

public sealed class C2sJudgeRoundTripTests
{
    [Fact]
    public async Task C2sWriter_DoesNotReuseStaleJudgeSummaryAfterNoteEdit()
    {
        var source = new C2sChart();
        source.Meta.Id = 2999;
        source.Meta.Difficulty = Difficulty.Master;
        source.Meta.Level = 15.6m;
        source.Meta.Designer = "JudgeStats";
        source.Meta.MainBpm = 120m;
        source.Meta.BgmInitialBpm = 120m;
        source.Meta.BgmInitialDenominator = 4;
        source.Meta.BgmInitialNumerator = 4;

        // Simulate statistics inherited from the source C2S.
        source.Meta.C2sJudgeTap = 1;
        source.Meta.C2sJudgeHld = 0;
        source.Meta.C2sJudgeSld = 0;
        source.Meta.C2sJudgeAir = 0;
        source.Meta.C2sJudgeFlk = 0;
        source.Meta.C2sJudgeAll = 1;

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 120m
        });

        // Original chart had one TAP.
        source.Notes.Add(new Tap
        {
            Tick = 0,
            Lane = 0,
            Width = 2
        });

        // Simulate adding another TAP in Margrete.
        source.Notes.Add(new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 2
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "judge-stale.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var text = await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains("T_JUDGE_TAP\t2", text);
            Assert.Contains("T_JUDGE_HLD\t0", text);
            Assert.Contains("T_JUDGE_SLD\t0", text);
            Assert.Contains("T_JUDGE_AIR\t0", text);
            Assert.Contains("T_JUDGE_FLK\t0", text);
            Assert.Contains("T_JUDGE_ALL\t2", text);

            Assert.DoesNotContain("T_JUDGE_TAP\t1", text);
            Assert.DoesNotContain("T_JUDGE_ALL\t1", text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task C2sWriter_RecalculatesSimpleHoldJudgeCount()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 0,
                C2sJudgeSld = 0,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 1,
                C2sJudgeHldProxyBaseline = 0
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "simple-hold-judge.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t2",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_AirAtHoldEndReplacesHoldEndJudge()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 1,
                C2sJudgeSld = 0,
                C2sJudgeAir = 1,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 3,
                C2sJudgeHldProxyBaseline = 1
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        var hold = new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 240,
            EndLane = 4,
            EndWidth = 4
        };

        source.Notes.Add(hold);

        source.Notes.Add(new Air
        {
            Tick = 240,
            Lane = 4,
            Width = 4,
            Parent = hold
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "air-at-hold-end-judge.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t2",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_AppliesHoldProxyDeltaToSourceJudgeSummary()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 500,
                C2sJudgeSld = 0,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 501,
                C2sJudgeHldProxyBaseline = 1
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 1440,
            EndLane = 4,
            EndWidth = 4
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "hold-proxy-delta.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t505",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t506",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_PreservesSourceHoldJudgeWithoutProxyBaseline()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 206m,
                BgmInitialBpm = 206m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 500,
                C2sJudgeSld = 0,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 501
            }
        };

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 206m
        });

        source.Notes.Add(new Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 1440,
            EndLane = 4,
            EndWidth = 4
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "hold-without-proxy-baseline.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t500",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t501",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_RecalculatesFlickJudgeAfterNoteEdit()
    {
        var source = new C2sChart();
        source.Meta.Id = 1;
        source.Meta.Difficulty = Difficulty.Master;
        source.Meta.Level = 1m;
        source.Meta.Designer = "JudgeStats";
        source.Meta.MainBpm = 120m;
        source.Meta.BgmInitialBpm = 120m;
        source.Meta.BgmInitialDenominator = 4;
        source.Meta.BgmInitialNumerator = 4;

        // Deliberately stale source statistics.
        source.Meta.C2sJudgeTap = 99;
        source.Meta.C2sJudgeHld = 0;
        source.Meta.C2sJudgeSld = 0;
        source.Meta.C2sJudgeAir = 0;
        source.Meta.C2sJudgeFlk = 99;
        source.Meta.C2sJudgeAll = 198;

        source.Events.Add(new Bpm
        {
            Tick = 0,
            Value = 120m
        });

        source.Notes.Add(new Flick
        {
            Tick = 0,
            Lane = 4,
            Width = 2
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "judge-flick-edit.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var text = await File.ReadAllTextAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains("T_JUDGE_TAP\t0", text);
            Assert.Contains("T_JUDGE_HLD\t0", text);
            Assert.Contains("T_JUDGE_SLD\t0", text);
            Assert.Contains("T_JUDGE_AIR\t0", text);
            Assert.Contains("T_JUDGE_FLK\t1", text);
            Assert.Contains("T_JUDGE_ALL\t1", text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task C2sWriter_AppliesSlideProxyDeltaToSourceJudgeSummary()
    {
        var source = new C2sChart
        {
            Meta =
            {
                C2sJudgeTap = 1,
                C2sJudgeHld = 0,
                C2sJudgeSld = 1000,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 1001,
                C2sJudgeSldProxyBaseline = 5
            }
        };

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            EndTick = 480,
            EndLane = 4,
            EndWidth = 4,
            Joint = Joint.D
        });

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 8,
            Width = 4,
            EndTick = 480,
            EndLane = 12,
            EndWidth = 4,
            Joint = Joint.D
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "slide-proxy-delta.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t2",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t1005",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t1007",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_PreservesSourceSlideJudgeWithoutProxyBaseline()
    {
        var source = new C2sChart
        {
            Meta =
            {
                C2sJudgeTap = 1,
                C2sJudgeHld = 0,
                C2sJudgeSld = 1000,
                C2sJudgeAir = 0,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 1001
            }
        };

        source.Notes.Add(new Slide
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            EndTick = 480,
            EndLane = 8,
            EndWidth = 4,
            Joint = Joint.D
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "slide-without-proxy-baseline.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                "T_JUDGE_TAP\t1",
                lines);

            Assert.Contains(
                "T_JUDGE_HLD\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_SLD\t1000",
                lines);

            Assert.Contains(
                "T_JUDGE_AIR\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_FLK\t0",
                lines);

            Assert.Contains(
                "T_JUDGE_ALL\t1001",
                lines);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task MgxcWriter_WritesNeutralJudgeCopyright()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart
        {
            Meta =
            {
                Comment = "User comment",
                C2sJudgeTap = 3000,
                C2sJudgeHld = 500,
                C2sJudgeSld = 1000,
                C2sJudgeAir = 1000,
                C2sJudgeFlk = 216,
                C2sJudgeAll = 5716
            }
        };

        chart.Events.AppendChild(
            new PenguinTools.Chart.Models.umgr.BpmEvent
            {
                Tick = 0,
                Bpm = 120m
            });

        chart.Events.AppendChild(
            new PenguinTools.Chart.Models.umgr.BeatEvent
            {
                Tick = 0,
                Bar = 0,
                Numerator = 4,
                Denominator = 4
            });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "neutral-judge-copyright.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, chart))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var bytes = await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken);

            var text = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Contains(
                "GJ2:00000BB8000001F4000003E8000003E8000000D800001654;",
                text);

            Assert.Contains(
                "T_JUDGE_TAP=3000;HLD=500;SLD=1000;AIR=1000;FLK=216;ALL=5716",
                text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcJudgeSummary_PreservesCommentAndJudgeSummary()
    {
        var source = new C2sChart();

        source.Notes.Add(new Tap
        {
            Tick = 0,
            Lane = 4,
            Width = 2
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var chart = converted.Value!;

        chart.Meta.Comment = "Original comment text";
        chart.Meta.C2sJudgeTap = 1;
        chart.Meta.C2sJudgeHld = 2;
        chart.Meta.C2sJudgeSld = 3;
        chart.Meta.C2sJudgeAir = 4;
        chart.Meta.C2sJudgeFlk = 5;
        chart.Meta.C2sJudgeAll = 15;

        chart.Meta.C2sJudgeHldProxyBaseline = 11;
        chart.Meta.C2sJudgeSldProxyBaseline = 22;
        chart.Meta.C2sJudgeAirProxyBaseline = 33;

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "judge-summary.mgxc");

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
                "#meta c2sjudge",
                meta.Comment);

            Assert.Equal(1, meta.C2sJudgeTap);
            Assert.Equal(2, meta.C2sJudgeHld);
            Assert.Equal(3, meta.C2sJudgeSld);
            Assert.Equal(4, meta.C2sJudgeAir);
            Assert.Equal(5, meta.C2sJudgeFlk);
            Assert.Equal(15, meta.C2sJudgeAll);

            Assert.Equal(
                11,
                meta.C2sJudgeHldProxyBaseline);

            Assert.Equal(
                22,
                meta.C2sJudgeSldProxyBaseline);

            Assert.Equal(
                33,
                meta.C2sJudgeAirProxyBaseline);

            Assert.True(
                meta.TryGetC2sJudgeSummary(
                    out var tap,
                    out var hld,
                    out var sld,
                    out var air,
                    out var flk,
                    out var all));

            Assert.Equal(1, tap);
            Assert.Equal(2, hld);
            Assert.Equal(3, sld);
            Assert.Equal(4, air);
            Assert.Equal(5, flk);
            Assert.Equal(15, all);

            Assert.DoesNotContain(
                parsed.Value.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BookmarkEvent>(),
                bookmark => C2sRoundTripComment.IsRoundTripLine(bookmark.Tag));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcJudgeSummary_PreservesPartialProxyBaselines()
    {
        var source = new PenguinTools.Chart.Models.umgr.Chart
        {
            Meta =
            {
                C2sJudgeTap = 3000,
                C2sJudgeHld = 500,
                C2sJudgeSld = 1000,
                C2sJudgeAir = 1000,
                C2sJudgeFlk = 216,
                C2sJudgeAll = 5716,
                C2sJudgeHldProxyBaseline = 509,
                C2sJudgeSldProxyBaseline = 1005,
                C2sJudgeAirProxyBaseline = null
            }
        };

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "partial-proxy-baseline.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

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
                509,
                parsed.Value!.Meta.C2sJudgeHldProxyBaseline);

            Assert.Equal(
                1005,
                parsed.Value.Meta.C2sJudgeSldProxyBaseline);

            Assert.Null(
                parsed.Value.Meta.C2sJudgeAirProxyBaseline);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task C2sWriter_AppliesAirProxyDeltaToSourceJudgeSummary()
    {
        var source = new C2sChart
        {
            Meta =
            {
                MainBpm = 120m,
                BgmInitialBpm = 120m,
                C2sJudgeTap = 1,
                C2sJudgeHld = 0,
                C2sJudgeSld = 0,
                C2sJudgeAir = 10,
                C2sJudgeFlk = 0,
                C2sJudgeAll = 11,
                C2sJudgeAirProxyBaseline = 1
            }
        };

        var tap = new Tap
        {
            Tick = 0,
            Lane = 0,
            Width = 4
        };
        source.Notes.Add(tap);
        source.Notes.Add(new Air
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Parent = tap
        });
        source.Notes.Add(new Air
        {
            Tick = 480,
            Lane = 0,
            Width = 4,
            Parent = tap
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "air-proxy-delta.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Contains("T_JUDGE_AIR\t11", lines);
            Assert.Contains("T_JUDGE_ALL\t12", lines);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
