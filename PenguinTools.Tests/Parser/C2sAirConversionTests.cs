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

public sealed class C2sAirConversionTests
{
    [Fact]
    public async Task HxdHold_AirParentHld_PairsAtHoldEnd()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "hxd-air.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            HXD	0	0	0	4	96	UP
            AIR	0	96	0	4	HLD	DEF
            """, TestContext.Current.CancellationToken);

        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded);
        Assert.DoesNotContain(parsed.Diagnostics.Diagnostics,
            d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);

        var ugc = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert().Value!;
        var air = Assert.Single(ugc.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.Air>());
        Assert.Equal(480, air.Tick.Original);
        Assert.NotNull(air.PairNote);
    }

    [Fact]
    public async Task Ahd_ParsesChainsAndConvertsToUgcAirHold()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "ahd.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            TAP	0	0	4	4
            AHD	0	0	4	4	TAP	96
            AHD	0	96	4	4	AHD	96
            TAP	1	0	0	4
            AHX	1	0	0	4	TAP	48	DEF
            """, TestContext.Current.CancellationToken);

        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var holds = parsed.Value!.Notes.OfType<AirHold>().OrderBy(x => x.Tick.Original).ToArray();
        Assert.Equal(3, holds.Length);
        Assert.Equal("AHD", holds[0].Id);
        Assert.Same(holds[0], holds[1].Parent);
        Assert.Equal("AHX", holds[2].Id);
        Assert.Equal(Joint.C, holds[2].Joint);

        var ugc = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(ugc.Succeeded, ugc.ToString());
        var airHolds = ugc.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirHold>()
            .OrderBy(x => x.Tick.Original).ToArray();
        Assert.Equal(2, airHolds.Length);
        Assert.Equal(2, airHolds[0].Children.Count);
        Assert.Equal(Joint.D, airHolds[0].Children.OfType<PenguinTools.Chart.Models.umgr.AirHoldJoint>().Last().Joint);
        Assert.Equal(Joint.C, airHolds[1].Children.OfType<PenguinTools.Chart.Models.umgr.AirHoldJoint>().Single().Joint);
    }

    [Fact]
    public async Task Asc_EndpointWithoutAirAction_ConvertsToUgcControlJoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "asc.c2s");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            VERSION	1.13.00	1.13.00
            RESOLUTION	384
            TAP	0	0	4	4
            ASD	0	0	4	4	TAP	5.0	96	6	4	5.0	DEF
            ASC	0	96	6	4	ASD	5.0	96	8	4	5.0	DEF
            TAP	1	0	0	4
            ASC	1	0	0	4	TAP	5.0	48	2	4	5.0	DEF
            """, TestContext.Current.CancellationToken);

        var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var slides = parsed.Value!.Notes.OfType<AirSlide>().OrderBy(x => x.Tick.Original).ToArray();
        Assert.Equal(3, slides.Length);
        Assert.Equal("ASD", slides[0].Id);
        Assert.Same(slides[0], slides[1].Parent);
        Assert.Equal("ASC", slides[1].Id);
        Assert.Equal(Joint.C, slides[1].Joint);
        Assert.Equal("ASC", slides[2].Id);
        Assert.Equal(Joint.C, slides[2].Joint);

        var ugc = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(ugc.Succeeded, ugc.ToString());
        var airSlides = ugc.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirSlide>()
            .OrderBy(x => x.Tick.Original).ToArray();
        Assert.Equal(2, airSlides.Length);

        var chained = airSlides[0].Children.OfType<PenguinTools.Chart.Models.umgr.AirSlideJoint>().ToArray();
        Assert.Equal(2, chained.Length);
        Assert.Equal(Joint.D, chained[0].Joint);
        Assert.Equal(Joint.C, chained[1].Joint);
        Assert.Equal(Joint.C, airSlides[1].Children.OfType<PenguinTools.Chart.Models.umgr.AirSlideJoint>().Single().Joint);
    }

    [Fact]
    public async Task MgxcWriter_EmitsAirBeforeAirHold()
    {
        var source = new C2sChart();
        source.Meta.MainBpm = 120m;
        var hold = new Hold { Tick = 0, Lane = 2, Width = 2, EndTick = 480 };
        source.Notes.Add(hold);
        source.Notes.Add(new AirHold
        {
            Tick = 0, Lane = 2, Width = 2, EndTick = 480, EndLane = 2, EndWidth = 2,
            Parent = hold, Color = Color.DEF, Joint = Joint.D
        });

        source.Notes.Add(new Air
        {
            Tick = 0,
            Lane = 2,
            Width = 2,
            Parent = hold,
            Direction = AirDirection.IR,
            Color = Color.DEF
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "air-hold.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            await using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            Assert.Equal("MGXC", System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)));
            reader.ReadInt32();
            reader.ReadInt32();

            sbyte? previousType = null;
            var sawAirBeforeHold = false;
            while (stream.Position + 8 <= stream.Length)
            {
                var block = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                var size = reader.ReadInt32();
                var end = stream.Position + size;
                if (block == "dat2")
                {
                    while (stream.Position + 24 <= end)
                    {
                        var type = reader.ReadSByte();
                        var longAttr = reader.ReadSByte();
                        reader.ReadSByte();
                        reader.ReadSByte();
                        reader.ReadSByte();
                        reader.ReadSByte();
                        reader.ReadInt16();
                        reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadInt32();
                        if (type == 0x0A && longAttr == 0x01) reader.ReadInt32();
                        if (type == 0x08 && longAttr == 0x01 && previousType == 0x07)
                            sawAirBeforeHold = true;
                        previousType = type;
                    }
                }
                else
                {
                    stream.Position = end;
                }
            }

            Assert.True(sawAirBeforeHold);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_PairsUnresolvedAirHoldWithCarrier()
    {
        var source = new C2sChart();
        source.Meta.MainBpm = 120m;
        source.Notes.Add(new AirHold
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 3,
            Color = Color.DEF,
            Joint = Joint.D
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        Assert.Equal(
            string.Empty,
            converted.Value!.Meta.C2sAirSnapshot);

        var airHold = Assert.Single(
            converted.Value.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirHold>());
        Assert.NotNull(airHold.PairNote);
        Assert.Equal(480, airHold.Tick.Original);
        Assert.Equal(4, airHold.Lane);
        Assert.Equal(3, airHold.Width);

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "unresolved-air-hold.mgxc");
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());

            var parsedAirHold = Assert.Single(
                parsed.Value!.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirHold>());

            Assert.NotNull(parsedAirHold.PairNote);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_WritesDisplayAirForAirHoldAndAirSlide()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();
        chart.Meta.MainBpm = 120m;

        Assert.Null(chart.Meta.C2sAirSnapshot);

        var holdParent = new PenguinTools.Chart.Models.umgr.Tap
        {
            Tick = 0,
            Lane = 2,
            Width = 2
        };

        var airHold = new PenguinTools.Chart.Models.umgr.AirHold
        {
            Tick = 0,
            Direction = AirDirection.UL,
            Color = Color.PNK
        };

        airHold.AppendChild(
            new PenguinTools.Chart.Models.umgr.AirHoldJoint
            {
                Tick = 480,
                Joint = Joint.D
            });

        holdParent.MakePair(airHold);

        chart.Notes.AppendChild(holdParent);
        chart.Notes.AppendChild(airHold);

        var slideParent = new PenguinTools.Chart.Models.umgr.Tap
        {
            Tick = 960,
            Lane = 4,
            Width = 3
        };

        var airSlide = new PenguinTools.Chart.Models.umgr.AirSlide
        {
            Tick = 960,
            Direction = AirDirection.UR,
            Color = Color.PNK,
            Height = 4m
        };

        airSlide.AppendChild(
            new PenguinTools.Chart.Models.umgr.AirSlideJoint
            {
                Tick = 1440,
                Lane = 4,
                Width = 3,
                Height = 4m,
                Joint = Joint.D
            });

        slideParent.MakePair(airSlide);

        chart.Notes.AppendChild(slideParent);
        chart.Notes.AppendChild(airSlide);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "no-air-arrow.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(
                        path,
                        chart))
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

            var parsedAirHold = Assert.Single(
                parsed.Value!.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirHold>());

            var parsedAirSlide = Assert.Single(
                parsed.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirSlide>());

            Assert.Equal(AirDirection.UL, parsedAirHold.Direction);
            Assert.Equal(Color.PNK, parsedAirHold.Color);
            Assert.Equal(AirDirection.UR, parsedAirSlide.Direction);
            Assert.Equal(Color.PNK, parsedAirSlide.Color);
            Assert.Empty(
                parsed.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.Air>());
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public void OverlappingTapAirAndHoldAirSlide_EmitsAirOnlyOnTap()
    {
        var chart = new PenguinTools.Chart.Models.umgr.Chart();
        chart.Meta.MainBpm = 120m;

        var tap = new PenguinTools.Chart.Models.umgr.Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 4
        };
        var air = new PenguinTools.Chart.Models.umgr.Air
        {
            Direction = AirDirection.UL,
            Color = Color.DEF
        };
        tap.MakePair(air);

        var hold = new PenguinTools.Chart.Models.umgr.Hold
        {
            Tick = 0,
            Lane = 4,
            Width = 4
        };
        var tail = new PenguinTools.Chart.Models.umgr.HoldJoint
        {
            Tick = 480
        };
        hold.AppendChild(tail);

        var airSlide = new PenguinTools.Chart.Models.umgr.AirSlide
        {
            Direction = AirDirection.IR,
            Color = Color.DEF,
            Height = 80m
        };
        airSlide.AppendChild(
            new PenguinTools.Chart.Models.umgr.AirSlideJoint
            {
                Tick = 960,
                Lane = 4,
                Width = 4,
                Height = 80m,
                Joint = Joint.D
            });
        tail.MakePair(airSlide);

        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        chart.Notes.AppendChild(hold);
        chart.Notes.AppendChild(airSlide);

        var convert = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(convert.Succeeded, convert.ToString());

        var c2sAir = Assert.Single(convert.Value!.Notes.OfType<Air>());
        Assert.IsType<Tap>(c2sAir.Parent);
        Assert.Equal(AirDirection.UL, c2sAir.Direction);

        var c2sSlide = Assert.Single(convert.Value.Notes.OfType<AirSlide>());
        Assert.IsType<Hold>(c2sSlide.Parent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void C2sRoundTrip_PreservesAirCountAcrossSharedParentActions(
        int airCount)
    {
        var source = new C2sChart();

        var tap = new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 3
        };
        source.Notes.Add(tap);

        source.Notes.Add(new AirHold
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 3,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new AirHold
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            EndTick = 1440,
            EndLane = 4,
            EndWidth = 3,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        for (var i = 0; i < airCount; i++)
        {
            source.Notes.Add(new Air
            {
                Tick = 480,
                Lane = 4,
                Width = 3,
                Direction = i == 0
                    ? AirDirection.IR
                    : AirDirection.DW,
                Color = Color.DEF,
                Parent = tap
            });
        }

        var converted =
            new UgcChartConverter(
                new UgcConvertRequest(source))
                .Convert();

        Assert.True(
            converted.Succeeded,
            converted.ToString());

        var roundTrip =
            new C2SChartConverter(
                new C2SConvertRequest(converted.Value!))
                .Convert();

        Assert.True(
            roundTrip.Succeeded,
            roundTrip.ToString());

        Assert.Equal(
            airCount,
            roundTrip.Value!.Notes.OfType<Air>().Count());
    }

    [Fact]
    public void C2sRoundTrip_DoesNotDuplicateSingleAirAcrossMixedSharedParentActions()
    {
        var source = new C2sChart();

        var tap = new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 3
        };
        source.Notes.Add(tap);

        source.Notes.Add(new AirHold
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 3,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new AirSlide
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Height = 4,
            EndTick = 1440,
            EndLane = 8,
            EndWidth = 3,
            EndHeight = 8,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new Air
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Direction = AirDirection.IR,
            Color = Color.DEF,
            Parent = tap
        });

        var converted =
            new UgcChartConverter(
                new UgcConvertRequest(source))
                .Convert();

        Assert.True(
            converted.Succeeded,
            converted.ToString());

        var roundTrip =
            new C2SChartConverter(
                new C2SConvertRequest(converted.Value!))
                .Convert();

        Assert.True(
            roundTrip.Succeeded,
            roundTrip.ToString());

        Assert.Single(
            roundTrip.Value!.Notes.OfType<Air>());
    }

    [Fact]
    public async Task C2sRoundTrip_PreservesAirSnapshotAcrossMgxcSerialization()
    {
        var source = new C2sChart();

        var tap = new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 3
        };
        source.Notes.Add(tap);

        source.Notes.Add(new AirSlide
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Height = 4,
            EndTick = 1440,
            EndLane = 8,
            EndWidth = 3,
            EndHeight = 8,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new Air
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Direction = AirDirection.IR,
            Color = Color.DEF,
            Parent = tap
        });

        var converted =
            new UgcChartConverter(
                new UgcConvertRequest(source))
                .Convert();

        Assert.True(
            converted.Succeeded,
            converted.ToString());

        Assert.NotNull(converted.Value!.Meta.C2sAirSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sAirEditKey);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "air-snapshot-roundtrip.mgxc");

        try
        {
            var written =
                await new MgxcChartWriter(
                        new MgxcWriteRequest(path, converted.Value))
                    .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(
                written.Succeeded,
                written.ToString());

            var parsed =
                await new MgxcParser(
                        new MgxcParseRequest(path, TestAssets.Load()),
                        TestMediaTool.Instance)
                    .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(
                parsed.Succeeded,
                parsed.ToString());

            Assert.Equal(
                converted.Value.Meta.C2sAirSnapshot,
                parsed.Value!.Meta.C2sAirSnapshot);

            Assert.Equal(
                converted.Value.Meta.C2sAirEditKey,
                parsed.Value.Meta.C2sAirEditKey);

            var roundTrip =
                new C2SChartConverter(
                    new C2SConvertRequest(parsed.Value))
                    .Convert();

            Assert.True(
                roundTrip.Succeeded,
                roundTrip.ToString());

            var air = Assert.Single(
                roundTrip.Value!.Notes.OfType<Air>());

            Assert.Equal(480, air.Tick.Original);
            Assert.Equal(4, air.Lane);
            Assert.Equal(3, air.Width);
            Assert.Equal(AirDirection.IR, air.Direction);
            Assert.Equal(Color.DEF, air.Color);
            Assert.IsType<Tap>(air.Parent);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AirHold_WithDuplicateSlideParents_UsesUniqueTerminalSlide()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "duplicate-slide-airhold.c2s");

        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                VERSION     1.14.00        1.14.00
                RESOLUTION  384
                SLD         67      56      3       8       88      4       8
                SLD         67      56      3       8       88      4       8
                SLD         67      56      3       8       88      4       8
                SLC         67      144     4       8       240     3       8
                SLC         67      144     4       8       240     5       8
                AHD         67      144     4       8       SLD     624
                """,
                TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(
                    new C2SParseRequest(path))
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(
                parsed.Succeeded,
                parsed.ToString());

            var converted = new UgcChartConverter(
                    new UgcConvertRequest(parsed.Value!))
                .Convert();

            Assert.True(
                converted.Succeeded,
                converted.ToString());

            var roots = converted.Value!.Notes.Children;

            Assert.DoesNotContain(
                roots,
                note => note is PenguinTools.Chart.Models.umgr.ExTap
                {
                    Role: PenguinTools.Chart.Models.umgr.ExTapRole.AirActionCarrier
                });

            var slides = roots
                .OfType<PenguinTools.Chart.Models.umgr.Slide>()
                .ToArray();

            Assert.Equal(3, slides.Length);

            var pairedJoint = Assert.Single(
                slides.SelectMany(slide =>
                    slide.Children
                        .OfType<PenguinTools.Chart.Models.umgr.SlideJoint>()),
                joint =>
                    joint.PairNote is PenguinTools.Chart.Models.umgr.AirHold);

            var pairedSlide =
                Assert.IsType<PenguinTools.Chart.Models.umgr.Slide>(
                    pairedJoint.Parent);

            Assert.Same(
                pairedJoint,
                pairedSlide.LastChild);

            Assert.IsType<PenguinTools.Chart.Models.umgr.AirHold>(
                pairedJoint.PairNote);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AirSnapshot_IsDroppedWhenAirSlideChanges()
    {
        var source = new C2sChart();

        var tap = new Tap
        {
            Tick = 480,
            Lane = 4,
            Width = 3
        };
        source.Notes.Add(tap);

        source.Notes.Add(new AirSlide
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Height = 4,
            EndTick = 1440,
            EndLane = 8,
            EndWidth = 3,
            EndHeight = 8,
            Color = Color.DEF,
            Joint = Joint.D,
            Parent = tap
        });

        source.Notes.Add(new Air
        {
            Tick = 480,
            Lane = 4,
            Width = 3,
            Direction = AirDirection.IR,
            Color = Color.DEF,
            Parent = tap
        });

        var converted =
            new UgcChartConverter(
                new UgcConvertRequest(source))
                .Convert();

        Assert.True(
            converted.Succeeded,
            converted.ToString());

        Assert.NotNull(converted.Value!.Meta.C2sAirSnapshot);
        Assert.NotNull(converted.Value.Meta.C2sAirEditKey);

        var airSlide = Assert.Single(
            converted.Value.Notes.Children
                .OfType<PenguinTools.Chart.Models.umgr.AirSlide>());

        airSlide.Height = 5m;

        var roundTrip =
            new C2SChartConverter(
                new C2SConvertRequest(converted.Value))
                .Convert();

        Assert.True(
            roundTrip.Succeeded,
            roundTrip.ToString());

        Assert.Null(roundTrip.Value!.Meta.C2sAirSnapshot);
        Assert.Null(roundTrip.Value.Meta.C2sAirEditKey);
    }

    [Fact]
    public async Task AirActions_OnSameParent_PreserveSourceAirOrder()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "air-action-order.c2s");

        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllTextAsync(path, """
                VERSION	1.13.00	1.13.00
                RESOLUTION	384
                TAP	0	0	4	4
                AHD	0	0	4	4	TAP	96
                ASD	0	0	4	4	TAP	5.0	96	6	4	5.0	DEF
                AUL	0	0	4	4	TAP
                ADR	0	0	4	4	TAP
                """, TestContext.Current.CancellationToken);

            var parsed = await new C2SParser(
                new C2SParseRequest(path))
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var sourceAirs = parsed.Value!.Notes
                .OfType<Air>()
                .ToArray();

            Assert.Equal(2, sourceAirs.Length);

            var converted = new UgcChartConverter(
                new UgcConvertRequest(parsed.Value))
                .Convert();

            Assert.True(converted.Succeeded, converted.ToString());

            var airHold = Assert.Single(
                converted.Value!.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirHold>());

            var airSlide = Assert.Single(
                converted.Value.Notes.Children
                    .OfType<PenguinTools.Chart.Models.umgr.AirSlide>());

            Assert.Equal(sourceAirs[0].Direction, airHold.Direction);
            Assert.Equal(sourceAirs[1].Direction, airSlide.Direction);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
