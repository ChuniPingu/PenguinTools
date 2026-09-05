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

public sealed class C2sAirCrashConversionTests
{
    [Fact]
    public async Task MgxcWriter_PreservesAirCrashAxis()
    {
        var source = new C2sChart();

        source.Notes.Add(new AirCrash
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 4,
            EndHeight = 90m
        });

        source.Notes.Add(new AirCrash
        {
            Tick = 960,
            Lane = 4,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisZ,
            EndTick = 1440,
            EndLane = 6,
            EndWidth = 4,
            EndHeight = 90m
        });

        var converted = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var directory = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "air-crash-axis.mgxc");

        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new MgxcParser(
                    new MgxcParseRequest(path, TestAssets.Load()),
                    TestMediaTool.Instance)
                .ParseAsync(TestContext.Current.CancellationToken);

            Assert.True(parsed.Succeeded, parsed.ToString());

            var crashes = parsed.Value!.Notes.Children
                .OfType<PenguinTools.Chart.Models.umgr.AirCrash>()
                .OrderBy(x => x.Tick.Original)
                .ToArray();

            Assert.Equal(2, crashes.Length);
            Assert.Equal(AirLadderAttr.AxisY, crashes[0].Attr);
            Assert.Equal(AirLadderAttr.AxisZ, crashes[1].Attr);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MgxcWriter_WritesOnlyFinalAirCrashJointAsEnd()
    {
        var source = new C2sChart();

        source.Notes.Add(new AirCrash
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 4,
            EndHeight = 90m
        });

        source.Notes.Add(new AirCrash
        {
            Tick = 480,
            Lane = 2,
            Width = 4,
            Height = 90m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 4,
            EndHeight = 100m
        });

        var converted = new UgcChartConverter(
            new UgcConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var convertedCrash = Assert.Single(
            converted.Value!.Notes.Children
                .OfType<PenguinTools.Chart.Models.umgr.AirCrash>());

        Assert.Equal(
            2,
            convertedCrash.Children
                .OfType<PenguinTools.Chart.Models.umgr.AirCrashJoint>()
                .Count());

        var directory = Path.Combine(
            Path.GetTempPath(),
            "PenguinToolsTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "air-crash-joints.mgxc");

        try
        {
            var written = await new MgxcChartWriter(
                    new MgxcWriteRequest(path, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            var airCrashLongAttrs = new List<sbyte>();

            await using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            Assert.Equal(
                "MGXC",
                System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)));

            reader.ReadInt32(); // 文件大小
            reader.ReadInt32(); // MGXC 版本

            while (stream.Position + 8 <= stream.Length)
            {
                var blockName = System.Text.Encoding.ASCII.GetString(
                    reader.ReadBytes(4));

                var blockSize = reader.ReadInt32();
                var blockEnd = stream.Position + blockSize;

                if (blockName != "dat2")
                {
                    stream.Position = blockEnd;
                    continue;
                }

                while (stream.Position + 20 <= blockEnd)
                {
                    var noteType = reader.ReadSByte();
                    var longAttr = reader.ReadSByte();

                    reader.ReadSByte(); // direction
                    reader.ReadSByte(); // exAttr
                    reader.ReadSByte(); // variation
                    reader.ReadSByte(); // lane
                    reader.ReadInt16(); // width
                    reader.ReadInt32(); // height
                    reader.ReadInt32(); // tick
                    reader.ReadInt32(); // timeline

                    if (noteType == 0x0A)
                        airCrashLongAttrs.Add(longAttr);

                    // AirCrush Begin 比普通记录多一个 density/option 字段。
                    if (noteType == 0x0A && longAttr == 0x01)
                        reader.ReadInt32();
                }

                stream.Position = blockEnd;
            }

            Assert.Equal(
                new sbyte[] { 0x01, 0x03, 0x05 },
                airCrashLongAttrs.ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AirCrashSegments_AreReconstructedAsSinglePath()
    {
        var source = new C2sChart();

        source.Notes.Add(new AirCrash
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 4,
            EndHeight = 90m
        });

        source.Notes.Add(new AirCrash
        {
            Tick = 480,
            Lane = 2,
            Width = 4,
            Height = 90m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 4,
            EndHeight = 100m
        });

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());

        var crash = Assert.Single(
            result.Value!.Notes.Children.OfType<PenguinTools.Chart.Models.umgr.AirCrash>());

        Assert.Equal(AirLadderAttr.AxisY, crash.Attr);

        var joints = crash.Children
            .OfType<PenguinTools.Chart.Models.umgr.AirCrashJoint>()
            .ToArray();

        Assert.Equal(2, joints.Length);
        Assert.Equal(480, joints[0].Tick.Original);
        Assert.Equal(960, joints[1].Tick.Original);
    }
    [Fact]
    public void AirCrashSegments_WithDifferentAxis_StartSeparatePaths()
    {
        var source = new C2sChart();

        source.Notes.Add(new AirCrash
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            Height = 80m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisY,
            EndTick = 480,
            EndLane = 2,
            EndWidth = 4,
            EndHeight = 90m
        });

        source.Notes.Add(new AirCrash
        {
            Tick = 480,
            Lane = 2,
            Width = 4,
            Height = 90m,
            Density = 20,
            Color = Color.DEF,
            Attr = AirLadderAttr.AxisZ,
            EndTick = 960,
            EndLane = 4,
            EndWidth = 4,
            EndHeight = 100m
        });

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());

        var crashes = result.Value!.Notes.Children
            .OfType<PenguinTools.Chart.Models.umgr.AirCrash>()
            .ToArray();

        Assert.Equal(2, crashes.Length);
    }
    [Fact]
    public void C2sBaseHeads_AreReconstructedByPaths()
    {
        var source = new C2sChart();

        source.Notes.Add(new Tap
        {
            Tick = 0, Lane = 0, Width = 1
        });

        source.Notes.Add(new ExTap
        {
            Tick = 100, Lane = 1, Width = 1, Effect = ExEffect.UP
        });

        source.Notes.Add(new Damage
        {
            Tick = 200, Lane = 2, Width = 1
        });

        source.Notes.Add(new Hold
        {
            Tick = 300, Lane = 3, Width = 1,
            EndTick = 400, EndLane = 3, EndWidth = 1
        });

        source.Notes.Add(new Hold
        {
            Tick = 500, Lane = 4, Width = 1,
            EndTick = 600, EndLane = 4, EndWidth = 1,
            Effect = ExEffect.UP
        });

        source.Notes.Add(new Slide
        {
            Tick = 700, Lane = 5, Width = 1,
            EndTick = 800, EndLane = 6, EndWidth = 1,
            Joint = Joint.D
        });

        source.Notes.Add(new Slide
        {
            Tick = 800, Lane = 6, Width = 1,
            EndTick = 900, EndLane = 7, EndWidth = 1,
            Joint = Joint.D
        });

        source.Notes.Add(new Slide
        {
            Tick = 1000, Lane = 9, Width = 1,
            EndTick = 1100, EndLane = 10, EndWidth = 1,
            Joint = Joint.D
        });

        source.Notes.Add(new Flick
        {
            Tick = 1200, Lane = 11, Width = 1
        });

        var result = new UgcChartConverter(new UgcConvertRequest(source)).Convert();

        Assert.True(result.Succeeded, result.ToString());

        var roots = result.Value!.Notes.Children;

        var baseHeadCount = roots.Count(note =>
            note is PenguinTools.Chart.Models.umgr.Tap
                or PenguinTools.Chart.Models.umgr.ExTap
                or PenguinTools.Chart.Models.umgr.Damage
                or PenguinTools.Chart.Models.umgr.Hold
                or PenguinTools.Chart.Models.umgr.Slide);

        Assert.Equal(7, baseHeadCount);
    }
}
