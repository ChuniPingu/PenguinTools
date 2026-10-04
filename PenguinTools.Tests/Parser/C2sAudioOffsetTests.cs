using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sAudioOffsetTests
{
    [Theory]
    [InlineData(true, 4, 4, 1920)]
    [InlineData(true, 3, 8, 720)]
    [InlineData(false, 4, 4, 0)]
    public async Task NativeMgxc_ShiftsChartByAudioPreroll(bool enabled, int numerator, int denominator, int offset)
    {
        var source = new U.Chart();
        source.Meta.BgmEnableBarOffset = enabled;
        source.Meta.BgmManualOffset = .25m;
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
            var parsed = await new MgxcParser(new MgxcParseRequest(mgxcPath, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
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
                Assert.Equal(offset, e.Tick.Original);
                Assert.Equal(1920, e.Length.Original);
            });
            Assert.Equal(.25m + (enabled ? initialMeasure / 960m : 0m), chart.Meta.BgmRealOffset);
            Assert.Equal(0, parsed.Value!.Notes.Children[0].Tick.Original);

            Assert.True((await new C2SChartWriter(new C2SWriteRequest(c2sPath, chart)).WriteAsync(ct)).Succeeded);
            var written = await new C2SParser(new C2SParseRequest(c2sPath)).ParseAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            Assert.Equal(offset, Assert.Single(written.Value!.Notes.OfType<C.Tap>()).Tick.Original);
            Assert.Equal(960 + offset, Assert.Single(written.Value.Notes.OfType<C.Hold>()).EndTick.Original);
        }
        finally
        {
            File.Delete(mgxcPath);
            File.Delete(c2sPath);
        }
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
