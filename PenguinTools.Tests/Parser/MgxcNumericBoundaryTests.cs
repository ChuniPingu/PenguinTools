using System.Buffers.Binary;
using System.Globalization;
using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class MgxcNumericBoundaryTests
{
    [Theory]
    [InlineData(239.99999237060547, 1.0000000596046448)]
    [InlineData(120.00000381469727, -1.0000000596046448)]
    [InlineData(120, 9.999999761589606e-8)]
    [InlineData(120, 1.0000000086552009e-20)]
    [InlineData(120, -1.0000000086552009e-20)]
    public async Task NativeDoubleMidpoints_KeepTheirFloatMeaningInC2s(double bpm, double speed)
    {
        var input = TestTempPaths.Create(".mgxc");
        var output = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new(input, Chart())).WriteAsync(ct);
            var bytes = await File.ReadAllBytesAsync(input, ct);
            WriteDouble(bytes, "mbpm"u8, 0, bpm);
            WriteDouble(bytes, "bpm "u8, 1, bpm);
            WriteDouble(bytes, "til "u8, 2, speed);
            WriteDouble(bytes, "smod"u8, 1, speed);
            await File.WriteAllBytesAsync(input, bytes, ct);

            var parsed = await new MgxcParser(new(input, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            Assert.Equal((float)bpm, NativeFloat(parsed.Value!.Meta.MainBpm.ToString("G29", CultureInfo.InvariantCulture)));
            var converted = new C2SChartConverter(new(parsed.Value)).Convert();
            Assert.True(converted.Succeeded);
            Assert.True((await new C2SChartWriter(new(output, converted.Value!)).WriteAsync(ct)).Succeeded);
            var rows = (await File.ReadAllLinesAsync(output, ct)).Select(C2SRoundTrip.Parts).ToArray();
            Assert.Equal((float)bpm, NativeFloat(Assert.Single(rows, p => p.Length > 0 && p[0] == "BPM")[3]));
            Assert.Equal((float)bpm, NativeFloat(Assert.Single(rows, p => p.Length > 0 && p[0] == "BPM_DEF")[1]));
            foreach (var kind in new[] { "SLP", "DCM" })
            {
                var speeds = rows.Where(p => p.Length > 4 && p[0] == kind).Select(p => NativeFloat(p[4])).ToArray();
                if ((float)speed != 1)
                {
                    Assert.NotEmpty(speeds);
                }
                Assert.All(speeds, actual => Assert.Equal((float)speed, actual));
            }
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task BinaryWriter_PreservesDoubleValuesBesideFloatMidpoints()
    {
        var chart = Chart();
        chart.Meta.MainBpm = 240.08264923095706m;
        Assert.Single(chart.Events.Children.OfType<U.BpmEvent>()).Bpm = chart.Meta.MainBpm;
        Assert.Single(chart.Events.Children.OfType<U.ScrollSpeedEvent>()).Speed = 0.025176567025482658m;
        Assert.Single(chart.Events.Children.OfType<U.NoteSpeedEvent>()).Speed = -0.025176567025482658m;
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new(path, chart)).WriteAsync(ct);
            var bytes = await File.ReadAllBytesAsync(path, ct);

            Assert.Equal(240.08264923095706, ReadDouble(bytes, "mbpm"u8, 0));
            Assert.Equal(240.08264923095706, ReadDouble(bytes, "bpm "u8, 1));
            Assert.Equal(0.025176567025482658, ReadDouble(bytes, "til "u8, 2));
            Assert.Equal(-0.025176567025482658, ReadDouble(bytes, "smod"u8, 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    [InlineData(1e-40)]
    public async Task UnrepresentableNativeNumber_WarnsAtAFieldLocation(double value)
    {
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new(path, Chart())).WriteAsync(ct);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            WriteDouble(bytes, "mbpm"u8, 0, value);
            await File.WriteAllBytesAsync(path, bytes, ct);

            var result = await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);

            Assert.True(result.Succeeded);
            Assert.Equal(value > 1 ? 100000m : 150m, result.Value!.Meta.MainBpm);
            var diagnostic = Assert.Single(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Number_adjusted);
            Assert.Equal(Severity.Warning, diagnostic.Severity);
            Assert.Equal(path, diagnostic.Path);
            Assert.Contains(MsgKeys.Warn_Number_adjusted, System.Text.Json.JsonSerializer.Serialize(diagnostic.Message));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static U.Chart Chart()
    {
        var chart = new U.Chart();
        chart.Meta.MainBpm = 120;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0 });
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Timeline = 0, Speed = 1 });
        chart.Events.AppendChild(new U.NoteSpeedEvent { Tick = 0, Speed = 1 });
        chart.Notes.AppendChild(new U.Tap { Tick = 480, Lane = 0, Width = 4 });
        return chart;
    }

    private static float NativeFloat(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    private static void WriteDouble(byte[] bytes, ReadOnlySpan<byte> name, int field, double value) =>
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(DoubleField(bytes, name, field)), value);

    private static double ReadDouble(byte[] bytes, ReadOnlySpan<byte> name, int field) =>
        BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(DoubleField(bytes, name, field)));

    private static int DoubleField(byte[] bytes, ReadOnlySpan<byte> name, int field)
    {
        var offset = bytes.AsSpan().IndexOf(name) + name.Length;
        Assert.True(offset >= name.Length);
        for (var i = 0; i < field; i++)
        {
            var type = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
            offset += type switch
            {
                0 or 1 => 4,
                2 => 8,
                3 => 12,
                4 => 4 + BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 2)),
                _ => throw new InvalidOperationException()
            };
        }
        Assert.Equal(3, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset)));
        return offset + 4;
    }
}
