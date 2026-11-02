using System.Buffers.Binary;
using System.Diagnostics;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Core;
using PenguinTools.Core.Asset;
using PenguinTools.Media;
using Xunit;

using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public class MgxcRegressionTests
{
    [Theory]
    [InlineData(2, null, 80, 800)]
    [InlineData(2, 2.0, 80, 400)]
    [InlineData(3, 2.0, 80, 400)]
    [InlineData(4, 2.0, 20, 100)]
    [InlineData(4, 2.0, 80, 360)]
    [InlineData(4, 10.0, 400, 400)]
    [InlineData(4, null, 20, 200)]
    [InlineData(4, null, 80, 360)]
    [InlineData(4, 1e-28, 80, 360)]
    public async Task BinaryHeightScale_UsesNativeVersionSemantics(int version, double? scale, int height, int expected)
    {
        var chart = new UmgrModel.Chart();
        chart.Events.AppendChild(new UmgrModel.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new UmgrModel.BeatEvent { Bar = 0 });
        var tap = new UmgrModel.Tap { Tick = 480, Lane = 0, Width = 4 };
        var slide = new UmgrModel.AirSlide { Height = height };
        slide.AppendChild(new UmgrModel.AirSlideJoint { Tick = 960, Lane = 0, Width = 4, Height = height });
        tap.MakePair(slide);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(slide);
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            if (version < 4)
            {
                await LegacyEditorFixture.DowngradeAsync(path, ct);
            }
            var bytes = await File.ReadAllBytesAsync(path, ct);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), version);
            var field = bytes.AsSpan().IndexOf("HSCL"u8);
            if (scale is { } value)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(field + 8), value);
            }
            else
            {
                // Omission uses the legacy unit scale, not the writer's 10.
                bytes = [.. bytes.AsSpan(0, field), .. bytes.AsSpan(field + 16)];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
                var metaSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16));
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), metaSize - 16);
            }
            await File.WriteAllBytesAsync(path, bytes, ct);
            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            var restored = Assert.Single(parsed.Value!.Notes.Children.OfType<UmgrModel.AirSlide>());
            Assert.Equal((decimal)expected, restored.Height);
            Assert.Equal((decimal)expected, Assert.IsType<UmgrModel.AirSlideJoint>(Assert.Single(restored.Children)).Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e-28)]
    public async Task InvalidBinaryHeightScale_ReportsLocation(double scale)
    {
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var chart = new UmgrModel.Chart();
            chart.Notes.AppendChild(new UmgrModel.Tap { Tick = 480, Lane = 0, Width = 4 });
            await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            await LegacyEditorFixture.DowngradeAsync(path, ct);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var field = bytes.AsSpan().IndexOf("HSCL"u8);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(field + 8), scale);
            await File.WriteAllBytesAsync(path, bytes, ct);

            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);

            Assert.False(parsed.Succeeded);
            var diagnostic = Assert.Single(parsed.Diagnostics.Diagnostics,
                d => d.Message.Key == MsgKeys.MgCrit_Invalid_height_scale);
            Assert.Equal(path, diagnostic.Path);
            Assert.Equal(PenguinTools.Core.Diagnostic.Severity.Error, diagnostic.Severity);
            Assert.Contains(MsgKeys.MgCrit_Invalid_height_scale, System.Text.Json.JsonSerializer.Serialize(diagnostic.Message));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BinaryTimingValues_PreserveSmallSpeedsAndBpmFloatBoundary()
    {
        var chart = new UmgrModel.Chart();
        // Six-decimal rounding crosses the float32 midpoint below 240 BPM,
        // and turns these nonzero scroll and note speeds into zero.
        const decimal bpm = 239.9999924m;
        const decimal speed = 0.0000001m;
        chart.Meta.MainBpm = bpm;
        chart.Meta.BgmManualOffset = 0.0000001m;
        chart.Meta.BgmPreviewStart = 1.23456789m;
        chart.Meta.BgmPreviewStop = 2.34567891m;
        chart.Events.AppendChild(new UmgrModel.BpmEvent { Tick = 0, Bpm = bpm });
        chart.Events.AppendChild(new UmgrModel.BeatEvent { Bar = 0 });
        chart.Events.AppendChild(new UmgrModel.ScrollSpeedEvent { Tick = 0, Timeline = 0, Speed = speed });
        chart.Events.AppendChild(new UmgrModel.NoteSpeedEvent { Tick = 0, Speed = speed });
        chart.Notes.AppendChild(new UmgrModel.Tap { Tick = 480, Lane = 0, Width = 4 });
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.Equal((float)bpm, (float)parsed.Value!.Meta.MainBpm);
            Assert.Equal(chart.Meta.BgmManualOffset, parsed.Value.Meta.BgmManualOffset);
            Assert.Equal(chart.Meta.BgmPreviewStart, parsed.Value.Meta.BgmPreviewStart);
            Assert.Equal(chart.Meta.BgmPreviewStop, parsed.Value.Meta.BgmPreviewStop);
            Assert.Equal((float)bpm, (float)Assert.Single(parsed.Value.Events.Children.OfType<UmgrModel.BpmEvent>()).Bpm);
            Assert.Equal(speed, Assert.Single(parsed.Value.Events.Children.OfType<UmgrModel.ScrollSpeedEvent>()).Speed);
            Assert.Equal(speed, Assert.Single(parsed.Value.Events.Children.OfType<UmgrModel.NoteSpeedEvent>()).Speed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetCalculator_WithOnlyBarBasedBeatEvents_FormatsAfterTimeSignatureChanges()
    {
        var chart = new UmgrModel.Chart();
        chart.Events.AppendChild(new UmgrModel.BeatEvent { Bar = 38, Numerator = 6, Denominator = 4 });
        chart.Events.AppendChild(new UmgrModel.BeatEvent { Bar = 64, Numerator = 4, Denominator = 4 });
        chart.Events.AppendChild(new UmgrModel.BeatEvent { Bar = 80, Numerator = 6, Denominator = 4 });

        var calculator = chart.GetCalculator();

        Assert.Equal("72:1.0", calculator.FormatTick(161280));
        Assert.Equal([0, 0, 0],
            chart.Events.Children.OfType<UmgrModel.BeatEvent>().Select(e => e.Tick.Original).ToArray());
    }

    [Fact]
    public async Task ParseKnownSample_StillProducesChart()
    {
        var masterMgxcPath = Path.Combine(ChartTestPaths.AssetsDirectory, "Ver seX.mgxc");
        if (!File.Exists(masterMgxcPath) && Directory.Exists(ChartTestPaths.AssetsDirectory))
        {
            masterMgxcPath = Directory.EnumerateFiles(ChartTestPaths.AssetsDirectory, "*.mgxc").FirstOrDefault() ?? masterMgxcPath;
        }

        Assert.SkipWhen(!File.Exists(masterMgxcPath), $"Optional chart sample is missing: {masterMgxcPath}");

        var assets = TestAssets.Load();
        var parser = new MgxcParser(new MgxcParseRequest(masterMgxcPath, assets), TestMediaTool.Instance);

        var result = await parser.ParseAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value!.Notes.Children);
    }

}
