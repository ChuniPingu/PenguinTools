using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core.Metadata;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sAudioOffsetTests
{
    [Theory]
    [InlineData(ChartDialect.Original, 4, true)]
    [InlineData(ChartDialect.Original, 4, false)]
    [InlineData(ChartDialect.Original, 7, true)]
    [InlineData(ChartDialect.Original, 7, false)]
    [InlineData(ChartDialect.Extended, 4, true)]
    [InlineData(ChartDialect.Extended, 4, false)]
    [InlineData(ChartDialect.Extended, 7, true)]
    [InlineData(ChartDialect.Extended, 7, false)]
    public async Task SpeedEndpoints_AreRoundedAfterSelectedOriginIsApplied(
        ChartDialect mode, int denominator, bool explicitEnd)
    {
        var input = TestTempPaths.Create(".mgxc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var source = new U.Chart();
            source.Meta.BgmEnableBarOffset = true;
            source.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
            source.Events.AppendChild(new U.BeatEvent { Tick = 0, Bar = 0, Numerator = 1, Denominator = denominator });
            source.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 1, Timeline = 0, Speed = 2 });
            source.Events.AppendChild(new U.NoteSpeedEvent { Tick = 1, Speed = 2 });
            if (explicitEnd)
            {
                source.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 503, Timeline = 0, Speed = 1 });
                source.Events.AppendChild(new U.NoteSpeedEvent { Tick = 503, Speed = 1 });
            }
            source.Notes.AppendChild(new U.Tap { Tick = 503, Lane = 0, Width = 4 });
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(input, source, ChartDialect.Original))
                .WriteAsync(ct)).Succeeded);
            var parsed = await new MgxcParser(new MgxcParseRequest(input, TestAssets.Load(), mode), TestMediaTool.Instance)
                .ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.True((await new C2SChartWriter(new C2SWriteRequest(output, converted.Value!)).WriteAsync(ct)).Succeeded);
            var result = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(result.Succeeded, result.ToString());
            var offset = mode == ChartDialect.Original ? (int)Math.Round(1920m / denominator) : 0;
            var start = (int)Math.Round((1m + offset) / 5) * 5;
            var end = (int)Math.Round((503m + offset) / 5) * 5;
            Assert.Equal(end, Assert.Single(result.Value!.Notes.OfType<C.Tap>()).Tick.Original);
            var speeds = result.Value.Events.OfType<C.SpeedEventBase>().ToArray();
            Assert.Equal(2, speeds.Length);
            Assert.All(speeds, speed =>
            {
                Assert.Equal(start, speed.Tick.Original);
                Assert.Equal(end, speed.Tick.Original + speed.Length.Original);
            });
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExtendedWriters_PreserveFixedOriginOnSubsequentDefaultReads(bool ugc, bool blank)
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = blank;
        chart.Meta.BgmManualOffset = .25m;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Tick = 0, Numerator = 3, Denominator = 8 });
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = 0, Width = 2 });
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            for (var cycle = 0; cycle < 2; cycle++)
            {
                var mode = cycle == 0 ? ChartDialect.Extended : ChartDialect.Auto;
                Assert.True(ugc
                    ? (await new UgcChartWriter(path, chart, mode).WriteAsync(ct)).Succeeded
                    : (await new MgxcChartWriter(new MgxcWriteRequest(path, chart, mode)).WriteAsync(ct)).Succeeded);
                var parsed = ugc
                    ? await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                    : await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                Assert.True(parsed.Succeeded, parsed.ToString());
                chart = parsed.Value!;
                Assert.Equal(blank, chart.Meta.BgmEnableBarOffset);
                Assert.Equal(.25m, chart.Meta.BgmManualOffset);
                Assert.True(chart.Extras.FixedZeroOrigin);
                var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
                Assert.True(converted.Succeeded, converted.ToString());
                Assert.Equal(0, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true, 4, 4, 1920)]
    [InlineData(true, 3, 8, 720)]
    [InlineData(false, 4, 4, 0)]
    [InlineData(false, 3, 8, 0)]
    public async Task OriginalMgxc_UsesHistoricalPreroll(bool enabled, int numerator, int denominator, int offset)
    {
        var source = new U.Chart();
        source.Meta.BgmEnableBarOffset = enabled;
        source.Meta.BgmManualOffset = .25m;
        source.Extras.ClickTicks = [0, 480, 1920];
        source.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120m });
        source.Events.AppendChild(new U.BpmEvent { Tick = 960, Bpm = 150m });
        source.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = numerator, Denominator = denominator });
        source.Events.AppendChild(new U.BeatEvent { Bar = 1, Numerator = 4, Denominator = 4 });
        source.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Speed = 2m, Timeline = 0 });
        source.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 1920, Speed = 1m, Timeline = 0 });
        source.Events.AppendChild(new U.NoteSpeedEvent { Tick = 0, Speed = .5m });
        source.Events.AppendChild(new U.NoteSpeedEvent { Tick = 1920, Speed = 1m });
        source.Notes.AppendChild(new U.Tap { Tick = 0, Lane = 0, Width = 2 });
        var hold = new U.Hold { Tick = 480, Lane = 4, Width = 2 };
        hold.AppendChild(new U.HoldJoint { Tick = 960 });
        source.Notes.AppendChild(hold);
        source.Notes.AppendChild(new U.Flick { Tick = 1920, Lane = 8, Width = 2 });

        var mgxcPath = TestTempPaths.Create(".mgxc");
        var c2sPath = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxcPath, source)).WriteAsync(ct)).Succeeded);
            var parsed = await new MgxcParser(new MgxcParseRequest(mgxcPath, TestAssets.Load(), ChartDialect.Original), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            var chart = converted.Value!;
            Assert.Equal(offset, Assert.Single(chart.Notes.OfType<C.Tap>()).Tick.Original);
            var convertedHold = Assert.Single(chart.Notes.OfType<C.Hold>());
            Assert.Equal(480 + offset, convertedHold.Tick.Original);
            Assert.Equal(960 + offset, convertedHold.EndTick.Original);
            Assert.Equal(0, chart.Events.OfType<C.Bpm>().First().Tick.Original);
            Assert.Equal(960 + offset, chart.Events.OfType<C.Bpm>().Last().Tick.Original);
            var initialMeasure = 1920 * numerator / denominator;
            Assert.Equal(0, chart.Events.OfType<C.Met>().First().Tick.Original);
            Assert.Equal(initialMeasure + offset, chart.Events.OfType<C.Met>().Last().Tick.Original);
            Assert.All(chart.Events.OfType<C.SpeedEventBase>(), e =>
            {
                Assert.Equal(0, e.Tick.Original);
                Assert.Equal(1920 + offset, e.Length.Original);
            });
            Assert.Equal(.25m + (enabled ? initialMeasure / 960m : 0m), chart.Meta.BgmRealOffset);
            Assert.Equal(0, parsed.Value!.Notes.Children[0].Tick.Original);
            Assert.Equal([offset, 480 + offset, 1920 + offset], chart.Extras.ClickTicks);
            Assert.Equal([0, 480, 1920], parsed.Value.Extras.ClickTicks);
            var secondConversion = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(secondConversion.Succeeded, secondConversion.ToString());
            Assert.Equal(chart.Extras.ClickTicks, secondConversion.Value!.Extras.ClickTicks);

            Assert.True((await new C2SChartWriter(new C2SWriteRequest(c2sPath, chart)).WriteAsync(ct)).Succeeded);
            var written = await new C2SParser(new C2SParseRequest(c2sPath)).ParseAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            Assert.Equal(offset, Assert.Single(written.Value!.Notes.OfType<C.Tap>()).Tick.Original);
            Assert.Equal(960 + offset, Assert.Single(written.Value.Notes.OfType<C.Hold>()).EndTick.Original);
            Assert.Equal([offset, 480 + offset, 1920 + offset], written.Value.Extras.ClickTicks);
        }
        finally
        {
            File.Delete(mgxcPath);
            File.Delete(c2sPath);
        }
    }

    [Theory]
    [InlineData(4, 4, 1920, true)]
    [InlineData(4, 4, 1920, false)]
    [InlineData(3, 8, 720, true)]
    [InlineData(3, 8, 720, false)]
    [InlineData(3, 7, 823, true)]
    [InlineData(3, 7, 823, false)]
    public async Task OriginalInitialSpeeds_EndAtTheShiftedBoundary(
        int numerator, int denominator, int offset, bool explicitEnd)
    {
        var source = new U.Chart();
        source.Meta.BgmEnableBarOffset = true;
        source.Meta.BgmInitialNumerator = numerator;
        source.Meta.BgmInitialDenominator = denominator;
        source.Extras.EncodingDialect = ChartDialect.Original;
        source.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        source.Events.AppendChild(new U.BeatEvent { Tick = 0, Bar = 0, Numerator = numerator, Denominator = denominator });
        source.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Timeline = 0, Speed = 2 });
        source.Events.AppendChild(new U.NoteSpeedEvent { Tick = 0, Speed = .5m });
        if (explicitEnd)
        {
            source.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 1921, Timeline = 0, Speed = 1 });
            source.Events.AppendChild(new U.NoteSpeedEvent { Tick = 1921, Speed = 1 });
        }
        source.Notes.AppendChild(new U.Tap { Tick = 960, Lane = 0, Width = 2 });
        source.Notes.AppendChild(new U.Tap { Tick = 1921, Lane = 4, Width = 2 });

        var converted = new C2SChartConverter(new C2SConvertRequest(source)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var chart = converted.Value!;
        var tap = Assert.Single(chart.Notes.OfType<C.Tap>(), n => n.Lane == 0);
        Assert.Equal(960 + offset, tap.Tick.Original);
        var speeds = chart.Events.OfType<C.SpeedEventBase>().ToArray();
        Assert.Equal(2, speeds.Length);
        Assert.All(speeds, speed =>
        {
            Assert.Equal(0, speed.Tick.Original);
            Assert.Equal(1921 + offset, speed.Tick.Original + speed.Length.Original);
            Assert.InRange(tap.Tick.Original, speed.Tick.Original, speed.Tick.Original + speed.Length.Original - 1);
        });

        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new C2SChartWriter(new C2SWriteRequest(output, chart)).WriteAsync(ct)).Succeeded);
            var parsed = await new C2SParser(new C2SParseRequest(output)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var expectedEnd = (int)Math.Round((1921 + offset) / 5m) * 5;
            Assert.All(parsed.Value!.Events.OfType<C.SpeedEventBase>(), speed =>
            {
                Assert.Equal(0, speed.Tick.Original);
                Assert.Equal(expectedEnd, speed.Tick.Original + speed.Length.Original);
            });
        }
        finally { File.Delete(output); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task C2sImport_PreservesOriginAcrossMgxcRoundTrips(bool legacyMetadata, bool edited)
    {
        var source = new C.Chart();
        source.Meta.BgmEnableBarOffset = true;
        source.Events.Add(new C.Bpm { Tick = 0, Value = 120m });
        source.Events.Add(new C.Met { Tick = 0, Numerator = 4, Denominator = 4 });
        source.Events.Add(new C.Slp { Tick = 480, Length = 960, Speed = 2m, Timeline = 0 });
        source.Notes.Add(new C.Tap { Tick = 1920, Lane = 0, Width = 2 });
        source.Notes.Add(new C.Sla { Tick = 1920, Length = 480, Lane = 0, Width = 2, Timeline = 0 });
        var ct = TestContext.Current.CancellationToken;
        var c2sPath = TestTempPaths.Create(".c2s");
        var mgxcPath = TestTempPaths.Create(".mgxc");
        try
        {
            Assert.True((await new C2SChartWriter(new C2SWriteRequest(c2sPath, source)).WriteAsync(ct)).Succeeded);
            var parsed = await new C2SParser(new C2SParseRequest(c2sPath)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            source = parsed.Value!;
            for (var round = 0; round < 2; round++)
            {
                var ugc = new UgcChartConverter(new UgcConvertRequest(source)).Convert();
                Assert.True(ugc.Succeeded, ugc.ToString());
                Assert.True(ugc.Value!.Extras.C2sCoordinateOrigin);
                if (legacyMetadata)
                {
                    ugc.Value.Extras.C2sCoordinateOrigin = false;
                    ugc.Value.Extras.FixedZeroOrigin = false;
                    ugc.Value.Meta.BgmFixedZeroOrigin = false;
                }
                if (edited)
                {
                    // Exercise both the origin flag and older snapshot-only imports.
                    ugc.Value.Extras.SourceKey = null;
                    if (!legacyMetadata)
                    {
                        ugc.Value.Extras.SourceSnapshot = "";
                    }
                    Assert.Single(ugc.Value.Notes.Children.OfType<U.Tap>()).Tick = 2400;
                }
                Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxcPath, ugc.Value)).WriteAsync(ct)).Succeeded);
                var mgxc = await new MgxcParser(new MgxcParseRequest(mgxcPath, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                Assert.True(mgxc.Succeeded, mgxc.ToString());
                Assert.Equal(!legacyMetadata, mgxc.Value!.Extras.C2sCoordinateOrigin);
                var result = new C2SChartConverter(new C2SConvertRequest(mgxc.Value)).Convert();
                Assert.True(result.Succeeded, result.ToString());
                source = result.Value!;
                Assert.Equal(edited ? 2400 : 1920, Assert.Single(source.Notes.OfType<C.Tap>()).Tick.Original);
                Assert.Equal(480, Assert.Single(source.Events.OfType<C.Slp>()).Tick.Original);
                Assert.Equal(1920, Assert.Single(source.Notes.OfType<C.Sla>()).Tick.Original);
            }
        }
        finally
        {
            File.Delete(mgxcPath);
            File.Delete(c2sPath);
        }
    }

    [Theory]
    [InlineData(ChartDialect.Original, false)]
    [InlineData(ChartDialect.Original, true)]
    [InlineData(ChartDialect.Extended, false)]
    [InlineData(ChartDialect.Extended, true)]
    public async Task FixedZeroOrigin_KeepsNotesEventsAndAudioAcrossEditedContainers(ChartDialect dialect, bool blankMeasure)
    {
        var source = new U.Chart();
        source.Meta.BgmEnableBarOffset = blankMeasure;
        source.Meta.BgmManualOffset = .25m;
        source.Extras.FixedZeroOrigin = true;
        source.Extras.ClickTicks = [0, 480, 1920];
        source.Meta.Copyright = "User copyright;retained";
        source.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        source.Events.AppendChild(new U.BpmEvent { Tick = 960, Bpm = 180 });
        source.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 3, Denominator = 8 });
        source.Events.AppendChild(new U.BeatEvent { Bar = 2, Numerator = 4, Denominator = 4 });
        source.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Speed = 2, Timeline = 0 });
        source.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 1920, Speed = 1, Timeline = 0 });
        foreach (var tick in new[] { 0, 1920, 3840 })
        {
            source.Notes.AppendChild(new U.Tap { Tick = tick, Lane = 0, Width = 2 });
        }
        var hold = new U.Hold { Tick = 480, Lane = 4, Width = 2 };
        hold.AppendChild(new U.HoldJoint { Tick = 960 });
        source.Notes.AppendChild(hold);
        var mgxcPath = TestTempPaths.Create(".mgxc");
        var ugcPath = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(mgxcPath, source, dialect)).WriteAsync(ct)).Succeeded);
                var binary = await new MgxcParser(new MgxcParseRequest(mgxcPath, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                Assert.True(binary.Succeeded, binary.ToString());
                Assert.True(binary.Value!.Extras.FixedZeroOrigin);
                Assert.True((await new UgcChartWriter(ugcPath, binary.Value, dialect).WriteAsync(ct)).Succeeded);
                var text = await new UgcParser(new UgcParseRequest(ugcPath, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                Assert.True(text.Succeeded, text.ToString());
                source = text.Value!;
                Assert.True(source.Extras.FixedZeroOrigin);
                Assert.Equal("User copyright;retained", source.Meta.Copyright);
                Assert.Equal(.25m, source.Meta.BgmRealOffset);
                // Origin survives independently of source snapshots and after editing.
                source.Extras.SourceKey = null;
                source.Extras.SourceSnapshot = "";
                source.Extras.C2sCoordinateOrigin = false;
                var converted = new C2SChartConverter(new C2SConvertRequest(source)).Convert();
                Assert.True(converted.Succeeded, converted.ToString());
                var chart = converted.Value!;
                Assert.Equal([0, 1920, 3840], chart.Notes.OfType<C.Tap>().Select(n => n.Tick.Original));
                Assert.Equal([0, 960], chart.Events.OfType<C.Bpm>().Select(n => n.Tick.Original));
                Assert.Equal([0, 1440], chart.Events.OfType<C.Met>().Select(n => n.Tick.Original));
                Assert.Equal([0, 480, 1920], chart.Extras.ClickTicks);
                Assert.Equal(0, Assert.Single(chart.Events.OfType<C.SpeedEventBase>()).Tick.Original);
                var convertedHold = Assert.Single(chart.Notes.OfType<C.Hold>());
                Assert.Equal(480, convertedHold.Tick.Original);
                Assert.Equal(960, convertedHold.EndTick.Original);
                Assert.Equal(.25m, chart.Meta.BgmRealOffset);
            }
        }
        finally
        {
            File.Delete(mgxcPath);
            File.Delete(ugcPath);
        }
    }

    [Theory]
    [InlineData("MGR_ORIGIN=ZERO_V1;User copyright", true, "User copyright")]
    [InlineData("User copyright;MGR_ORIGIN=ZERO_V1", true, "User copyright")]
    [InlineData("User MGR_ORIGIN=ZERO_V1 copyright", false, "User MGR_ORIGIN=ZERO_V1 copyright")]
    public async Task FixedOrigin_RequiresIndependentCopyrightToken(string copyright, bool fixedOrigin, string userText)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, $"@VER\t8\n@COPYRIGHT\t{copyright}\n@BPM\t0'0\t120\n" +
                "@FLAG\tSOFFSET\tTRUE\n#0'0:t04\n", ct);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(fixedOrigin, parsed.Value!.Extras.FixedZeroOrigin);
            Assert.Equal(userText, parsed.Value.Meta.Copyright);
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(fixedOrigin ? 0 : 1920, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FixedOrigin_CompatibilityBookmarkDoesNotBecomeUserBookmark()
    {
        var source = new U.Chart();
        source.Meta.BgmEnableBarOffset = true;
        source.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        source.Events.AppendChild(new U.BookmarkEvent { Tick = 0, Tag = PenguinTools.Chart.ChartExtras.FixedZeroOriginMarker });
        source.Notes.AppendChild(new U.Tap { Tick = 1920, Lane = 0, Width = 2 });
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, source)).WriteAsync(ct)).Succeeded);
            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.True(parsed.Value!.Extras.FixedZeroOrigin);
            Assert.Empty(parsed.Value.Events.Children.OfType<U.BookmarkEvent>());
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(1920, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(32748, false, false)]
    [InlineData(32749, false, false)]
    [InlineData(32767, false, false)]
    [InlineData(32767, true, false)]
    [InlineData(32728, false, true)]
    [InlineData(32729, false, true)]
    [InlineData(32767, true, true)]
    public async Task FixedOrigin_PreservesCopyrightAtNativeByteLimit(int byteCount, bool unicode, bool preroll)
    {
        var source = new U.Chart();
        source.Extras.FixedZeroOrigin = true;
        source.Extras.PrerollBar1Applied = preroll;
        source.Meta.BgmEnableBarOffset = true;
        source.Meta.Copyright = unicode
            ? new string('界', byteCount / 3) + new string('x', byteCount % 3)
            : new string('x', byteCount);
        source.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        source.Notes.AppendChild(new U.Tap { Tick = 1920, Lane = 0, Width = 2 });
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, source)).WriteAsync(ct)).Succeeded);
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                reader.BaseStream.Position = 20; // MGXC header and meta block header.
                while (true)
                {
                    var tag = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                    var type = reader.ReadInt16();
                    var size = reader.ReadInt16();
                    var bytes = reader.ReadBytes(type switch
                    {
                        1 => 0,
                        2 => 4,
                        3 => 8,
                        4 => size,
                        _ => throw new InvalidDataException($"Unknown MGXC metadata type {type}")
                    });
                    if (tag != "lcpy")
                    {
                        continue;
                    }
                    var copyright = System.Text.Encoding.UTF8.GetString(bytes);
                    Assert.Equal(byteCount + 19 + (preroll ? 20 : 0) <= short.MaxValue,
                        copyright.StartsWith(PenguinTools.Chart.ChartExtras.FixedZeroOriginMarker, StringComparison.Ordinal));
                    break;
                }
            }
            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Equal(source.Meta.Copyright, parsed.Value!.Meta.Copyright);
            Assert.True(parsed.Value.Extras.FixedZeroOrigin);
            Assert.Equal(preroll, parsed.Value.Extras.PrerollBar1Applied);
            Assert.Empty(parsed.Value.Events.Children.OfType<U.BookmarkEvent>());
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(1920, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NativeOriginToken_OverridesOlderRetainedExtras()
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = true;
        chart.Meta.Copyright = "MGR_ORIGIN=ZERO_V1;User copyright";
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Notes.AppendChild(new U.Tap { Tick = 1920, Lane = 0, Width = 2 });
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.False(chart.Extras.FixedZeroOrigin);
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.True(parsed.Value!.Extras.FixedZeroOrigin);
            Assert.Equal("User copyright", parsed.Value.Meta.Copyright);
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(1920, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
            Assert.Equal(0m, converted.Value.Meta.BgmRealOffset);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("@COPYRIGHT\tMGR_ORIGIN=ZERO_V2;User copyright")]
    [InlineData("'MGR_ORIGIN=ZERO_V2")]
    [InlineData("@COPYRIGHT\tMGR_PREROLL=BAR1_V2;User copyright")]
    [InlineData("'MGR_PREROLL=BAR1_V2")]
    [InlineData("@COPYRIGHT\tMGR_PREROLL=BAR1_V1;MGR_PREROLL_SNAPSHOT_SHIFT=1920;")]
    [InlineData("'MGR_PREROLL_SNAPSHOT_SHIFT=1920")]
    public async Task UnknownCoordinateOrigin_IsRejectedInsteadOfShifting(string header)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, $"@VER\t8\n{header}\n@BPM\t0'0\t120\n#0'0:t04\n", ct);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.False(parsed.Succeeded);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, ChartDialect.Original, false)]
    [InlineData(false, ChartDialect.Original, true)]
    [InlineData(false, ChartDialect.Extended, false)]
    [InlineData(false, ChartDialect.Extended, true)]
    [InlineData(true, ChartDialect.Original, false)]
    [InlineData(true, ChartDialect.Original, true)]
    [InlineData(true, ChartDialect.Extended, false)]
    [InlineData(true, ChartDialect.Extended, true)]
    public async Task PrerollEvidence_SurvivesC2sAndEditorCyclesWithoutMovingAgain(
        bool ugc, ChartDialect dialect, bool applied)
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Extras.PrerollBar1Applied = applied;
        chart.Meta.BgmEnableBarOffset = true;
        chart.Extras.ClickTicks = [0, 480];
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Notes.AppendChild(new U.Tap { Tick = 1920, Lane = 0, Width = 2 });
        var editor = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var c2s = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Assert.True(ugc
                    ? (await new UgcChartWriter(editor, chart, dialect).WriteAsync(ct)).Succeeded
                    : (await new MgxcChartWriter(new MgxcWriteRequest(editor, chart, dialect)).WriteAsync(ct)).Succeeded);
                var parsed = ugc
                    ? await new UgcParser(new UgcParseRequest(editor, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct)
                    : await new MgxcParser(new MgxcParseRequest(editor, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
                Assert.True(parsed.Succeeded, parsed.ToString());
                Assert.Equal(applied, parsed.Value!.Extras.PrerollBar1Applied);
                var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
                Assert.True(converted.Succeeded, converted.ToString());
                Assert.Equal(1920, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
                Assert.Equal([0, 480], converted.Value.Extras.ClickTicks);
                Assert.True((await new C2SChartWriter(new C2SWriteRequest(c2s, converted.Value, parsed.Value.GetCalculator())).WriteAsync(ct)).Succeeded);
                var text = await File.ReadAllTextAsync(c2s, ct);
                Assert.Equal(applied ? 1 : 0, text.Split('\n').Count(l => l.Trim() == "//MGR_PREROLL=BAR1_V1"));
                var reparsed = await new C2SParser(new C2SParseRequest(c2s)).ParseAsync(ct);
                Assert.True(reparsed.Succeeded, reparsed.ToString());
                Assert.Equal(applied, reparsed.Value!.Extras.PrerollBar1Applied);
                var restored = new UgcChartConverter(new UgcConvertRequest(reparsed.Value)).Convert();
                Assert.True(restored.Succeeded, restored.ToString());
                chart = restored.Value!;
            }
        }
        finally { File.Delete(editor); File.Delete(c2s); }
    }

    [Theory]
    [InlineData(true, 4, 1920, true)]
    [InlineData(false, 4, 0, false)]
    [InlineData(true, 0, 0, false)]
    public void LegacyCompensation_RecordsOnlyMaterializedPositivePreroll(bool enabled, int numerator, int tick, bool applied)
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = enabled;
        chart.Meta.BgmInitialNumerator = numerator;
        chart.Meta.BgmInitialDenominator = 4;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = 0, Width = 2 });
        var result = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(tick, Assert.Single(result.Value!.Notes.OfType<C.Tap>()).Tick.Original);
        Assert.Equal(applied, result.Value.Extras.PrerollBar1Applied);
        Assert.False(chart.Extras.PrerollBar1Applied);
    }

    [Theory]
    [InlineData("//MGR_PREROLL=BAR1_V2")]
    [InlineData("//MGR_PREROLL_SNAPSHOT_SHIFT=1920")]
    public async Task UnsupportedC2sPrerollMetadata_IsRejected(string marker)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "VERSION\t1.15.00\t1.15.00\n" + marker + "\n", ct);
            Assert.False((await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct)).Succeeded);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PendingSnapshotShiftBookmark_IsRejected()
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Extras.PrerollBar1Applied = true;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BookmarkEvent { Tick = 0, Tag = "MGR_PREROLL_SNAPSHOT_SHIFT=1920" });
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            var result = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.False(result.Succeeded);
            Assert.True(result.Diagnostics.HasError);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, ChartDialect.Original, 1920)]
    [InlineData(false, ChartDialect.Extended, 0)]
    [InlineData(true, ChartDialect.Original, 1920)]
    [InlineData(true, ChartDialect.Extended, 0)]
    public async Task ExplicitEditorMode_SelectsOriginWithoutChangingSofs(bool ugc, ChartDialect dialect, int expectedTick)
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = true;
        chart.Meta.BgmManualOffset = .25m;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = 0, Width = 2 });
        var path = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True(ugc
                ? (await new UgcChartWriter(path, chart, ChartDialect.Original).WriteAsync(ct)).Succeeded
                : (await new MgxcChartWriter(new MgxcWriteRequest(path, chart, ChartDialect.Original)).WriteAsync(ct)).Succeeded);
            var parsed = ugc
                ? await new UgcParser(new UgcParseRequest(path, TestAssets.Load(), dialect), TestMediaTool.Instance).ParseAsync(ct)
                : await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load(), dialect), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.True(parsed.Value!.Meta.BgmEnableBarOffset);
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(expectedTick, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
            Assert.Equal(expectedTick == 0 ? .25m : 2.25m, converted.Value.Meta.BgmRealOffset);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task InvalidEditorMode_IsRejected()
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "@VER\t8\n@BPM\t0'0\t120\n#0'0:t04\n", ct);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load(),
                (ChartDialect)999), TestMediaTool.Instance).ParseAsync(ct);
            Assert.False(parsed.Succeeded);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(ChartDialect.Auto, 0)]
    [InlineData(ChartDialect.Extended, 0)]
    [InlineData(ChartDialect.Original, 1920)]
    public async Task ExplicitEditorMode_TakesPrecedenceOverCreatorFallback(ChartDialect dialect, int expectedTick)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path,
                "' Created with Margrete v2.3.3\n@VER\t8\n@FLAG\tSOFFSET\tTRUE\n@BPM\t0'0\t120\n#0'0:t04\n", ct);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load(), dialect), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value!)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Equal(expectedTick, Assert.Single(converted.Value!.Notes.OfType<C.Tap>()).Tick.Original);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NativeMgxcSample_ChartAndAudioUseSamePreroll()
    {
        var path = Directory.Exists(ChartTestPaths.AssetsDirectory)
            ? Directory.EnumerateFiles(ChartTestPaths.AssetsDirectory, "*.mgxc").FirstOrDefault()
            : null;
        Assert.SkipWhen(path is null, "Optional native MGXC sample is missing.");
        var parsed = await new MgxcParser(new MgxcParseRequest(path!, TestAssets.Load()), TestMediaTool.Instance)
            .ParseAsync(TestContext.Current.CancellationToken);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var source = parsed.Value!;
        Assert.SkipWhen(source.Extras.C2sCoordinateOrigin || source.Extras.SourceKey is not null || source.Extras.SourceSnapshot.Length > 0,
            "Sample uses imported C2S coordinates.");
        var expectedOffset = source.Meta.BgmEnableBarOffset
            ? 1920 * source.Meta.BgmInitialNumerator / source.Meta.BgmInitialDenominator
            : 0;
        var result = new C2SChartConverter(new C2SConvertRequest(source)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(source.Notes.Children.Min(n => n.Tick.Original) + expectedOffset,
            result.Value!.Notes.Min(n => n.Tick.Original));
    }
}
