using System.Buffers.Binary;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Metadata;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class OfficialEditorFormatTests
{
    [Theory]
    [InlineData(PenguinTools.Chart.EditorTarget.Legacy)]
    [InlineData(PenguinTools.Chart.EditorTarget.Standard)]
    [InlineData(PenguinTools.Chart.EditorTarget.Extended)]
    public async Task Mgxc_NonjudgingGroundTail_UsesControlWhileAirTailsUseEndNoAct(
        PenguinTools.Chart.EditorTarget target)
    {
        var chart = Chart();
        var ground = new U.Slide { Tick = 0, Lane = 0, Width = 2 };
        var tail = new U.SlideJoint { Tick = 480, Lane = 4, Width = 2, Joint = Joint.C };
        ground.AppendChild(tail);
        var air = new U.AirSlide { Height = 80 };
        air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 8, Width = 2, Height = 160, Joint = Joint.C });
        tail.MakePair(air);
        chart.Notes.AppendChild(ground);
        chart.Notes.AppendChild(air);
        var tap = new U.Tap { Tick = 1440, Lane = 0, Width = 2 };
        var hold = new U.AirHold();
        hold.AppendChild(new U.AirHoldJoint { Tick = 1920, Joint = Joint.C });
        tap.MakePair(hold);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(hold);
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new(path, chart, target)).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var data = bytes.AsSpan().IndexOf("dat2"u8) + 8;
            var attrs = new Dictionary<byte, byte>();
            for (var offset = data; offset < bytes.Length; offset += 20)
            {
                if (bytes[offset] is 6 or 8 or 9 && bytes[offset + 1] != 1)
                {
                    attrs[bytes[offset]] = bytes[offset + 1];
                }
            }
            Assert.Equal(3, attrs[6]);
            Assert.Equal(6, attrs[8]);
            Assert.Equal(6, attrs[9]);
            var restored = await Read(path);
            var restoredTail = Assert.IsType<U.SlideJoint>(Assert.Single(restored.Notes.Children.OfType<U.Slide>()).LastChild);
            Assert.Equal(Joint.C, restoredTail.Joint);
            Assert.Same(restoredTail, Assert.Single(restored.Notes.Children.OfType<U.AirSlide>()).PairNote);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ugc_ExplicitChrAndGeneratedCarrier_RetainSeparateRoles(bool longFirst)
    {
        var chart = Chart();
        var hold = new U.Hold { Tick = 0, Lane = 4, Width = 4, Effect = ExEffect.DW };
        hold.AppendChild(new U.HoldJoint { Tick = 960 });
        var chr = new U.ExTap { Tick = 0, Lane = 4, Width = 4, Effect = ExEffect.UP, Role = U.ExTapRole.Explicit };
        chart.Notes.AppendChild(longFirst ? hold : chr);
        chart.Notes.AppendChild(longFirst ? chr : hold);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var parsed = await Read(path);
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed)).Convert();
            Assert.True(converted.Succeeded);
            Assert.Equal(ExEffect.UP, Assert.Single(converted.Value!.Notes.OfType<C.ExTap>()).Effect);
            Assert.Equal(ExEffect.DW, Assert.Single(converted.Value.Notes.OfType<C.Hold>()).Effect);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(2, 8)]
    public async Task Ugc_NativeExTapCover_AppliesToLongHead(int lane, int width)
    {
        var chart = Chart();
        var hold = new U.Hold { Tick = 0, Lane = 4, Width = 4 };
        hold.AppendChild(new U.HoldJoint { Tick = 960 });
        chart.Notes.AppendChild(new U.ExTap { Tick = 0, Lane = lane, Width = width, Effect = ExEffect.DW, Role = U.ExTapRole.Explicit });
        chart.Notes.AppendChild(hold);
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(path, ct);
            await File.WriteAllLinesAsync(path, lines.Where(l => !l.StartsWith("@COPYRIGHT", StringComparison.Ordinal)), ct);
            var parsed = await Read(path);
            Assert.Equal(ExEffect.DW, Assert.Single(parsed.Notes.Children.OfType<U.Hold>()).Effect);
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed)).Convert();
            Assert.True(converted.Succeeded);
            Assert.Equal(ExEffect.DW, Assert.Single(converted.Value!.Notes.OfType<C.Hold>()).Effect);
            Assert.Equal(lane == 4 ? 0 : 1, converted.Value.Notes.OfType<C.ExTap>().Count());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Ugc_FractionalBarLength_PreservesAbsoluteTicksAndAirPair()
    {
        var chart = Chart();
        var beat = Assert.Single(chart.Events.Children.OfType<U.BeatEvent>());
        beat.Numerator = 1;
        beat.Denominator = 35;
        var slide = new U.Slide { Tick = 108, Lane = 0, Width = 2 };
        var tail = new U.SlideJoint { Tick = 324, Lane = 7, Width = 2, Joint = Joint.D };
        slide.AppendChild(tail);
        var air = new U.Air();
        tail.MakePair(air);
        chart.Notes.AppendChild(slide);
        chart.Notes.AppendChild(air);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var parsed = await Read(path);
            var result = Assert.Single(parsed.Notes.Children.OfType<U.Slide>());
            Assert.Equal(108, result.Tick.Original);
            Assert.Equal(324, result.LastChild!.Tick.Original);
            Assert.Same(result.LastChild, Assert.Single(parsed.Notes.Children.OfType<U.Air>()).PairNote);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Ugc_TraceMetadata_IsDiscardedAfterNativeEdit()
    {
        var chart = Chart();
        var crash = new U.AirCrash { Tick = 0, Lane = 0, Width = 4, Attr = AirLadderAttr.Trace };
        crash.AppendChild(new U.AirCrashJoint { Tick = 480, Lane = 4, Width = 4, Height = 20 });
        chart.Notes.AppendChild(crash);
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(ct)).Succeeded);
            Assert.Equal(AirLadderAttr.Trace, Assert.Single((await Read(path)).Notes.Children.OfType<U.AirCrash>()).Attr);
            var text = await File.ReadAllTextAsync(path, ct);
            await File.WriteAllTextAsync(path, text.Replace("#480>", "#960>", StringComparison.Ordinal), ct);
            Assert.Equal(AirLadderAttr.DEF, Assert.Single((await Read(path)).Notes.Children.OfType<U.AirCrash>()).Attr);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LargeMetadata_RoundTripsWithinBoundedLimit()
    {
        var text = new string('a', 4_100_000);
        Assert.Equal(text, PenguinTools.Chart.C2SRoundTrip.Decode(PenguinTools.Chart.C2SRoundTrip.Pack(text)));
        Assert.Throws<FormatException>(() => PenguinTools.Chart.C2SRoundTrip.Decode("MGR_C2S_LZ1=/wAAAQ==;"));
    }

    [Fact]
    public void MetadataExpansion_RejectsDataBeyondTheLimit()
    {
        var repeats = PenguinTools.Chart.C2SRoundTrip.MaxDecodedBytes / 258 + 1;
        var compressed = new byte[1 + repeats * 4];
        compressed[0] = (byte)'a';
        for (var i = 1; i < compressed.Length; i += 4)
        {
            compressed[i] = 255;
            compressed[i + 2] = 1;
            compressed[i + 3] = 255;
        }
        var metadata = "MGR_C2S_LZ1=" + Convert.ToBase64String(compressed) + ";";
        Assert.Throws<FormatException>(() => PenguinTools.Chart.C2SRoundTrip.Decode(metadata));
    }

    private static U.Chart Chart()
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        return chart;
    }

    [Theory]
    [InlineData(Difficulty.Basic, 0)]
    [InlineData(Difficulty.Advanced, 1)]
    [InlineData(Difficulty.Expert, 2)]
    [InlineData(Difficulty.Master, 3)]
    [InlineData(Difficulty.Ultima, 5)]
    [InlineData(Difficulty.WorldsEnd, 4)]
    public async Task Ugc_DifficultyHeaders_OnlyWorldsEndHasAnAttribute(Difficulty difficulty, int nativeValue)
    {
        var chart = Chart();
        chart.Meta.Difficulty = difficulty;
        chart.Meta.Level = 16;
        chart.Meta.WeDifficulty = StarDifficulty.S4;
        chart.Meta.WeTag = new(1, "改");
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(path, ct);
            Assert.Contains($"@DIFF\t{nativeValue}", lines);
            Assert.Contains($"@LEVEL\t{(difficulty == Difficulty.WorldsEnd ? "4" : "16")}", lines);
            if (difficulty == Difficulty.WorldsEnd)
            {
                Assert.Contains("@WEATTR\t改", lines);
            }
            else
            {
                Assert.DoesNotContain(lines, line => line.StartsWith("@WEATTR\t", StringComparison.Ordinal));
            }

            var parsed = await Read(path);
            Assert.Equal(difficulty, parsed.Meta.Difficulty);
            if (difficulty == Difficulty.WorldsEnd)
            {
                Assert.Equal(StarDifficulty.S4, parsed.Meta.WeDifficulty);
                Assert.Equal("改", parsed.Meta.WeTag.Str);
            }
            else
            {
                Assert.Equal(16, parsed.Meta.Level);
            }
        }
        finally { File.Delete(path); }
    }

    private static async Task<U.Chart> Read(string path)
    {
        var result = Path.GetExtension(path) == ".mgxc"
            ? await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(TestContext.Current.CancellationToken)
            : await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.ToString());
        return result.Value!;
    }

    [Theory]
    [InlineData(false, 65535)]
    [InlineData(true, 1295)]
    public async Task HighAirHeights_AreEncodedWithoutClamping(bool ugc, int maximum)
    {
        var chart = Chart();
        var crash = new U.AirCrash { Tick = 0, Lane = 0, Width = 4, Height = 361 };
        crash.AppendChild(new U.AirCrashJoint { Tick = 480, Lane = 4, Width = 4, Height = 780 });
        crash.AppendChild(new U.AirCrashJoint { Tick = 960, Lane = 8, Width = 4, Height = 800 });
        crash.AppendChild(new U.AirCrashJoint { Tick = 1440, Lane = 12, Width = 4, Height = maximum });
        chart.Notes.AppendChild(crash);
        var tap = new U.Tap { Tick = 1920, Lane = 0, Width = 4 };
        var air = new U.AirSlide { Height = 780 };
        tap.MakePair(air);
        air.AppendChild(new U.AirSlideJoint { Tick = 2400, Lane = 4, Width = 4, Height = 800 });
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = ugc ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                : await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            Assert.DoesNotContain(written.Diagnostics.Diagnostics, d => d.Message.Key.StartsWith("diag.warn.editor_", StringComparison.Ordinal));
            if (!ugc)
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                Assert.Equal([361, 780, 800, maximum], MgxcHeights(bytes, 0x0A));
                Assert.Equal([780, 800], MgxcHeights(bytes, 0x09));
            }
            else
            {
                var lines = await File.ReadAllLinesAsync(path, ct);
                Assert.Contains("#480>cK4LO", lines);
                Assert.Contains("#960>cO4M8", lines);
                Assert.Contains("#1440>cS4ZZ", lines);
            }
            var parsed = await Read(path);
            var actual = Assert.Single(parsed.Notes.Children.OfType<U.AirCrash>());
            Assert.Equal(361, actual.Height);
            Assert.Equal<decimal>([780, 800, maximum], actual.Children.Cast<U.AirCrashJoint>().Select(j => j.Height));
            var actualAir = Assert.Single(parsed.Notes.Children.OfType<U.AirSlide>());
            Assert.Equal(780, actualAir.Height);
            Assert.Equal(800, Assert.IsType<U.AirSlideJoint>(actualAir.LastChild).Height);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 65536)]
    [InlineData(true, -1)]
    [InlineData(true, 1296)]
    public async Task UnencodableAirHeight_RejectsExportWithoutReplacingExistingFile(bool ugc, int height)
    {
        var chart = Chart();
        var crash = new U.AirCrash { Tick = 480, Lane = 0, Width = 4, Height = 80 };
        crash.AppendChild(new U.AirCrashJoint { Tick = 960, Lane = 4, Width = 4, Height = height });
        chart.Notes.AppendChild(crash);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", ct);
            var written = ugc ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                : await new MgxcChartWriter(new MgxcWriteRequest(path, chart, PenguinTools.Chart.EditorTarget.Standard)).WriteAsync(ct);
            Assert.False(written.Succeeded);
            Assert.Contains(written.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.MgCrit_Unrepresentable_height);
            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, ct));
            Assert.Equal(height, Assert.IsType<U.AirCrashJoint>(crash.LastChild).Height);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(2000, true)]
    [InlineData(3000, false)]
    public async Task UgcHeightLimit_AppliesToSampledCurveInsteadOfItsHandle(int controlHeight, bool representable)
    {
        var chart = Chart();
        var tap = new U.Tap { Tick = 0, Lane = 4, Width = 4 };
        var air = new U.AirSlide { Height = 0 };
        tap.MakePair(air);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        air.AppendChild(new U.AirSlideJoint
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            Height = controlHeight,
            Joint = Joint.C,
            IsCurveControl = true
        });
        air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 4, Width = 4, Height = 0 });
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", ct);
            var written = await new UgcChartWriter(path, chart).WriteAsync(ct);
            Assert.Equal(representable, written.Succeeded);
            if (!representable)
            {
                Assert.Contains(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Unrepresentable_height);
                Assert.Equal("existing chart", await File.ReadAllTextAsync(path, ct));
                return;
            }
            // Check native geometry without the converter's restoration metadata.
            var lines = await File.ReadAllLinesAsync(path, ct);
            await File.WriteAllLinesAsync(path, lines.Where(l => !l.StartsWith("@COPYRIGHT", StringComparison.Ordinal)), ct);
            var parsed = Assert.Single((await Read(path)).Notes.Children.OfType<U.AirSlide>());
            var heights = parsed.Children.Cast<U.AirSlideJoint>().Select(n => n.Height).ToArray();
            Assert.All(heights, h => Assert.InRange(h, 0, 1295));
            Assert.InRange(heights.Max(), 990, 1000);
            Assert.Equal(controlHeight, Assert.IsType<U.AirSlideJoint>(air.FirstChild).Height);
        }
        finally { File.Delete(path); }
    }

    private static List<int> MgxcHeights(byte[] bytes, byte type)
    {
        var block = bytes.AsSpan().IndexOf("dat2"u8);
        var end = block + 8 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(block + 4));
        var heights = new List<int>();
        for (var offset = block + 8; offset < end; offset += bytes[offset] == 0x0A && bytes[offset + 1] == 1 ? 24 : 20)
        {
            if (bytes[offset] == type)
            {
                heights.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 8)));
            }
        }
        return heights;
    }

    [Fact]
    public async Task OfficialWireFormat_EncodesExtendedLanesNoLineAndCrushAxes()
    {
        var chart = Chart();
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = -16, Width = 16 });
        chart.Notes.AppendChild(new U.Tap { Tick = 240, Lane = 31, Width = 1 });
        var slide = new U.Slide { Tick = 480, Lane = 0, Width = 4, NoLine = true };
        slide.AppendChild(new U.SlideJoint { Tick = 960, Lane = 4, Width = 4 });
        chart.Notes.AppendChild(slide);
        foreach (var axis in new[] { AirLadderAttr.DEF, AirLadderAttr.AxisY, AirLadderAttr.AxisZ })
        {
            var crash = new U.AirCrash { Tick = 1920, Lane = 0, Width = 4, Height = 80, Attr = axis };
            crash.AppendChild(new U.AirCrashJoint { Tick = 2400, Lane = 4, Width = 4, Height = 160 });
            chart.Notes.AppendChild(crash);
        }
        var binary = TestTempPaths.Create(".mgxc");
        var text = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(binary, chart)).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(binary, ct);
            Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
            var data = bytes.AsSpan().IndexOf("dat2"u8) + 8;
            Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 16, 240, 80, 0, 0, 0 }, bytes[data..(data + 12)]);
            Assert.True((await new UgcChartWriter(text, chart).WriteAsync(ct)).Succeeded);
            var lines = await File.ReadAllLinesAsync(text, ct);
            Assert.Equal("@VER\t9", lines[0]);
            Assert.Contains("@FLAG\tEXFIELD\tTRUE", lines);
            Assert.Contains("#0'0:t0G", lines);
            Assert.Contains("#0'240:tl1", lines);
            Assert.Contains("#0'480:sG4Z", lines);
            foreach (var axis in new[] { 'N', 'Y', 'Z' })
            {
                Assert.Contains($"#1'0:CG4280{axis},0", lines);
            }

            foreach (var path in new[] { binary, text })
            {
                var parsed = await Read(path);
                Assert.True(parsed.Meta.ExtendedField);
                Assert.Equal(new[] { -16, 31 }, parsed.Notes.Children.OfType<U.Tap>().Select(n => n.Lane));
                Assert.True(Assert.Single(parsed.Notes.Children.OfType<U.Slide>()).NoLine);
                Assert.Equal(new[] { AirLadderAttr.DEF, AirLadderAttr.AxisY, AirLadderAttr.AxisZ },
                    parsed.Notes.Children.OfType<U.AirCrash>().Select(n => n.Attr));
            }
            Assert.False(chart.Meta.ExtendedField);
        }
        finally { File.Delete(binary); File.Delete(text); }
    }

    [Fact]
    public async Task VersionThree_UsesOfficialAttributesWithOldGeometryOffsets()
    {
        var chart = Chart();
        var slide = new U.Slide { Tick = 0, Lane = -2, Width = 4, NoLine = true };
        slide.AppendChild(new U.SlideJoint { Tick = 480, Lane = 2, Width = 4 });
        chart.Notes.AppendChild(slide);
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 3);
            var data = bytes.AsSpan().IndexOf("dat2"u8) + 8;
            for (var offset = data; offset < bytes.Length; offset += 20)
            {
                bytes[offset + 5] = bytes[offset + 7];
                bytes[offset + 7] = 0xAB;
                bytes[offset + 10] = 0xCD;
                bytes[offset + 11] = 0xEF;
            }
            await File.WriteAllBytesAsync(path, bytes, ct);
            var parsed = Assert.Single((await Read(path)).Notes.Children.OfType<U.Slide>());
            Assert.Equal((-2, 4, true), (parsed.Lane, parsed.Width, parsed.NoLine));
            Assert.Equal((2, 4), (parsed.LastChild!.Lane, parsed.LastChild.Width));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(1920, 240, -1, 13, 9, "18,42,73,111,157,215,287,380,507,699")]
    [InlineData(960, 480, 0, 12, 12, "39,81,126,174,225,278,337,403,476,563,676,898")]
    [InlineData(960, 240, 0, 12, 12, "20,44,71,103,138,179,228,286,356,447,577,869")]
    [InlineData(960, 720, 0, 12, 12, "58,118,181,245,311,377,446,520,596,679,776,927")]
    [InlineData(960, 480, 0, 6, 12, "76,156,236,316,396,476,556,636,716,796,876,956")]
    [InlineData(960, 480, 0, 2, 12, "168,291,390,476,553,622,687,747,803,857,909,958")]
    public async Task QuadraticCurve_MatchesNativeGoldenPointsAndPreservesEditableMgxc(
        int duration, int controlTick, int startLane, int controlLane, int endLane, string ticks)
    {
        var chart = Chart();
        var slide = new U.Slide { Tick = 0, Lane = startLane, Width = 4 };
        var handle = new U.SlideJoint { Tick = controlTick, Lane = controlLane, Width = 4, Joint = Joint.C, IsCurveControl = true };
        slide.AppendChild(handle);
        slide.AppendChild(new U.SlideJoint { Tick = duration, Lane = endLane, Width = 4 });
        chart.Notes.AppendChild(slide);
        chart.Meta.ExtendedField = startLane < 0;
        var binary = TestTempPaths.Create(".mgxc");
        var text = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(binary, chart)).WriteAsync(ct)).Succeeded);
            var editable = await Read(binary);
            var saved = Assert.Single(editable.Notes.Children.OfType<U.Slide>());
            Assert.True(Assert.IsType<U.SlideJoint>(saved.FirstChild).IsCurveControl);
            Assert.Equal(2, saved.Children.Count);
            Assert.True((await new UgcChartWriter(text, editable).WriteAsync(ct)).Succeeded);
            var flat = await Read(text);
            var points = Assert.Single(flat.Notes.Children.OfType<U.Slide>()).Children;
            var expectedTicks = ticks.Split(',').Select(int.Parse).Append(duration).ToArray();
            Assert.Equal(expectedTicks, points.Select(n => n.Tick.Original));
            Assert.Equal(Enumerable.Range(startLane + 1, expectedTicks.Length - 1).Append(endLane), points.Select(n => n.Lane));
            Assert.All(points, n => Assert.Equal(4, n.Width));
            var direct = new C2SChartConverter(new C2SConvertRequest(editable)).Convert();
            var exported = new C2SChartConverter(new C2SConvertRequest(flat)).Convert();
            Assert.True(direct.Succeeded);
            Assert.True(exported.Succeeded);
            static object Segments(C.Chart value) => value.Notes.OfType<C.Slide>()
                .Select(n => (n.Tick.Original, n.EndTick.Original, n.Lane, n.EndLane, n.Width, n.EndWidth, n.Joint)).ToArray();
            Assert.Equivalent(Segments(direct.Value!), Segments(exported.Value!));
            Assert.Equal(2, saved.Children.Count);
            Assert.Same(handle, slide.FirstChild);
            Assert.Same(slide, handle.Parent);
        }
        finally { File.Delete(binary); File.Delete(text); }
    }

    [Fact]
    public async Task CubicAirCurve_PreservesHeightWidthNoActionEndpointAndSourcePairing()
    {
        var chart = Chart();
        var tap = new U.Tap { Tick = 0, Lane = 0, Width = 2 };
        var air = new U.AirSlide { Height = 40, Color = Color.PNK };
        tap.MakePair(air);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        air.AppendChild(new U.AirSlideJoint { Tick = 320, Lane = 10, Width = 6, Height = 240, Joint = Joint.C, IsCurveControl = true });
        air.AppendChild(new U.AirSlideJoint { Tick = 640, Lane = 2, Width = 4, Height = 80, Joint = Joint.C, IsCurveControl = true });
        air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 8, Width = 4, Height = 160, Joint = Joint.C });
        var path = TestTempPaths.Create(".ugc");
        var mgxc = TestTempPaths.Create(".mgxc");
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxc, chart)).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var editable = await Read(mgxc);
            Assert.Equal(Color.PNK, Assert.Single(editable.Notes.Children.OfType<U.AirSlide>()).Color);
            Assert.True((await new UgcChartWriter(path, editable).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
            var parsed = await Read(path);
            var result = Assert.Single(parsed.Notes.Children.OfType<U.AirSlide>());
            var end = Assert.IsType<U.AirSlideJoint>(result.LastChild);
            Assert.Equal((960, 8, 4, 160m, Joint.C), (end.Tick.Original, end.Lane, end.Width, end.Height, end.Joint));
            Assert.Equal(new[] { (28, 1, 2, 56m), (958, 8, 4, 159m), (960, 8, 4, 160m) },
                result.Children.Cast<U.AirSlideJoint>().Select(n => (n.Tick.Original, n.Lane, n.Width, n.Height)));
            Assert.All(result.Children.OfType<U.AirSlideJoint>(), n => Assert.False(n.IsCurveControl));
            Assert.Equal(3, air.Children.Count);
            Assert.Same(tap, air.PairNote);
            Assert.Same(air, tap.PairNote);
        }
        finally { File.Delete(path); File.Delete(mgxc); }
    }

    [Fact]
    public async Task ClippedAirCurve_MatchesNativeHeightRoundingAndKeepsZeroWidthNodes()
    {
        var chart = Chart();
        var tap = new U.Tap { Tick = 0, Lane = 12, Width = 4 };
        var air = new U.AirSlide { Height = 0 };
        tap.MakePair(air);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        air.AppendChild(new U.AirSlideJoint { Tick = 480, Lane = 28, Width = 4, Height = 360, Joint = Joint.C, IsCurveControl = true });
        air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 12, Width = 4, Height = 0, Joint = Joint.C });
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new UgcChartWriter(path, chart).WriteAsync(ct);
            // Native 1.9.3 export: boundary clipping, five-unit height simplification,
            // truncation of output heights, and preservation of the original end.
            var expected = "#30>cT30L;#31>cT30N;#62>cU218;#99>cV11U;#101>cV11V;#138>cW02H;#146>cW02K;#155>cW02P;#166>cW02V;#176>cW02Z;#186>cW034;#197>cW039;#209>cW03E;#221>cW03J;#233>cW03O;#247>cW03T;#261>cW03Y;#276>cW043;#292>cW048;#310>cW04D;#331>cW04I;#354>cW04N;#382>cW04S;#423>cW04X;#537>cW04X;#578>cW04S;#606>cW04N;#630>cW04I;#650>cW04D;#668>cW048;#684>cW043;#699>cW03Y;#713>cW03T;#727>cW03O;#739>cW03J;#751>cW03E;#763>cW039;#774>cW034;#784>cW02Z;#795>cW02U;#805>cW02P;#814>cW02K;#857>cV11W;#859>cV11V;#894>cU219;#928>cT30N;#929>cT30M;#959>cS400;#960>cS400".Split(';');
            var lines = (await File.ReadAllLinesAsync(path, ct)).Where(l => l.StartsWith('#') && l.Contains('>')).ToArray();
            Assert.Equal(expected, lines);
            var parsed = await Read(path);
            var points = Assert.Single(parsed.Notes.Children.OfType<U.AirSlide>()).Children;
            Assert.Equal(expected.Length, points.Count);
            Assert.Contains(points, n => n.Width == 0);
            await new UgcChartWriter(path, parsed).WriteAsync(ct);
            Assert.Equal(expected, (await File.ReadAllLinesAsync(path, ct)).Where(l => l.StartsWith('#') && l.Contains('>')));
            Assert.True(new C2SChartConverter(new C2SConvertRequest(parsed)).Convert().Succeeded);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AdjacentCurves_PreserveTheirSharedJudgmentAndNativeTicks()
    {
        var chart = Chart();
        var slide = new U.Slide { Tick = 0, Lane = 0, Width = 4 };
        slide.AppendChild(new U.SlideJoint { Tick = 480, Lane = 6, Width = 4, Joint = Joint.C, IsCurveControl = true });
        slide.AppendChild(new U.SlideJoint { Tick = 960, Lane = 12, Width = 4, Joint = Joint.D });
        slide.AppendChild(new U.SlideJoint { Tick = 1440, Lane = 4, Width = 4, Joint = Joint.C, IsCurveControl = true });
        slide.AppendChild(new U.SlideJoint { Tick = 1920, Lane = 0, Width = 4, Joint = Joint.D });
        chart.Notes.AppendChild(slide);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken);
            var points = Assert.Single((await Read(path)).Notes.Children.OfType<U.Slide>()).Children.Cast<U.SlideJoint>().ToArray();
            Assert.Equal<int>([76, 156, 236, 316, 396, 476, 556, 636, 716, 796, 876, 956, 960, 1018, 1080, 1146, 1214, 1285, 1358, 1436, 1518, 1605, 1699, 1801, 1914, 1920], points.Select(n => n.Tick.Original));
            Assert.Equal<int>([960, 1920], points.Where(n => n.Joint == Joint.D).Select(n => n.Tick.Original));
            Assert.Equal(4, slide.Children.Count);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SlideLineNormalization_WarnsOnlyForOtherGeometryChanges(bool ugc, bool oversized)
    {
        var chart = Chart();
        var slide = new U.Slide { Tick = 0, Lane = 0, Width = 4, NoLine = true };
        slide.AppendChild(new U.SlideJoint { Tick = 480, Lane = 4, Width = oversized ? 20 : 4, Joint = Joint.C });
        slide.AppendChild(new U.SlideJoint { Tick = 960, Lane = 8, Width = 4 });
        chart.Notes.AppendChild(slide);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = ugc ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                : await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded);
            Assert.Equal(oversized ? 1 : 0,
                written.Diagnostics.Diagnostics.Count(d => d.Message.Key == MsgKeys.Warn_Editor_width_adjusted));
            var restored = Assert.Single((await Read(path)).Notes.Children.OfType<U.Slide>());
            Assert.True(restored.NoLine);
            var joint = Assert.IsType<U.SlideJoint>(restored.FirstChild);
            Assert.False(joint.NoLine);
            Assert.Equal(oversized ? 20 : 4, joint.Width);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approximation_OverlappingNotesRestoreInOrder(bool ugc)
    {
        var chart = Chart();
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = -16, Width = 16 });
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = -30, Width = 20 });
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var written = ugc ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                : await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded);
            var laneWarning = Assert.Single(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_lane_adjusted);
            Assert.Equal(-30, laneWarning.Message.Args!["arg2"]);
            Assert.Equal(-16, laneWarning.Message.Args["arg3"]);
            var widthWarning = Assert.Single(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_width_adjusted);
            Assert.Equal(20, widthWarning.Message.Args!["arg2"]);
            Assert.Equal(16, widthWarning.Message.Args["arg3"]);
            var parsed = await Read(path);
            Assert.Equal(new[] { (-16, 16), (-30, 20) }, parsed.Notes.Children.OfType<U.Tap>().Select(n => (n.Lane, n.Width)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Approximation_CurvesRetainHandlesOverlapsAndEndpointAirPairing()
    {
        var chart = Chart();
        for (var i = 0; i < 2; i++)
        {
            var slide = new U.Slide { Tick = 0, Lane = 0, Width = 4 };
            slide.AppendChild(new U.SlideJoint
            {
                Tick = 480,
                Lane = i == 0 ? 6 : 100,
                Width = i == 0 ? 4 : 20,
                Joint = Joint.C,
                IsCurveControl = true
            });
            var end = new U.SlideJoint { Tick = 960 + 480 * i, Lane = 12, Width = 4 };
            slide.AppendChild(end);
            chart.Notes.AppendChild(slide);
            if (i == 1)
            {
                var air = new U.Air();
                end.MakePair(air);
                chart.Notes.AppendChild(air);
            }
        }
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new UgcChartWriter(path, chart).WriteAsync(ct);
            Assert.True(written.Succeeded);
            Assert.Contains(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_width_adjusted);
            var parsed = await Read(path);
            var slides = parsed.Notes.Children.OfType<U.Slide>().ToArray();
            Assert.Equal<int>([2, 2], slides.Select(n => n.Children.Count));
            Assert.Equal(new[] { (6, 4), (100, 20) }, slides.Select(n => (n.FirstChild!.Lane, n.FirstChild.Width)));
            Assert.All(slides, n => Assert.True(Assert.IsType<U.SlideJoint>(n.FirstChild).IsCurveControl));
            var end = Assert.IsType<U.SlideJoint>(slides[1].LastChild);
            var air = Assert.Single(parsed.Notes.Children.OfType<U.Air>());
            Assert.Same(end, air.PairNote);
            Assert.Same(air, end.PairNote);
            var text = await File.ReadAllTextAsync(path, ct);
            await File.WriteAllTextAsync(path, text.Replace("#0'0:sG40", "#0'0:sH40", StringComparison.Ordinal), ct);
            var edited = await Read(path);
            Assert.All(edited.Notes.Children.OfType<U.Slide>().SelectMany(n => n.Children).Cast<U.SlideJoint>(),
                n => Assert.False(n.IsCurveControl));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Approximation_OverlappingCrushPathsRestoreInOrder()
    {
        var chart = Chart();
        for (var i = 0; i < 2; i++)
        {
            var crash = new U.AirCrash { Tick = 0, Lane = 0, Width = 4, Height = 80 };
            for (var j = 0; j <= i; j++)
            {
                crash.AppendChild(new U.AirCrashJoint { Tick = 480, Lane = 4 + i + j, Width = 4, Height = 80 });
            }
            chart.Notes.AppendChild(crash);
        }
        var path = TestTempPaths.Create(".ugc");
        try
        {
            var written = await new UgcChartWriter(path, chart).WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded);
            Assert.Contains(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_crash_points_merged);
            var crashes = (await Read(path)).Notes.Children.OfType<U.AirCrash>().ToArray();
            Assert.Equal<int>([1, 2], crashes.Select(n => n.Children.Count));
            Assert.Equal<int>([4, 5, 6], crashes.SelectMany(n => n.Children).Select(n => n.Lane));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ZeroMeter_NativeOutputKeepsBarsAndAbsoluteNoteAndEventTimes()
    {
        var chart = Chart();
        chart.Events.AppendChild(new U.BeatEvent { Bar = 1, Tick = 1920, Numerator = 0, Denominator = 4 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 3, Tick = 1920, Numerator = 3, Denominator = 8 });
        chart.Events.AppendChild(new U.BpmEvent { Tick = 2640, Bpm = 180 });
        chart.Notes.AppendChild(new U.Tap { Tick = 2640, Lane = 4, Width = 4 });
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new UgcChartWriter(path, chart).WriteAsync(ct);
            Assert.True(written.Succeeded);
            Assert.DoesNotContain(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_meter_skipped);
            var lines = await File.ReadAllLinesAsync(path, ct);
            Assert.Contains("@BEAT\t1\t0\t4", lines);
            Assert.Contains("@BEAT\t3\t3\t8", lines);
            Assert.Contains("@BPM\t4'0\t180", lines);
            Assert.Contains("#4'0:tK4", lines);
            var retained = await Read(path);
            Assert.Contains(retained.Events.Children.OfType<U.BeatEvent>(), b => b.Numerator == 0);
            await File.WriteAllLinesAsync(path, lines.Where(l => !l.StartsWith("@COPYRIGHT", StringComparison.Ordinal)), ct);
            var native = await Read(path);
            Assert.Contains(native.Events.Children.OfType<U.BeatEvent>(), b => b.Numerator == 0);
            Assert.Equal(2640, Assert.Single(native.Notes.Children.OfType<U.Tap>()).Tick.Original);
            Assert.Contains(native.Events.Children.OfType<U.BpmEvent>(), b => b.Tick.Original == 2640 && b.Bpm == 180);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApproximationMetadata_IsInvalidatedByAnEditorChange(bool ugc)
    {
        var chart = Chart();
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = -30, Width = 20 });
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = ugc ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                : await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            Assert.Contains(written.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Editor_width_adjusted);
            var retained = Assert.Single((await Read(path)).Notes.Children.OfType<U.Tap>());
            Assert.Equal((-30, 20), (retained.Lane, retained.Width));
            if (ugc)
            {
                var text = await File.ReadAllTextAsync(path, ct);
                await File.WriteAllTextAsync(path, text.Replace("#0'0:t0G", "#0'0:t1G", StringComparison.Ordinal), ct);
            }
            else
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                var offset = bytes.AsSpan().IndexOf("dat2"u8) + 8;
                bytes[offset + 7] = unchecked((byte)-15);
                await File.WriteAllBytesAsync(path, bytes, ct);
            }
            var edited = Assert.Single((await Read(path)).Notes.Children.OfType<U.Tap>());
            Assert.Equal((-15, 16), (edited.Lane, edited.Width));
            Assert.Equal((-30, 20), (chart.Notes.FirstChild!.Lane, chart.Notes.FirstChild.Width));
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
    public async Task AirHoldControls_WarnOnlyAboutIntermediateControlsInUgc(bool ugc, bool intermediateControl, bool openEnd)
    {
        var chart = Chart();
        var tap = new U.Tap { Tick = 0, Lane = 4, Width = 4 };
        var hold = new U.AirHold { Color = Color.PNK };
        var first = intermediateControl ? Joint.C : Joint.D;
        var last = openEnd ? Joint.C : Joint.D;
        hold.AppendChild(new U.AirHoldJoint { Tick = 480, Joint = first });
        hold.AppendChild(new U.AirHoldJoint { Tick = 960, Joint = last });
        tap.MakePair(hold);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(hold);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var written = ugc ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                    : await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
                Assert.True(written.Succeeded, written.ToString());
                Assert.Equal(ugc && intermediateControl ? 1 : 0,
                    written.Diagnostics.Diagnostics.Count(d => d.Message.Key == MsgKeys.Warn_Ugc_air_hold_controls));
                chart = await Read(path);
                var retained = Assert.Single(chart.Notes.Children.OfType<U.AirHold>());
                Assert.Equal<Joint>([first, last], retained.Children.Cast<U.AirHoldJoint>().Select(j => j.Joint));
                Assert.Equal<int>([480, 960], retained.Children.Select(j => j.Tick.Original));
                Assert.Equal(Color.PNK, retained.Color);
                Assert.Same(Assert.Single(chart.Notes.Children.OfType<U.Tap>()), retained.PairNote);
            }
            Assert.Equal<Joint>([first, last], hold.Children.Cast<U.AirHoldJoint>().Select(j => j.Joint));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedColor_WarnsAboutColorWithoutReportingUnchangedGeometry(bool ugc)
    {
        var chart = Chart();
        var tap = new U.Tap { Tick = 0, Lane = 4, Width = 4 };
        var air = new U.Air { Color = Color.RED };
        tap.MakePair(air);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var written = ugc ? await new UgcChartWriter(path, chart).WriteAsync(ct)
                : await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded);
            var warning = Assert.Single(written.Diagnostics.Diagnostics,
                d => d.Message.Key.StartsWith("diag.warn.editor_", StringComparison.Ordinal));
            Assert.Equal(MsgKeys.Warn_Editor_color_defaulted, warning.Message.Key);
            Assert.Equal("AIR", warning.Message.Args!["arg1"]);
            Assert.Equal("RED", warning.Message.Args["arg2"]);
            Assert.Equal(Color.RED, air.Color);
            Assert.Equal(Color.RED, Assert.Single((await Read(path)).Notes.Children.OfType<U.Air>()).Color);
        }
        finally { File.Delete(path); }
    }

}
