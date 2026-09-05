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

public sealed class C2sFormatCompatibilityTests
{
    [Fact]
    public async Task ZeroNumeratorMet_IsSkippedAndMgxcStillWrites()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "zero-met.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            MET	0	0	4	4
            MET	1	0	4	0
            BPM	0	0	120.000
            SLP	1	0	96	0.500000	0
            TAP	0	0	0	4
            TAP	1	48	0	4
            """, TestContext.Current.CancellationToken);

        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        Assert.DoesNotContain(parsed.Value!.Events.OfType<Met>(), met => met.Numerator <= 0 || met.Denominator <= 0);
        Assert.Contains(parsed.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Key == MsgKeys.C2s_Invalid_field);

        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.DoesNotContain(converted.Value!.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BeatEvent>(),
            beat => beat.Numerator <= 0 || beat.Denominator <= 0);

        var outPath = Path.Combine(directory, "zero-met.mgxc");
        var written = await new MgxcChartWriter(new MgxcWriteRequest(outPath, converted.Value))
            .WriteAsync(TestContext.Current.CancellationToken);
        Assert.True(written.Succeeded, written.ToString());

        var reparsed = await new MgxcParser(new MgxcParseRequest(outPath, TestAssets.Load()), TestMediaTool.Instance)
            .ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(reparsed.Succeeded, reparsed.ToString());
        Assert.Contains(reparsed.Value!.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BeatEvent>(),
            beat => beat is { Bar: 0, Numerator: 4, Denominator: 4 });
        Assert.DoesNotContain(reparsed.Value.Events.Children.OfType<PenguinTools.Chart.Models.umgr.BeatEvent>(),
            beat => beat.Denominator == 0);
        Assert.Contains(reparsed.Value.Events.Children.OfType<PenguinTools.Chart.Models.umgr.ScrollSpeedEvent>(),
            til => til.Timeline == 0);
    }

    [Fact]
    public async Task MgxcWriter_PreservesNegativeLanes()
    {
        var source = new C2sChart();
        source.Notes.Add(new Tap { Tick = 0, Lane = -2, Width = 4 });
        source.Notes.Add(new Tap { Tick = 480, Lane = 0, Width = 2 });
        source.Notes.Add(new Tap { Tick = 960, Lane = 16, Width = 3 });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "negative-lanes.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());

            var taps = parsed.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Tap>()
                .OrderBy(n => n.Tick.Original).ToArray();
            Assert.Equal(3, taps.Length);
            Assert.Equal(-2, taps[0].Lane);
            Assert.Equal(4, taps[0].Width);
            Assert.Equal(0, taps[1].Lane);
            Assert.Equal(16, taps[2].Lane);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_AcceptsVersion114_WithNegativeLanes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "v114.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
            VERSION	1.14.00	1.14.00
            MUSIC	0
            SEQUENCEID	0
            DIFFICULT	03
            LEVEL	15.0
            CREATOR	test
            BPM_DEF	120.000	120.000	120.000	120.000
            MET_DEF	4	4
            RESOLUTION	384
            CLK_DEF	384
            PROGJUDGE_BPM	240.000
            PROGJUDGE_AER	0.999
            TUTORIAL	0

            MET	0	0	4	4
            BPM	0	0	120.000

            TAP	0	0	-2	4
            """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Unsupported_version);
            Assert.Contains(parsed.Value!.Notes.OfType<Tap>(), tap => tap is { Lane: -2, Width: 4 });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_AcceptsVersion115()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "v115.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
            VERSION	1.15.00	1.15.00
            MUSIC	0
            SEQUENCEID	0
            DIFFICULT	03
            LEVEL	15.0
            CREATOR	test
            BPM_DEF	120.000	120.000	120.000	120.000
            MET_DEF	4	4
            RESOLUTION	384
            CLK_DEF	384
            PROGJUDGE_BPM	240.000
            PROGJUDGE_AER	0.999
            TUTORIAL	0

            MET	0	0	4	4
            BPM	0	0	120.000

            TAP	0	0	0	4
            """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Unsupported_version);
            Assert.Contains(parsed.Value!.Notes.OfType<Tap>(), tap => tap is { Lane: 0, Width: 4 });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_LegacySevenTokenSlide_DefaultsEndWidthToStartWidth()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "legacy-slide.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
            VERSION	1.01.00	1.01.00
            MUSIC	0
            SEQUENCEID	0
            DIFFICULT	00
            LEVEL	0.0
            CREATOR	test
            BPM_DEF	165.000	165.000	165.000	165.000
            MET_DEF	4	4
            RESOLUTION	384
            CLK_DEF	384
            PROGJUDGE_BPM	240.000
            PROGJUDGE_AER	0.999
            TUTORIAL	0

            BPM	0	0	165.000
            MET	0	0	4	4

            SLD	19	0	0	8	384	8
            AIR	19	384	8	8	SLD
            """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.DoesNotContain(parsed.Diagnostics.Diagnostics,
                d => d.Message.Key is MsgKeys.C2s_Invalid_field or MsgKeys.C2s_Parent_not_resolved);

            var slide = Assert.Single(parsed.Value!.Notes.OfType<Slide>());
            Assert.Equal(0, slide.Lane);
            Assert.Equal(8, slide.Width);
            Assert.Equal(8, slide.EndLane);
            Assert.Equal(8, slide.EndWidth);
            Assert.Equal(1920, slide.EndTick.Original - slide.Tick.Original); // length 384 @ RES 384 → 1920 umgr ticks

            Assert.Single(parsed.Value.Notes.OfType<Air>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_UnknownVersion_WarnsAndContinues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "unknown-ver.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
            VERSION	9.99.00	9.99.00
            MUSIC	0
            SEQUENCEID	0
            DIFFICULT	03
            LEVEL	15.0
            CREATOR	test
            BPM_DEF	120.000	120.000	120.000	120.000
            MET_DEF	4	4
            RESOLUTION	384
            CLK_DEF	384
            PROGJUDGE_BPM	240.000
            PROGJUDGE_AER	0.999
            TUTORIAL	0

            MET	0	0	4	4
            BPM	0	0	120.000

            TAP	0	0	0	4
            """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Contains(parsed.Diagnostics.Diagnostics,
                d => d is { Severity: PenguinTools.Core.Diagnostic.Severity.Warning, Message.Key: MsgKeys.C2s_Unsupported_version });
            Assert.Contains(parsed.Value!.Notes.OfType<Tap>(), tap => tap is { Lane: 0, Width: 4 });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_ReadsAirLadderAttr_AndWriterRoundTrips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ald-attr.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.15.00	1.15.00
                MUSIC	0
                SEQUENCEID	0
                DIFFICULT	03
                LEVEL	15.0
                CREATOR	test
                BPM_DEF	120.000	120.000	120.000	120.000
                MET_DEF	4	4
                RESOLUTION	384
                CLK_DEF	384
                PROGJUDGE_BPM	240.000
                PROGJUDGE_AER	0.999
                TUTORIAL	0

                MET	0	0	4	4
                BPM	0	0	120.000

                ALD	0	0	0	4	0	1.0	96	0	4	1.0	DEF	DEF
                ALD	1	0	2	2	38400	5.0	48	3	2	5.0	NON	AxisY
                ALD	2	0	4	4	1	2.0	24	5	4	3.0	RED	Trace
                ALD	3	0	6	2	0	1.0	12	7	2	1.0	CYN	AxisZ
                """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var crashes = parsed.Value!.Notes.OfType<AirCrash>().OrderBy(x => x.Tick.Original).ToArray();
            Assert.Equal(4, crashes.Length);
            Assert.Equal(AirLadderAttr.DEF, crashes[0].Attr);
            Assert.Equal(AirLadderAttr.AxisY, crashes[1].Attr);
            Assert.Equal(Color.NON, crashes[1].Color);
            Assert.Equal(AirLadderAttr.Trace, crashes[2].Attr);
            Assert.Equal(AirLadderAttr.AxisZ, crashes[3].Attr);

            var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Contains(converted.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>(),
                x => x is { Attr: AirLadderAttr.AxisY, Color: Color.NON });
            Assert.Contains(converted.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>(),
                x => x.Attr == AirLadderAttr.Trace);

            var roundtripPath = Path.Combine(directory, "ald-attr-out.c2s");
            var back = new C2SChartConverter(new C2SConvertRequest(converted.Value)).Convert();
            Assert.True(back.Succeeded, back.ToString());
            var written = await new C2SChartWriter(new C2SWriteRequest(roundtripPath, back.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(roundtripPath, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.15.00\t1.15.00", lines[0]);
            // Global 1.15: every ALD line carries ATTR, including DEF.
            Assert.Contains(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal) && line.EndsWith("\tDEF\tDEF", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal) && line.EndsWith("\tNON\tAxisY", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal) && line.EndsWith("\tRED\tTrace", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal) && line.EndsWith("\tCYN\tAxisZ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
    [Fact]
    public async Task Writer_KeepsV114_WhenAldAttrIsDefault()
    {
        var source = new C2sChart { Meta = new Meta { MainBpm = 120m } };
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Notes.Add(new AirCrash
        {
            Tick = 0, Lane = 0, Width = 4, EndTick = 480, EndLane = 0, EndWidth = 4,
            Height = 0m, EndHeight = 0m, Density = 0, Color = Color.CYN, Attr = AirLadderAttr.DEF
        });

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ald-v114.c2s");
        try
        {
            Assert.False(C2SChartWriter.NeedsV115(source));
            var written = await new C2SChartWriter(new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.14.00\t1.14.00", lines[0]);
            var ald = Assert.Single(lines, l => l.StartsWith("ALD\t", StringComparison.Ordinal));
            Assert.EndsWith("\tCYN", ald);
            Assert.DoesNotContain("AxisY", ald);
            Assert.False(ald.EndsWith("\tDEF", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Writer_SwitchesToV115_ForNoLineSlide()
    {
        var source = new C2sChart { Meta = new Meta { MainBpm = 120m } };
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Notes.Add(new Slide
        {
            Tick = 0, Lane = 0, Width = 4, EndTick = 480, EndLane = 2, EndWidth = 4,
            Joint = Joint.D, NoLine = true
        });
        source.Notes.Add(new Slide
        {
            Tick = 480, Lane = 2, Width = 4, EndTick = 960, EndLane = 4, EndWidth = 4,
            Joint = Joint.D, NoLine = false
        });
        source.Notes.Add(new AirCrash
        {
            Tick = 0, Lane = 4, Width = 2, EndTick = 96, EndLane = 4, EndWidth = 2,
            Height = 0m, EndHeight = 0m, Density = 0, Color = Color.DEF, Attr = AirLadderAttr.DEF
        });

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "noline-v115.c2s");
        try
        {
            Assert.True(C2SChartWriter.NeedsV115(source));
            var written = await new C2SChartWriter(new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.15.00\t1.15.00", lines[0]);
            Assert.Contains(lines, l => l.Contains("\tNCL", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("\tSLD", StringComparison.Ordinal));
            // Global 1.15: default ALD still emits ATTR.
            Assert.Contains(lines, l => l.StartsWith("ALD\t", StringComparison.Ordinal) && l.EndsWith("\tDEF\tDEF", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Writer_OmitsSlideLinkMarker_OnV114()
    {
        var source = new C2sChart { Meta = new Meta { MainBpm = 120m } };
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Notes.Add(new Slide
        {
            Tick = 0, Lane = 0, Width = 4, EndTick = 480, EndLane = 2, EndWidth = 4,
            Joint = Joint.D, Effect = ExEffect.UP
        });

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "slide-v114.c2s");
        try
        {
            Assert.False(C2SChartWriter.NeedsV115(source));
            var written = await new C2SChartWriter(new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.14.00\t1.14.00", lines[0]);
            var slide = Assert.Single(lines, l => l.StartsWith("SXD\t", StringComparison.Ordinal) || l.StartsWith("SLD\t", StringComparison.Ordinal));
            Assert.DoesNotContain("\tSLD\t", slide);
            Assert.DoesNotContain("\tNCL", slide);
            Assert.EndsWith("\tUP", slide);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var note = Assert.Single(parsed.Value!.Notes.OfType<Slide>());
            Assert.False(note.NoLine);
            Assert.Equal(ExEffect.UP, note.Effect);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Writer_EmitsOfficialFlickTrailingMarker()
    {
        var source = new C2sChart { Meta = new Meta { MainBpm = 120m } };
        source.Events.Add(new Bpm { Tick = 0, Value = 120m });
        source.Notes.Add(new Flick
        {
            Tick = 0,
            Lane = 3,
            Width = 5
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "flick-marker.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(path, source))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var lines = await File.ReadAllLinesAsync(
                path,
                TestContext.Current.CancellationToken);

            var flick = Assert.Single(
                lines,
                line => line.StartsWith("FLK\t", StringComparison.Ordinal));

            Assert.Equal("FLK\t0\t0\t3\t5\tL", flick);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parser_LegacyAldWithoutAttr_DefaultsToDef()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ald-legacy.c2s");
        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.13.00	1.13.00
                RESOLUTION	384
                ALD	0	0	0	4	0	1.0	96	0	4	1.0	CYN
                """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var crash = Assert.Single(parsed.Value!.Notes.OfType<AirCrash>());
            Assert.Equal(Color.CYN, crash.Color);
            Assert.Equal(AirLadderAttr.DEF, crash.Attr);

            var outPath = Path.Combine(directory, "ald-legacy-out.c2s");
            var written = await new C2SChartWriter(new C2SWriteRequest(outPath, parsed.Value))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            var lines = await File.ReadAllLinesAsync(outPath, TestContext.Current.CancellationToken);
            Assert.Equal("VERSION\t1.14.00\t1.14.00", lines[0]);
            var ald = Assert.Single(lines, line => line.StartsWith("ALD\t", StringComparison.Ordinal));
            Assert.EndsWith("\tCYN", ald);
            Assert.DoesNotContain("AxisY", ald);
            Assert.False(ald.EndsWith("\tDEF", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
