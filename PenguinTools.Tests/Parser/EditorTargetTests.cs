using System.Buffers.Binary;
using System.Text;
using PenguinTools.Chart;
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

public sealed class EditorTargetTests
{
    [Theory]
    [InlineData(EditorTarget.Legacy, ".mgxc")]
    [InlineData(EditorTarget.Standard, ".mgxc")]
    [InlineData(EditorTarget.Extended, ".mgxc")]
    [InlineData(EditorTarget.Legacy, ".ugc")]
    [InlineData(EditorTarget.Standard, ".ugc")]
    [InlineData(EditorTarget.Extended, ".ugc")]
    public async Task InteriorSlideAir_IsRejectedWithoutReplacingOutput(EditorTarget target, string extension)
    {
        var source = new PenguinTools.Chart.Models.c2s.Chart();
        var paired = new PenguinTools.Chart.Models.c2s.Slide
        { Tick = 0, Lane = 0, Width = 4, EndTick = 960, EndLane = 4, EndWidth = 4 };
        source.Notes.AddRange([
            paired,
            new PenguinTools.Chart.Models.c2s.Slide
                { Tick = 480, Lane = 8, Width = 4, EndTick = 960, EndLane = 4, EndWidth = 4 },
            new PenguinTools.Chart.Models.c2s.Slide
                { Tick = 960, Lane = 4, Width = 4, EndTick = 1440, EndLane = 12, EndWidth = 4 },
            new PenguinTools.Chart.Models.c2s.Air
                { Tick = 960, Lane = 4, Width = 4, Parent = paired, Direction = AirDirection.IR }
        ]);
        var converted = new UgcChartConverter(new(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var chart = converted.Value!;
        var air = Assert.Single(chart.Notes.Children.OfType<U.Air>());
        var parent = Assert.IsType<U.SlideJoint>(air.PairNote);
        Assert.Equal(0, parent.Parent!.Tick.Original);
        Assert.NotSame(parent.Parent.LastChild, parent);
        var path = TestTempPaths.Create(extension);
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", Ct);
            var result = extension == ".mgxc"
                ? await new MgxcChartWriter(new(path, chart, target)).WriteAsync(Ct)
                : await new UgcChartWriter(path, chart, target).WriteAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Editor_interior_air);
            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, Ct));
            Assert.Same(parent, air.PairNote);
            Assert.Equal(1440, parent.Parent.LastChild!.Tick.Original);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public async Task ExtendedUgcCopyright_StillRespectsNativeTextCapacity(int bytes, bool supported)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        chart.Extras.FixedZeroOrigin = true;
        chart.Meta.Copyright = new string('中', bytes / 3) + new string('A', bytes % 3);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", Ct);
            var result = await new UgcChartWriter(path, chart, EditorTarget.Extended).WriteAsync(Ct);
            Assert.Equal(supported, result.Succeeded);
            if (supported)
            {
                Assert.Equal(chart.Meta.Copyright, (await Read(path)).Meta.Copyright);
            }
            else
            {
                Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Ugc_metadata_too_long);
                Assert.Equal("existing chart", await File.ReadAllTextAsync(path, Ct));
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy)]
    [InlineData(EditorTarget.Standard)]
    [InlineData(EditorTarget.Extended)]
    public async Task MgxcNativeText_UsesUnsignedLengthAndRejectsOverflow(EditorTarget target)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        chart.Extras.FixedZeroOrigin = true;
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            foreach (var length in new[] { 32768, 65535 })
            {
                var text = new string('中', length / 3) + new string('A', length % 3);
                chart.Meta.Title = text;
                chart.Meta.Copyright = text;
                Assert.True((await new MgxcChartWriter(new(path, chart, target)).WriteAsync(Ct)).Succeeded);
                var restored = await Read(path);
                Assert.Equal(text, restored.Meta.Title);
                Assert.Equal(text, restored.Meta.Copyright);
                Assert.True(restored.Extras.FixedZeroOrigin);
            }

            chart.Meta.Title += "A";
            await File.WriteAllTextAsync(path, "existing chart", Ct);
            var result = await new MgxcChartWriter(new(path, chart, target)).WriteAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Mgxc_string_too_long);
            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, Ct));
            Assert.Equal(65536, Encoding.UTF8.GetByteCount(chart.Meta.Title));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy)]
    [InlineData(EditorTarget.Standard)]
    [InlineData(EditorTarget.Extended)]
    public async Task LargeRoundTripPayload_RequiresAnEditorWithBookmarkMigration(EditorTarget target)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        var bytes = new byte[65536];
        new Random(512).NextBytes(bytes);
        chart.Extras.SourceSnapshot = "RESOLUTION\t384\nSOURCE\t" + Convert.ToBase64String(bytes);
        var snapshot = chart.Extras.SourceSnapshot;
        var text = TestTempPaths.Create(".ugc");
        var binary = TestTempPaths.Create(".mgxc");
        try
        {
            await File.WriteAllTextAsync(text, "existing chart", Ct);
            var result = await new UgcChartWriter(text, chart, target).WriteAsync(Ct);
            if (target == EditorTarget.Extended)
            {
                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal(snapshot, (await Read(text)).Extras.SourceSnapshot);
            }
            else
            {
                Assert.False(result.Succeeded);
                Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Ugc_metadata_too_long);
                Assert.Equal("existing chart", await File.ReadAllTextAsync(text, Ct));
            }
            // All targets can retain this payload in the wider MGXC bookmark field.
            Assert.True((await new MgxcChartWriter(new(binary, chart, target)).WriteAsync(Ct)).Succeeded);
            Assert.Equal(snapshot, (await Read(binary)).Extras.SourceSnapshot);
            Assert.Equal(snapshot, chart.Extras.SourceSnapshot);
        }
        finally { File.Delete(text); File.Delete(binary); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy)]
    [InlineData(EditorTarget.Standard)]
    public async Task UgcMetadataLimit_CountsUtf8BytesIncludingTimingMarkers(EditorTarget target)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        var path = TestTempPaths.Create(".ugc");
        try
        {
            Assert.True((await new UgcChartWriter(path, chart, target).WriteAsync(Ct)).Succeeded);
            var header = (await File.ReadAllLinesAsync(path, Ct)).Single(l => l.StartsWith("@COPYRIGHT\t", StringComparison.Ordinal));
            var remaining = ushort.MaxValue - Encoding.UTF8.GetByteCount(header[11..]);
            chart.Meta.Copyright = new string('中', remaining / 3) + new string('A', remaining % 3);
            Assert.True((await new UgcChartWriter(path, chart, target).WriteAsync(Ct)).Succeeded);
            header = (await File.ReadAllLinesAsync(path, Ct)).Single(l => l.StartsWith("@COPYRIGHT\t", StringComparison.Ordinal));
            Assert.Equal(ushort.MaxValue, Encoding.UTF8.GetByteCount(header[11..]));
            chart.Meta.Copyright += "中";
            await File.WriteAllTextAsync(path, "existing chart", Ct);
            var result = await new UgcChartWriter(path, chart, target).WriteAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Ugc_metadata_too_long);
            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, Ct));
        }
        finally { File.Delete(path); }
    }

    private const string Source = """
        @VER	8
        @TICKS	480
        @TITLE	互换测试
        @DESIGN	Test
        @MAINBPM	120
        @FLAG	SOFFSET	FALSE
        @FLAG	HIPRECISION	TRUE
        @CLKCNT	3
        @BEAT	0	3	4
        @BEAT	3	5	8
        @BPM	2'240	180
        @TIL	0	1'0	1.5
        #0'0:t44
        #0'0:a44ULI
        #0'480:h84
        #960>s
        #1'480:s44
        #480>s84
        #960>s44
        #2'480:C44280,240
        #480>c8440
        #960>c4428
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Records(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim()).Order(StringComparer.Ordinal).ToArray();

    private static async Task<U.Chart> Read(string path)
    {
        var result = path.EndsWith(".ugc", StringComparison.Ordinal)
            ? await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct)
            : await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
        Assert.True(result.Succeeded, result.ToString());
        Assert.DoesNotContain(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Unrecognized_note);
        return result.Value!;
    }

    private static async Task<string> WriteC2s(string path, U.Chart chart)
    {
        var converted = new C2SChartConverter(new(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var result = await new C2SChartWriter(new(path, converted.Value!)).WriteAsync(Ct);
        Assert.True(result.Succeeded, result.ToString());
        return await File.ReadAllTextAsync(path, Ct);
    }

    [Theory]
    [InlineData(EditorTarget.Legacy, false)]
    [InlineData(EditorTarget.Standard, false)]
    [InlineData(EditorTarget.Extended, false)]
    [InlineData(EditorTarget.Legacy, true)]
    [InlineData(EditorTarget.Standard, true)]
    [InlineData(EditorTarget.Extended, true)]
    public async Task CommonChart_PreservesC2sAcrossThreeCycles(EditorTarget target, bool preroll)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.ugc");
            var effects = new[] { 'U', 'D', 'C', 'A', 'W', 'L', 'R', 'I' };
            var textSource = preroll ? Source.Replace("SOFFSET\tFALSE", "SOFFSET\tTRUE", StringComparison.Ordinal) : Source;
            await File.WriteAllTextAsync(source, textSource + "\n" + string.Join('\n', effects.Select((e, i) => $"#4'{i * 120}:x44{e}")), Ct);
            var chart = await Read(source);
            var c2sPath = Path.Combine(dir, "result.c2s");
            var expected = await WriteC2s(c2sPath, chart);
            for (var round = 0; round < 3; round++)
            {
                var mgxc = Path.Combine(dir, $"round-{round}.mgxc");
                var ugc = Path.Combine(dir, $"round-{round}.ugc");
                var binary = await new MgxcChartWriter(new(mgxc, chart, target)).WriteAsync(Ct);
                Assert.True(binary.Succeeded, binary.ToString());
                var bytes = await File.ReadAllBytesAsync(mgxc, Ct);
                Assert.Equal(target == EditorTarget.Legacy ? 2 : 4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
                chart = await Read(mgxc);
                Assert.Equal(Records(expected), Records(await WriteC2s(c2sPath, chart)));
                var text = await new UgcChartWriter(ugc, chart, target).WriteAsync(Ct);
                Assert.True(text.Succeeded, text.ToString());
                Assert.StartsWith(target == EditorTarget.Legacy ? "@VER\t8" : "@VER\t9", await File.ReadAllTextAsync(ugc, Ct));
                chart = await Read(ugc);
                Assert.Equal("互换测试", chart.Meta.Title);
                Assert.Equal(Records(expected), Records(await WriteC2s(c2sPath, chart)));
                var parsed = await new C2SParser(new(c2sPath)).ParseAsync(Ct);
                Assert.True(parsed.Succeeded, parsed.ToString());
                var reverse = new UgcChartConverter(new(parsed.Value!)).Convert();
                Assert.True(reverse.Succeeded, reverse.ToString());
                chart = reverse.Value!;
                // C2S has no song-title field. Restore the test title for the next editor cycle.
                chart.Meta.Title = "互换测试";
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy, false)]
    [InlineData(EditorTarget.Standard, false)]
    [InlineData(EditorTarget.Extended, false)]
    [InlineData(EditorTarget.Legacy, true)]
    [InlineData(EditorTarget.Standard, true)]
    [InlineData(EditorTarget.Extended, true)]
    public async Task OpeningMeasureMigration_PreservesAudioDelayAndMarkerUnits(EditorTarget target, bool ugc)
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = true;
        chart.Meta.BgmManualOffset = .25m;
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 3, Denominator = 4 });
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BreakingMarker { Tick = 2 });
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = 0, Width = 4 });
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        try
        {
            for (var round = 0; round < 3; round++)
            {
                var result = ugc ? await new UgcChartWriter(path, chart, target).WriteAsync(Ct)
                    : await new MgxcChartWriter(new(path, chart, target)).WriteAsync(Ct);
                Assert.True(result.Succeeded, result.ToString());
                chart = await Read(path);
                Assert.Equal(1.75m, chart.Meta.BgmRealOffset);
                Assert.Equal(target == EditorTarget.Legacy ? 0 : 1440,
                    Assert.Single(chart.Notes.Children.OfType<U.Tap>()).Tick.Original);
                Assert.Equal(target == EditorTarget.Legacy ? 2 : 3,
                    Assert.Single(chart.Events.Children.OfType<U.BreakingMarker>()).Tick.Original);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy, false, "meter")]
    [InlineData(EditorTarget.Standard, true, "meter")]
    [InlineData(EditorTarget.Legacy, true, "lane")]
    [InlineData(EditorTarget.Legacy, false, "click")]
    [InlineData(EditorTarget.Standard, false, "click")]
    [InlineData(EditorTarget.Legacy, true, "axis")]
    [InlineData(EditorTarget.Legacy, true, "control-tail")]
    [InlineData(EditorTarget.Standard, true, "control-tail")]
    public async Task UnsupportedTargetFeature_LeavesSourceAndDestinationUntouched(EditorTarget target, bool ugc, string feature)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        try
        {
            await File.WriteAllTextAsync(input, Source, Ct);
            var chart = await Read(input);
            switch (feature)
            {
                case "meter":
                    chart.Events.Children.OfType<U.BeatEvent>().First().Numerator = 0;
                    break;
                case "lane":
                    chart.Notes.Children.OfType<U.Tap>().First().Lane = -1;
                    break;
                case "click":
                    chart.Extras.ClickEnabled = true;
                    chart.Extras.ClickCount = 7;
                    break;
                case "axis":
                    chart.Notes.Children.OfType<U.AirCrash>().First().Attr = AirLadderAttr.AxisZ;
                    break;
                case "control-tail":
                    ((U.SlideJoint)chart.Notes.Children.OfType<U.Slide>().First().LastChild!).Joint = Joint.C;
                    break;
            }
            await File.WriteAllTextAsync(output, "existing destination", Ct);
            var result = ugc ? await new UgcChartWriter(output, chart, target).WriteAsync(Ct)
                : await new MgxcChartWriter(new(output, chart, target)).WriteAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Editor_target_feature);
            Assert.Equal("existing destination", await File.ReadAllTextAsync(output, Ct));
            Assert.Equal(Source, await File.ReadAllTextAsync(input, Ct));
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy, Color.DEF)]
    [InlineData(EditorTarget.Legacy, Color.PNK)]
    [InlineData(EditorTarget.Standard, Color.DEF)]
    [InlineData(EditorTarget.Standard, Color.PNK)]
    [InlineData(EditorTarget.Extended, Color.DEF)]
    [InlineData(EditorTarget.Extended, Color.PNK)]
    public async Task AirHoldColor_IsReadableAtBothNativeLegacyAndCurrentOffsets(EditorTarget target, Color color)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        var tap = new U.Tap { Tick = 480, Lane = 0, Width = 4 };
        var hold = new U.AirHold { Tick = 480, Lane = 0, Width = 4, Color = color };
        hold.AppendChild(new U.AirHoldJoint { Tick = 960, Joint = Joint.D });
        hold.AppendChild(new U.AirHoldJoint { Tick = 1440, Joint = Joint.C });
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(hold);
        tap.MakePair(hold);
        var path = TestTempPaths.Create(".ugc");
        try
        {
            var written = await new UgcChartWriter(path, chart, target).WriteAsync(Ct);
            Assert.True(written.Succeeded, written.ToString());
            var text = await File.ReadAllTextAsync(path, Ct);
            var payload = Assert.Single(text.Split('\n'), l => l.Contains(":H", StringComparison.Ordinal))
                .Split(':')[1].Trim();
            Assert.Equal(color == Color.PNK ? 'I' : 'N', payload[3]);
            if (target == EditorTarget.Legacy && color == Color.PNK)
            {
                Assert.Equal('I', payload[4]);
            }
            else
            {
                Assert.Equal(4, payload.Length);
            }
            var parsed = await Read(path);
            var actual = Assert.Single(parsed.Notes.Children.OfType<U.AirHold>());
            Assert.Equal(color, actual.Color);
            Assert.IsType<U.Tap>(actual.PairNote);
            Assert.Equal(2, actual.Children.Count);
            Assert.Equal(Joint.C, Assert.IsType<U.AirHoldJoint>(actual.LastChild).Joint);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("sG40", "c80", "sK4", true, EditorTarget.Standard)]
    [InlineData("SG428N", "c8028", "sK428", true, EditorTarget.Standard)]
    [InlineData("sG40", "c80", "sK4", false, EditorTarget.Extended)]
    [InlineData("SG428N", "c8028", "sK428", false, EditorTarget.Extended)]
    [InlineData("sG40", "c80", "sK4", true, EditorTarget.Extended)]
    [InlineData("SG428N", "c8028", "sK428", true, EditorTarget.Extended)]
    public async Task ClippedControls_RetainZeroWidthAcrossThreeC2sCycles(
        string head, string control, string end, bool ugc, EditorTarget target)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var c2s = TestTempPaths.Create(".c2s");
        try
        {
            await File.WriteAllTextAsync(input,
                $"@VER\t9\n@FLAG\tHIPRECISION\tTRUE\n@FLAG\tSOFFSET\tFALSE\n" +
                $"@FLAG\tEXFIELD\tTRUE\n@TICKS\t480\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n" +
                $"#1'0:tG4\n#1'0:{head}\n#480>{control}\n#960>{end}\n", Ct);
            var chart = await Read(input);
            var expected = await WriteC2s(c2s, chart);
            for (var round = 0; round < 3; round++)
            {
                var parsed = await new C2SParser(new(c2s)).ParseAsync(Ct);
                Assert.True(parsed.Succeeded, parsed.ToString());
                var reverse = new UgcChartConverter(new(parsed.Value!)).Convert();
                Assert.True(reverse.Succeeded, reverse.ToString());
                var written = ugc ? await new UgcChartWriter(output, reverse.Value!, target).WriteAsync(Ct)
                    : await new MgxcChartWriter(new(output, reverse.Value!, target)).WriteAsync(Ct);
                Assert.True(written.Succeeded, written.ToString());
                Assert.Equal(ugc && target == EditorTarget.Standard ? 1 : 0,
                    written.Diagnostics.Diagnostics.Count(d => d.Message.Key == MsgKeys.Warn_Editor_clipped_control_save));
                var reloaded = await Read(output);
                var root = Assert.Single(reloaded.Notes.Children, n => n is U.Slide or U.AirSlide);
                Assert.Equal(2, root.Children.Count);
                Assert.Equal(960, root.LastChild!.Tick.Original - root.Tick.Original);
                Assert.Equal(0, root.FirstChild!.Width);
                Assert.Equal(-8, root.FirstChild.Lane);
                Assert.Equal(4, root.LastChild!.Width);
                Assert.Equal(Records(expected), Records(await WriteC2s(c2s, reloaded)));
            }
        }
        finally { File.Delete(input); File.Delete(output); File.Delete(c2s); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy, false, Joint.C)]
    [InlineData(EditorTarget.Standard, false, Joint.C)]
    [InlineData(EditorTarget.Legacy, false, Joint.D)]
    [InlineData(EditorTarget.Standard, false, Joint.D)]
    [InlineData(EditorTarget.Extended, false, Joint.D)]
    [InlineData(EditorTarget.Legacy, true, Joint.D)]
    [InlineData(EditorTarget.Standard, true, Joint.D)]
    [InlineData(EditorTarget.Extended, true, Joint.D)]
    public async Task UnsupportedZeroWidth_LeavesSourceAndDestinationUntouched(EditorTarget target, bool ugc, Joint kind)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        var slide = new U.Slide { Width = 4 };
        slide.AppendChild(new U.SlideJoint { Tick = 480, Width = 0, Lane = 4, Joint = kind });
        chart.Notes.AppendChild(slide);
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        try
        {
            await File.WriteAllTextAsync(path, "existing chart", Ct);
            var result = ugc ? await new UgcChartWriter(path, chart, target).WriteAsync(Ct)
                : await new MgxcChartWriter(new(path, chart, target)).WriteAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key ==
                (kind == Joint.C ? MsgKeys.MgCrit_Editor_target_feature : MsgKeys.MgCrit_Invalid_note_width));
            Assert.Equal("existing chart", await File.ReadAllTextAsync(path, Ct));
            Assert.Equal(0, slide.FirstChild!.Width);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy, false, false)]
    [InlineData(EditorTarget.Legacy, true, false)]
    [InlineData(EditorTarget.Standard, false, false)]
    [InlineData(EditorTarget.Standard, true, false)]
    [InlineData(EditorTarget.Legacy, false, true)]
    [InlineData(EditorTarget.Legacy, true, true)]
    [InlineData(EditorTarget.Standard, false, true)]
    [InlineData(EditorTarget.Standard, true, true)]
    [InlineData(EditorTarget.Extended, false, true)]
    [InlineData(EditorTarget.Extended, true, true)]
    public async Task CoincidentControls_PreserveGeometryOrRejectBeforeReplacing(
        EditorTarget target, bool air, bool ugc)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var c2s = TestTempPaths.Create(".c2s");
        try
        {
            var body = air
                ? "#0'0:t04\n#0'0:S0428N\n#480>c4428\n#480>c422A\n#960>s0428"
                : "#0'0:s04\n#480>c44\n#480>c82\n#960>s04";
            await File.WriteAllTextAsync(input,
                "@VER\t8\n@TICKS\t480\n@FLAG\tHIPRECISION\tTRUE\n@FLAG\tSOFFSET\tFALSE\n" +
                "@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n" + body, Ct);
            var chart = await Read(input);
            var expected = await WriteC2s(c2s, chart);
            await File.WriteAllTextAsync(output, "existing chart", Ct);
            var result = ugc ? await new UgcChartWriter(output, chart, target).WriteAsync(Ct)
                : await new MgxcChartWriter(new(output, chart, target)).WriteAsync(Ct);
            if (ugc && target != EditorTarget.Extended)
            {
                Assert.False(result.Succeeded);
                Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Editor_target_feature);
                Assert.Equal("existing chart", await File.ReadAllTextAsync(output, Ct));
            }
            else
            {
                Assert.True(result.Succeeded, result.ToString());
                var reloaded = await Read(output);
                var path = Assert.Single(reloaded.Notes.Children, n => n is U.Slide or U.AirSlide);
                Assert.Equal(3, path.Children.Count);
                Assert.Equal(path.Children[0].Tick.Original, path.Children[1].Tick.Original);
                Assert.Equal(Records(expected), Records(await WriteC2s(c2s, reloaded)));
            }
            var originalPath = Assert.Single(chart.Notes.Children, n => n is U.Slide or U.AirSlide);
            Assert.Equal(3, originalPath.Children.Count);
            Assert.Equal(originalPath.Children[0].Tick.Original, originalPath.Children[1].Tick.Original);
        }
        finally { File.Delete(input); File.Delete(output); File.Delete(c2s); }
    }
}
