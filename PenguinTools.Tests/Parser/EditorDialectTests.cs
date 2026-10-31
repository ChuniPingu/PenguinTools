using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class EditorDialectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtendedOutput_RejectsUnmigratedOriginalPrerollWithoutReplacingOutput(bool ugc)
    {
        var chart = await Parse("@FLAG\tSOFFSET\tTRUE\n@BEAT\t0\t3\t8\n#0'0:t04\n", ChartDialect.Original);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "existing output", ct);
            await Assert.ThrowsAsync<DiagnosticException>(async () =>
            {
                if (ugc)
                {
                    await new UgcChartWriter(path, chart, ChartDialect.Extended).WriteAsync(ct);
                }
                else
                {
                    await new MgxcChartWriter(new MgxcWriteRequest(path, chart, ChartDialect.Extended)).WriteAsync(ct);
                }
            });
            Assert.Equal("existing output", await File.ReadAllTextAsync(path, ct));
            Assert.Equal(ChartDialect.Original, chart.Extras.EncodingDialect);
            Assert.False(chart.Extras.FixedZeroOrigin);
            Assert.False(chart.Meta.BgmFixedZeroOrigin);
            Assert.True(chart.Meta.BgmEnableBarOffset);
            Assert.Equal(0, Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Tick.Original);
            var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(720, Assert.Single(converted.Value!.Notes).Tick.Original);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, ChartDialect.Original, false, 4, false)]
    [InlineData(true, ChartDialect.Original, false, 4, false)]
    [InlineData(false, ChartDialect.Original, true, 0, false)]
    [InlineData(true, ChartDialect.Original, true, 0, false)]
    [InlineData(false, ChartDialect.Original, true, 4, true)]
    [InlineData(true, ChartDialect.Original, true, 4, true)]
    [InlineData(false, ChartDialect.Auto, true, 4, false)]
    [InlineData(true, ChartDialect.Auto, true, 4, false)]
    public async Task ExtendedOutput_AllowsCoordinatesWithoutPendingOriginalPreroll(
        bool ugc, ChartDialect sourceDialect, bool blank, int numerator, bool fixedOrigin)
    {
        var chart = await Parse($"@FLAG\tSOFFSET\t{blank}\n@BEAT\t0\t{numerator}\t4\n#0'0:t04\n",
            ChartDialect.Original);
        chart.Extras.EncodingDialect = sourceDialect;
        chart.Extras.FixedZeroOrigin = fixedOrigin;
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True(ugc
                ? (await new UgcChartWriter(path, chart, ChartDialect.Extended).WriteAsync(ct)).Succeeded
                : (await new MgxcChartWriter(new MgxcWriteRequest(path, chart, ChartDialect.Extended)).WriteAsync(ct)).Succeeded);
            var parsed = ugc
                ? await new UgcParser(new UgcParseRequest(path, TestAssets.Load(), ChartDialect.Extended), TestMediaTool.Instance).ParseAsync(ct)
                : await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load(), ChartDialect.Extended), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(0, Assert.Single(converted.Value!.Notes).Tick.Original);
            Assert.Equal(blank, parsed.Value!.Meta.BgmEnableBarOffset);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWriters_RejectInvalidDialectWithoutReplacingOutput(bool ugc)
    {
        var chart = await Parse("#0'0:t04\n", ChartDialect.Original);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "existing output", ct);
            await Assert.ThrowsAsync<DiagnosticException>(async () =>
            {
                if (ugc)
                {
                    await new UgcChartWriter(path, chart, (ChartDialect)999).WriteAsync(ct);
                }
                else
                {
                    await new MgxcChartWriter(new MgxcWriteRequest(path, chart, (ChartDialect)999)).WriteAsync(ct);
                }
            });
            Assert.Equal("existing output", await File.ReadAllTextAsync(path, ct));
            Assert.Equal(ChartDialect.Original, chart.Extras.EncodingDialect);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task NativeWriters_RejectUnrepresentableMeterPositionsWithoutReplacingOutput(
        bool ugc, bool zero, bool representable)
    {
        var source = new PenguinTools.Chart.Models.c2s.Chart();
        source.Events.Add(new PenguinTools.Chart.Models.c2s.Bpm { Tick = 0, Value = 120 });
        source.Events.Add(new PenguinTools.Chart.Models.c2s.Met
        { Tick = 0, Numerator = zero ? 4 : 7, Denominator = zero ? 4 : 8 });
        if (zero)
        {
            source.Events.Add(new PenguinTools.Chart.Models.c2s.Met { Tick = 1920, Numerator = 0, Denominator = 4 });
        }
        source.Events.Add(new PenguinTools.Chart.Models.c2s.Met
        { Tick = zero ? representable ? 1920 : 3840 : representable ? 3360 : 2160, Numerator = 4, Denominator = 4 });
        var converted = new PenguinTools.Chart.Converter.ugc.UgcChartConverter(
            new PenguinTools.Chart.Converter.ugc.UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "existing output", ct);
            async Task Write()
            {
                Assert.True(ugc
                    ? (await new UgcChartWriter(path, converted.Value!, ChartDialect.Extended).WriteAsync(ct)).Succeeded
                    : (await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!, ChartDialect.Extended)).WriteAsync(ct)).Succeeded);
            }
            if (representable)
            {
                await Write();
                Assert.NotEqual("existing output", await File.ReadAllTextAsync(path, ct));
            }
            else
            {
                await Assert.ThrowsAsync<DiagnosticException>(Write);
                Assert.Equal("existing output", await File.ReadAllTextAsync(path, ct));
            }
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> Parse(string body, ChartDialect dialect = ChartDialect.Auto)
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120\n" + body,
                TestContext.Current.CancellationToken);
            var result = await new UgcParser(new UgcParseRequest(path, TestAssets.Load(), dialect), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, result.ToString());
            return result.Value!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(ChartDialect.Original, 1440)]
    [InlineData(ChartDialect.Extended, 3840)]
    public async Task MusicalAndFixedAxes_PositionNotesAndAllTimedEvents(ChartDialect dialect, int tick)
    {
        var chart = await Parse("@BEAT\t0\t3\t8\n@BEAT\t2\t4\t4\n@BPM\t2'0\t180\n" +
            "@TIL\t0\t0'0\t1\n@TIL\t0\t2'0\t2\n@SPDMOD\t2'0\t1.5\n#2'0:h04\n#240:s\n#2'0:c\n", dialect);
        var hold = Assert.Single(chart.Notes.Children.OfType<U.Hold>());
        Assert.Equal(tick, hold.Tick.Original);
        Assert.Equal(tick + 240, Assert.Single(hold.Children).Tick.Original);
        Assert.Contains(chart.Events.Children.OfType<U.BpmEvent>(), b => b.Bpm == 180 && b.Tick.Original == tick);
        Assert.Contains(chart.Events.Children.OfType<U.ScrollSpeedEvent>(), b => b.Timeline == 0 && b.Tick.Original == tick);
        Assert.Equal(tick, Assert.Single(chart.Events.Children.OfType<U.NoteSpeedEvent>()).Tick.Original);
        Assert.Equal([tick], chart.Extras.ClickTicks);
    }

    [Theory]
    [InlineData(false, 800)]
    [InlineData(true, 80)]
    public async Task NativeHeightPrecision_IsAppliedToBothEndpoints(bool highPrecision, int height)
    {
        var chart = await Parse($"@FLAG\tHIPRECISION\t{highPrecision}\n#0'0:C0428B,240\n#240:c0428\n");
        var crash = Assert.Single(chart.Notes.Children.OfType<U.AirCrash>());
        Assert.Equal(height, crash.Height);
        Assert.Equal(height, Assert.IsType<U.AirCrashJoint>(Assert.Single(crash.Children)).Height);
        Assert.Equal(Color.VLT, crash.Color);
    }

    [Theory]
    [InlineData('L', ExEffect.LS)]
    [InlineData('R', ExEffect.RS)]
    [InlineData('A', ExEffect.LC)]
    [InlineData('W', ExEffect.RC)]
    public async Task NativeEffects_KeepOriginalConverterMapping(char code, ExEffect effect)
    {
        var chart = await Parse($"#0'0:x04{code}\n");
        Assert.Equal(effect, Assert.Single(chart.Notes.Children.OfType<U.ExTap>()).Effect);
    }

    [Theory]
    [InlineData('B', Color.VLT)]
    [InlineData('Y', Color.PNK)]
    [InlineData('A', Color.PPL)]
    public async Task NativeCrashColors_StayDistinct(char code, Color color)
    {
        var chart = await Parse($"@FLAG\tHIPRECISION\tTRUE\n#0'0:C0428{code},240\n#240:c0428\n");
        Assert.Equal(color, Assert.Single(chart.Notes.Children.OfType<U.AirCrash>()).Color);
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task Writers_KeepDialectTimingEffectsAndUserText(ChartDialect dialect)
    {
        var chart = await Parse("@FLAG\tHIPRECISION\tTRUE\n@BEAT\t0\t3\t8\n@BEAT\t2\t4\t4\n#2'0:x04L\n", dialect);
        chart.Meta.Copyright = "Copyright author; literal text";
        chart.Meta.License = "MIT";
        chart.Meta.LicenseUrl = "https://example.org/license";
        chart.Meta.MgxcId = "chart_alpha";
        chart.Meta.ExVersion = 0;
        chart.Meta.ExLongEnabled = false;
        chart.Meta.WaitBgmEnd = true;
        var expectedTick = Assert.Single(chart.Notes.Children.OfType<U.ExTap>()).Tick.Original;
        var expectedEffect = Assert.Single(chart.Notes.Children.OfType<U.ExTap>()).Effect;
        var ugc = TestTempPaths.Create(".ugc");
        var mgxc = TestTempPaths.Create(".mgxc");
        try
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Assert.True((await new UgcChartWriter(ugc, chart, dialect).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var text = await File.ReadAllTextAsync(ugc, TestContext.Current.CancellationToken);
                Assert.Contains("'PenguinTools dialect: " + (dialect == ChartDialect.Original ? "original" : "extended"), text);
                Assert.DoesNotContain("'PenguinTools dialect: margrete-", text);
                var parsed = await new UgcParser(new UgcParseRequest(ugc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(parsed.Succeeded, parsed.ToString());
                chart = parsed.Value!;
                Assert.Equal(dialect, chart.Extras.EncodingDialect);
                Assert.Equal(expectedTick, Assert.Single(chart.Notes.Children.OfType<U.ExTap>()).Tick.Original);
                Assert.Equal(expectedEffect, Assert.Single(chart.Notes.Children.OfType<U.ExTap>()).Effect);
                Assert.Equal("Copyright author; literal text", chart.Meta.Copyright);
                Assert.Equal("MIT", chart.Meta.License);
                Assert.Equal("https://example.org/license", chart.Meta.LicenseUrl);
                Assert.Equal("chart_alpha", chart.Meta.MgxcId);
                Assert.Equal(0, chart.Meta.ExVersion);
                Assert.False(chart.Meta.ExLongEnabled);
                Assert.True(chart.Meta.WaitBgmEnd);
                Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxc, chart, dialect))
                    .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var binary = await new MgxcParser(new MgxcParseRequest(mgxc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(binary.Succeeded, binary.ToString());
                chart = binary.Value!;
                Assert.Equal(expectedEffect, Assert.Single(chart.Notes.Children.OfType<U.ExTap>()).Effect);
                Assert.Equal("Copyright author; literal text", chart.Meta.Copyright);
            }
        }
        finally
        {
            File.Delete(ugc);
            File.Delete(mgxc);
        }
    }

    [Theory]
    [InlineData(32767)]
    [InlineData(32768)]
    [InlineData(65535)]
    [InlineData(65536)]
    public async Task MgxcHeight_UsesComplete32BitField(int height)
    {
        var chart = await Parse("@FLAG\tHIPRECISION\tTRUE\n#0'0:C04280,240\n#240:c0428\n");
        var crash = Assert.Single(chart.Notes.Children.OfType<U.AirCrash>());
        crash.Height = height;
        Assert.IsType<U.AirCrashJoint>(Assert.Single(crash.Children)).Height = height;
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            await new MgxcChartWriter(new MgxcWriteRequest(path, chart, ChartDialect.Extended))
                .WriteAsync(TestContext.Current.CancellationToken);
            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(height, Assert.Single(parsed.Value!.Notes.Children.OfType<U.AirCrash>()).Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnsupportedLegacyOutput_FailsBeforeReplacingExistingFile()
    {
        var chart = await Parse("#0'0:s04\n#480:n44\n", ChartDialect.Extended);
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            await File.WriteAllTextAsync(path, "keep existing output", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<Core.Diagnostic.DiagnosticException>(async () =>
                await new MgxcChartWriter(new MgxcWriteRequest(path, chart, ChartDialect.Original))
                    .WriteAsync(TestContext.Current.CancellationToken));
            Assert.Equal("keep existing output", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task NativePreroll_FollowsSelectedEditorMode(ChartDialect dialect)
    {
        var chart = await Parse("@FLAG\tSOFFSET\tTRUE\n@BEAT\t0\t3\t8\n#0'0:t04\n", dialect);
        var result = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(dialect == ChartDialect.Original ? 720 : 0, Assert.Single(result.Value!.Notes).Tick.Original);
    }

    [Fact]
    public void PathPointsAtOneTick_KeepOrderAndSiblingLinks()
    {
        var root = new U.Note();
        var crash = new U.AirCrash { Tick = 0, Lane = 0, Width = 2, Height = 80 };
        var first = new U.AirCrashJoint { Tick = 480, Lane = 12, Width = 2, Height = 80 };
        var second = new U.AirCrashJoint { Tick = 480, Lane = 0, Width = 2, Height = 160 };
        crash.AppendChild(first);
        crash.AppendChild(second);
        root.AppendChild(crash);
        root.Sort();
        Assert.Equal([first, second], crash.Children);
        Assert.Same(second, first.NextSibling);
        Assert.Same(first, second.PreviousSibling);
    }

    [Fact]
    public async Task MainBpmOnly_IsAnInitialTempo()
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "@VER\t8\n@MAINBPM\t193\n#0'0:t04\n", TestContext.Current.CancellationToken);
            var result = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(193, result.Value!.Meta.BgmInitialBpm);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(ChartDialect.Original)]
    [InlineData(ChartDialect.Extended)]
    public async Task ChildTimeline_SurvivesUgcSave(ChartDialect dialect)
    {
        var chart = await Parse("@FLAG\tHIPRECISION\tTRUE\n@TIL\t0\t0'0\t1\n@TIL\t1\t0'0\t2\n#0'0:C04280,240\n@USETIL\t1\n#480:c4428\n",
            dialect);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken);
            Assert.Contains("#480>c4428", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            var result = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, result.ToString());
            var crash = Assert.Single(result.Value!.Notes.Children.OfType<U.AirCrash>());
            Assert.Equal(1, Assert.Single(crash.Children).Timeline);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(ChartDialect.Original, false)]
    [InlineData(ChartDialect.Extended, true)]
    public async Task AirLongHead_UsesNativeDialectSyntax(ChartDialect dialect, bool separateAir)
    {
        var chart = await Parse("#0'0:t04\n#0'0:H04N\n#480>s\n", dialect);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken);
            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(separateAir, text.Contains("#0'0:a04", StringComparison.Ordinal));
            Assert.Contains("#0'0:H04N", text);
            Assert.Contains("#480>s", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CoincidentCrashPoints_RequireMgxcOrExtendedUgc()
    {
        var chart = await Parse("@FLAG\tHIPRECISION\tTRUE\n#0'0:C04280,240\n#480>c4428\n#480>c8428\n",
            ChartDialect.Extended);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "keep existing output", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<Core.Diagnostic.DiagnosticException>(async () =>
                await new UgcChartWriter(path, chart, ChartDialect.Original).WriteAsync(TestContext.Current.CancellationToken));
            Assert.Equal("keep existing output", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.True((await new UgcChartWriter(path, chart, ChartDialect.Extended)
                .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnmarkedExTapHeight_IsNotAConverterRole()
    {
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            using var content = new MemoryStream();
            using var writer = new BinaryWriter(content);
            writer.Write("MGXC"u8);
            writer.Write(0);
            writer.Write(2);
            void Block(string name, Action<BinaryWriter> write)
            {
                using var data = new MemoryStream();
                using var body = new BinaryWriter(data);
                write(body);
                body.Flush();
                writer.Write(System.Text.Encoding.ASCII.GetBytes(name));
                writer.Write((int)data.Length);
                writer.Write(data.ToArray());
            }
            Block("meta", _ => { });
            Block("evnt", b =>
            {
                b.Write("bpm "u8);
                b.Write((short)1);
                b.Write((short)0);
                b.Write((short)3);
                b.Write((short)0);
                b.Write(120d);
                b.Write(0);
            });
            Block("dat2", b =>
            {
                void Note(byte type, byte attr, int tick, int height)
                {
                    b.Write(type);
                    b.Write(attr);
                    b.Write((byte)2);
                    b.Write((byte)0);
                    b.Write((byte)0);
                    b.Write((byte)0);
                    b.Write((short)4);
                    b.Write(height);
                    b.Write(tick);
                    b.Write(0);
                }
                Note(2, 0, 0, 81);
                Note(5, 1, 0, 80);
                Note(5, 5, 480, 80);
            });
            writer.Flush();
            content.Position = 4;
            writer.Write(checked((int)content.Length - 8));
            writer.Flush();
            await File.WriteAllBytesAsync(path, content.ToArray(), TestContext.Current.CancellationToken);
            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(U.ExTapRole.SharedLongCarrier, Assert.Single(parsed.Value!.Notes.Children.OfType<U.ExTap>()).Role);
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.DoesNotContain(converted.Value!.Notes, n => n is Chart.Models.c2s.ExTap);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NativeVersionStamp_WithUtf8Bom_SelectsFixedAxis()
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path,
                "\uFEFF' Created with Margrete v2.3.2\n@VER\t8\n@MAINBPM\t120\n@BEAT\t0\t3\t8\n#2'0:t04\n",
                TestContext.Current.CancellationToken);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(3840, Assert.Single(parsed.Value!.Notes.Children.OfType<U.Tap>()).Tick.Original);
            Assert.Equal(ChartDialect.Extended, parsed.Value.Extras.EncodingDialect);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("original", ChartDialect.Original, 1440)]
    [InlineData("extended", ChartDialect.Extended, 3840)]
    [InlineData("margrete-1.8.0", ChartDialect.Original, 1440)]
    [InlineData("margrete-2.3.2", ChartDialect.Extended, 3840)]
    public async Task FormatMarkers_OverrideEditorRelease(string marker, ChartDialect dialect, int tick)
    {
        var chart = await Parse($"' Created with Margrete v99.0.0\n'PenguinTools dialect: {marker}\n" +
            "@BEAT\t0\t3\t8\n#2'0:t04\n");
        Assert.Equal(dialect, chart.Extras.EncodingDialect);
        Assert.Equal(tick, Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Tick.Original);
    }

    [Theory]
    [InlineData("Auto", 0)]
    [InlineData("Original", 1)]
    [InlineData("Extended", 2)]
    [InlineData("Margrete180", 1)]
    [InlineData("Margrete232", 2)]
    public void FormatNames_KeepExistingSerializedValues(string name, int value)
    {
        Assert.True(Enum.TryParse<ChartDialect>(name, out var dialect));
        Assert.Equal(value, (int)dialect);
    }

    [Theory]
    [InlineData(ChartDialect.Original, 1440)]
    [InlineData(ChartDialect.Extended, 3840)]
    public async Task RetainedFormat_SurvivesRemovedMarkerAndChangedCreator(ChartDialect dialect, int tick)
    {
        var chart = await Parse("@BEAT\t0\t3\t8\n#2'0:t04\n", dialect);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            await File.WriteAllLinesAsync(path,
                lines.Where(l => !l.StartsWith("'PenguinTools dialect:", StringComparison.Ordinal))
                    .Prepend("' Created with Margrete v99.0.0"), TestContext.Current.CancellationToken);
            var result = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(dialect, result.Value!.Extras.EncodingDialect);
            Assert.Equal(tick, Assert.Single(result.Value.Notes.Children.OfType<U.Tap>()).Tick.Original);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("1.9.0")]
    [InlineData("2.4.0")]
    [InlineData("3.0.0")]
    public async Task UnknownCreator_RequiresFormatInsteadOfGuessing(string version)
    {
        var body = $"' Created with Margrete v{version}\n@BEAT\t0\t3\t8\n#2'0:t04\n";
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "@MAINBPM\t120\n" + body, TestContext.Current.CancellationToken);
            var result = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            foreach (var (dialect, tick) in new[] { (ChartDialect.Original, 1440), (ChartDialect.Extended, 3840) })
            {
                var chart = await Parse(body, dialect);
                Assert.Equal(tick, Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Tick.Original);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacySnapshotWithoutClockSpan_SelectsItsOriginalFixedAxis(bool compressed)
    {
        var snapshot = "HASH\tlegacy\nRESOLUTION\t384\n";
        var retained = C2SRoundTrip.Pack(snapshot, compressed);
        var chart = await Parse($"@COPYRIGHT\tMGR_CLKCNT=4;{retained}\n" +
            "@BEAT\t0\t3\t8\n#2'0:t\uE0004\n");
        Assert.Equal(ChartDialect.Extended, chart.Extras.EncodingDialect);
        var tap = Assert.Single(chart.Notes.Children.OfType<U.Tap>());
        Assert.Equal(3840, tap.Tick.Original);
        Assert.Equal(-1, tap.Lane);
    }

    [Fact]
    public async Task ClockCountAnnotationAlone_DoesNotEnableExtendedEncoding()
    {
        var chart = await Parse("@COPYRIGHT\tMGR_CLKCNT=3;User copyright\n@BEAT\t0\t3\t8\n#2'0:t04\n");
        Assert.Equal(ChartDialect.Original, chart.Extras.EncodingDialect);
        Assert.Equal(1440, Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Tick.Original);
        Assert.Equal("User copyright", chart.Meta.Copyright);
    }

    [Theory]
    [InlineData(null, ChartDialect.Auto, 3840)]
    [InlineData("1.8.0", ChartDialect.Auto, 1440)]
    [InlineData(null, ChartDialect.Original, 1440)]
    public async Task LegacyC2sImporterHeader_IdentifiesFixedAxisWithoutOverridingNativeCreator(
        string? creator, ChartDialect dialect, int tick)
    {
        var header = creator is null ? "" : $"' Created with Margrete v{creator}\n";
        var chart = await Parse(header + "@C2S_BASECOUNT\t1\n@BEAT\t0\t3\t8\n" +
            "#2'0:t04\n#2'0:C8428Y,240\n#240:cc428\n", dialect);
        Assert.Equal(tick, Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Tick.Original);
        Assert.Equal(Color.PNK, Assert.Single(chart.Notes.Children.OfType<U.AirCrash>()).Color);
        Assert.Equal(creator is null && dialect != ChartDialect.Original, chart.Meta.BgmFixedZeroOrigin);
    }

    [Theory]
    [InlineData(ChartDialect.Original, 4, 4, 4, true, 384)]
    [InlineData(ChartDialect.Extended, 4, 4, 4, true, 384)]
    [InlineData(ChartDialect.Original, 3, 8, 3, true, 144)]
    [InlineData(ChartDialect.Extended, 3, 8, 7, true, 336)]
    [InlineData(ChartDialect.Original, 3, 8, 0, true, 0)]
    [InlineData(ChartDialect.Extended, 3, 8, 3, false, 0)]
    [InlineData(ChartDialect.Original, 6, 8, -1, true, 288)]
    [InlineData(ChartDialect.Extended, 3, 8, -1, true, 144)]
    public async Task MetronomeCountSwitchAndExplicitClicks_SurviveBothContainers(
        ChartDialect dialect, int numerator, int denominator, int count, bool enabled, int span)
    {
        var countHeader = count < 0 ? "" : $"@CLKCNT\t{count}\n";
        var chart = await Parse($"@BEAT\t0\t{numerator}\t{denominator}\n" +
            $"@FLAG\tCLICK\t{enabled}\n{countHeader}#0'0:t04\n#0'240:c\n#1'0:c\n", dialect);
        var clicks = chart.Extras.ClickTicks.ToArray();
        var ugc = TestTempPaths.Create(".ugc");
        var mgxc = TestTempPaths.Create(".mgxc");
        var c2s = TestTempPaths.Create(".c2s");
        try
        {
            for (var cycle = 0; cycle < 2; cycle++)
            {
                Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxc, chart, dialect))
                    .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var binary = await new MgxcParser(new MgxcParseRequest(mgxc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(binary.Succeeded, binary.ToString());
                Assert.True((await new UgcChartWriter(ugc, binary.Value!, dialect)
                    .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var text = await new UgcParser(new UgcParseRequest(ugc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(text.Succeeded, text.ToString());
                chart = text.Value!;
                Assert.Equal(count < 0 ? numerator : count, chart.Extras.ClickCount);
                Assert.Equal(enabled, chart.Extras.ClickEnabled);
                Assert.Equal(clicks, chart.Extras.ClickTicks);
                var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
                Assert.True(converted.Succeeded, converted.ToString());
                Assert.True((await new PenguinTools.Chart.Writer.c2s.C2SChartWriter(
                        new PenguinTools.Chart.Writer.c2s.C2SWriteRequest(c2s, converted.Value!))
                    .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var lines = await File.ReadAllLinesAsync(c2s, TestContext.Current.CancellationToken);
                Assert.Contains($"CLK_DEF\t{span}", lines);
                Assert.Equal(2, lines.Count(line => line.StartsWith("CLK\t", StringComparison.Ordinal)));
                Assert.Contains("MUSIC\t0", lines);
                Assert.Contains("SEQUENCEID\t0", lines);
                Assert.Contains("DIFFICULT\t0", lines);
                Assert.Contains("LEVEL\t0.0", lines);
            }
        }
        finally
        {
            File.Delete(ugc);
            File.Delete(mgxc);
            File.Delete(c2s);
        }
    }
}
