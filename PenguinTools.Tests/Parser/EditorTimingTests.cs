using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class EditorTimingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficialOutput_PreservesOriginalPrerollWithoutMutatingSource(bool ugc)
    {
        var chart = await Parse("@FLAG\tSOFFSET\tTRUE\n@BEAT\t0\t3\t8\n#0'0:t04\n");
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "existing output", ct);
            {
                if (ugc)
                {
                    await new UgcChartWriter(path, chart).WriteAsync(ct);
                }
                else
                {
                    await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
                }
            }
            Assert.NotEqual("existing output", await File.ReadAllTextAsync(path, ct));
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
    [InlineData(false, false, 4, false)]
    [InlineData(true, false, 4, false)]
    [InlineData(false, true, 0, false)]
    [InlineData(true, true, 0, false)]
    [InlineData(false, true, 4, true)]
    [InlineData(true, true, 4, true)]
    [InlineData(false, true, 4, false)]
    [InlineData(true, true, 4, false)]
    public async Task OfficialOutput_PreservesSourceOrigin(
        bool ugc, bool blank, int numerator, bool fixedOrigin)
    {
        var chart = await Parse($"@FLAG\tSOFFSET\t{blank}\n@BEAT\t0\t{numerator}\t4\n#0'0:t04\n");
        chart.Extras.FixedZeroOrigin = fixedOrigin;
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True(ugc
                ? (await new UgcChartWriter(path, chart).WriteAsync(ct)).Succeeded
                : (await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            var parsed = ugc
                ? await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                : await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(blank && !fixedOrigin ? numerator * 480 : 0, Assert.Single(converted.Value!.Notes).Tick.Original);
            Assert.Equal(blank, parsed.Value!.Meta.BgmEnableBarOffset);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWriters_RejectDanglingCurveWithoutReplacingOutput(bool ugc)
    {
        var chart = await Parse("#0'0:t04\n");
        var slide = new U.Slide { Tick = 0, Lane = 0, Width = 4 };
        slide.AppendChild(new U.SlideJoint { Tick = 240, Lane = 4, Width = 4, Joint = Joint.C, IsCurveControl = true });
        chart.Notes.AppendChild(slide);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "existing output", ct);
            await Assert.ThrowsAsync<TimedDiagnosticException>(async () =>
            {
                if (ugc)
                {
                    await new UgcChartWriter(path, chart).WriteAsync(ct);
                }
                else
                {
                    await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
                }
            });
            Assert.Equal("existing output", await File.ReadAllTextAsync(path, ct));
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
    public async Task NativeWriters_WarnAndApproximateUnrepresentableMeters(
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
            var written = ugc
                ? await new UgcChartWriter(path, converted.Value!).WriteAsync(ct)
                : await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded);
            Assert.Equal(!representable,
                written.Diagnostics.Diagnostics.Any(d => d.Message.Key == MsgKeys.Warn_Editor_meter_skipped));
            Assert.NotEqual("existing output", await File.ReadAllTextAsync(path, ct));
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> Parse(string body)
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "@VER\t8\n@TICKS\t480\n@BPM\t0'0\t120\n" + body,
                TestContext.Current.CancellationToken);
            var result = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
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
    [InlineData(1440)]
    public async Task MusicalBars_PositionNotesAndAllTimedEvents(int tick)
    {
        var chart = await Parse("@BEAT\t0\t3\t8\n@BEAT\t2\t4\t4\n@BPM\t2'0\t180\n" +
            "@TIL\t0\t0'0\t1\n@TIL\t0\t2'0\t2\n@SPDMOD\t2'0\t1.5\n#2'0:h04\n#240:s\n#2'0:c\n");
        var hold = Assert.Single(chart.Notes.Children.OfType<U.Hold>());
        Assert.Equal(tick, hold.Tick.Original);
        Assert.Equal(tick + 240, Assert.Single(hold.Children).Tick.Original);
        Assert.Contains(chart.Events.Children.OfType<U.BpmEvent>(), b => b.Bpm == 180 && b.Tick.Original == tick);
        Assert.Contains(chart.Events.Children.OfType<U.ScrollSpeedEvent>(), b => b.Timeline == 0 && b.Tick.Original == tick);
        Assert.Equal(tick, Assert.Single(chart.Events.Children.OfType<U.NoteSpeedEvent>()).Tick.Original);
        Assert.Equal([tick], chart.Extras.ClickTicks);
    }

    [Theory]
    [InlineData(false, 80)]
    [InlineData(true, 80)]
    public async Task NativeHeightPrecision_IsAppliedToBothEndpoints(bool highPrecision, int height)
    {
        var heightCode = highPrecision ? "28" : "8";
        var chart = await Parse($"@FLAG\tHIPRECISION\t{highPrecision}\n#0'0:C04{heightCode}B,240\n#240:c04{heightCode}\n");
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

    [Fact]
    public async Task OfficialWriters_KeepTimingEffectsAndUserText()
    {
        var chart = await Parse("@FLAG\tHIPRECISION\tTRUE\n@BEAT\t0\t3\t8\n@BEAT\t2\t4\t4\n#2'0:x04L\n");
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
            for (var cycle = 0; cycle < 2; cycle++)
            {
                Assert.True((await new UgcChartWriter(ugc, chart).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var text = await File.ReadAllTextAsync(ugc, TestContext.Current.CancellationToken);
                Assert.Contains("@VER\t9", text);
                var parsed = await new UgcParser(new UgcParseRequest(ugc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(parsed.Succeeded, parsed.ToString());
                chart = parsed.Value!;
                Assert.Equal(expectedTick, Assert.Single(chart.Notes.Children.OfType<U.ExTap>()).Tick.Original);
                Assert.Equal(expectedEffect, Assert.Single(chart.Notes.Children.OfType<U.ExTap>()).Effect);
                Assert.Equal("Copyright author; literal text", chart.Meta.Copyright);
                Assert.Equal("MIT", chart.Meta.License);
                Assert.Equal("https://example.org/license", chart.Meta.LicenseUrl);
                Assert.Equal("chart_alpha", chart.Meta.MgxcId);
                Assert.Equal(0, chart.Meta.ExVersion);
                Assert.False(chart.Meta.ExLongEnabled);
                Assert.True(chart.Meta.WaitBgmEnd);
                Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxc, chart))
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
    public async Task MgxcHeight_RetainsUnsignedRangeAndRejectsOverflow(int height)
    {
        var chart = await Parse("@FLAG\tHIPRECISION\tTRUE\n#0'0:C04280,240\n#240:c0428\n");
        var crash = Assert.Single(chart.Notes.Children.OfType<U.AirCrash>());
        crash.Height = height;
        Assert.IsType<U.AirCrashJoint>(Assert.Single(crash.Children)).Height = height;
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, chart, PenguinTools.Chart.EditorTarget.Standard))
                .WriteAsync(TestContext.Current.CancellationToken);
            if (height > ushort.MaxValue)
            {
                Assert.False(written.Succeeded);
                Assert.False(File.Exists(path));
                return;
            }
            Assert.True(written.Succeeded, written.ToString());
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
    public async Task NativePreroll_FollowsInitialMeter()
    {
        var chart = await Parse("@FLAG\tSOFFSET\tTRUE\n@BEAT\t0\t3\t8\n#0'0:t04\n");
        var result = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(720, Assert.Single(result.Value!.Notes).Tick.Original);
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

    [Fact]
    public async Task ChildTimeline_SurvivesUgcSave()
    {
        var chart = await Parse("@FLAG\tHIPRECISION\tTRUE\n@TIL\t0\t0'0\t1\n@TIL\t1\t0'0\t2\n#0'0:C04280,240\n@USETIL\t1\n#480:c4428\n");
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken);
            Assert.Contains("#480>cK428", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
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
    [InlineData(false)]
    public async Task AirLongHead_UsesOfficialSyntax(bool separateAir)
    {
        var chart = await Parse("#0'0:t04\n#0'0:H04N\n#480>s\n");
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken);
            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(separateAir, text.Contains("#0'0:aG4", StringComparison.Ordinal));
            Assert.Contains("#0'0:HG4N", text);
            Assert.Contains("#480>s", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CoincidentCrashPoints_AreApproximatedWithWarning()
    {
        var chart = await Parse("@FLAG\tHIPRECISION\tTRUE\n#0'0:C04280,240\n#480>c4428\n#480>c8428\n");
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "keep existing output", TestContext.Current.CancellationToken);
            var written = await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded);
            Assert.Contains(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_crash_points_merged);
            Assert.Single(await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken),
                line => line.StartsWith("#480>", StringComparison.Ordinal));
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, Assert.Single(parsed.Value!.Notes.Children.OfType<U.AirCrash>()).Children.Count);
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

    [Theory]
    [InlineData(1440)]
    public async Task OfficialVersion_SurvivesChangedCreator(int tick)
    {
        var chart = await Parse("@BEAT\t0\t3\t8\n#2'0:t04\n");
        var path = TestTempPaths.Create(".ugc");
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            await File.WriteAllLinesAsync(path,
                lines.Prepend("' Created with Margrete v99.0.0"), TestContext.Current.CancellationToken);
            var result = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(tick, Assert.Single(result.Value.Notes.Children.OfType<U.Tap>()).Tick.Original);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(4, 4, 4, true, 384)]
    [InlineData(3, 8, 3, true, 144)]
    [InlineData(3, 8, 0, true, 0)]
    [InlineData(6, 8, -1, true, 288)]
    public async Task MetronomeCountSwitchAndExplicitClicks_SurviveBothContainers(
        int numerator, int denominator, int count, bool enabled, int span)
    {
        var countHeader = count < 0 ? "" : $"@CLKCNT\t{count}\n";
        var chart = await Parse($"@BEAT\t0\t{numerator}\t{denominator}\n" +
            $"@FLAG\tCLICK\t{enabled}\n{countHeader}#0'0:t04\n#0'240:c\n#1'0:c\n");
        var clicks = chart.Extras.ClickTicks.ToArray();
        var ugc = TestTempPaths.Create(".ugc");
        var mgxc = TestTempPaths.Create(".mgxc");
        var c2s = TestTempPaths.Create(".c2s");
        try
        {
            for (var cycle = 0; cycle < 2; cycle++)
            {
                Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxc, chart))
                    .WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
                var binary = await new MgxcParser(new MgxcParseRequest(mgxc, TestAssets.Load()), TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);
                Assert.True(binary.Succeeded, binary.ToString());
                Assert.True((await new UgcChartWriter(ugc, binary.Value!)
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
