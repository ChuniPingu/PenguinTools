using System.Buffers.Binary;
using System.Text;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.mgxc;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class MgxcRoleMetadataTests
{
    [Theory]
    [InlineData("none", true)]
    [InlineData("title", true)]
    [InlineData("unrelated", true)]
    [InlineData("head", false)]
    [InlineData("tail", false)]
    public async Task NativeExTapFields_AndCheckedRolesSurviveIndependentEdits(string edit, bool retainRole)
    {
        var chart = Chart();
        chart.Notes.AppendChild(new U.ExTap
        {
            Tick = 0,
            Lane = 4,
            Width = 4,
            Effect = ExEffect.UP,
            Role = U.ExTapRole.Explicit
        });
        var hold = new U.Hold { Tick = 0, Lane = 4, Width = 4 };
        hold.AppendChild(new U.HoldJoint { Tick = 480 });
        chart.Notes.AppendChild(hold);
        chart.Notes.AppendChild(new U.Tap { Tick = 1920, Lane = 0, Width = 2 });
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var records = Records(bytes).ToArray();
            var ex = Assert.Single(records, offset => bytes[offset] == 2);
            Assert.Equal(80, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ex + 8)));
            Assert.Equal(0, bytes[ex + 5]);
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ex + 10)));
            switch (edit)
            {
                case "title":
                    "Retitled"u8.CopyTo(bytes.AsSpan(bytes.AsSpan().IndexOf("Original"u8)));
                    break;
                case "unrelated":
                    bytes[Assert.Single(records, offset => bytes[offset] == 1) + 7] = 2;
                    break;
                case "head":
                    bytes[Assert.Single(records, offset => bytes[offset] == 5 && bytes[offset + 1] == 1) + 6] = 3;
                    break;
                case "tail":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(
                        Assert.Single(records, offset => bytes[offset] == 5 && bytes[offset + 1] == 5) + 12), 960);
                    break;
            }
            await File.WriteAllBytesAsync(path, bytes, ct);
            var result = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(result.Succeeded, result.ToString());
            var parsed = result.Value!;
            Assert.Equal(retainRole ? U.ExTapRole.Explicit : U.ExTapRole.SharedLongCarrier,
                Assert.Single(parsed.Notes.Children.OfType<U.ExTap>()).Role);
            Assert.Equal(retainRole ? null : ExEffect.UP,
                Assert.Single(parsed.Notes.Children.OfType<U.Hold>()).Effect);
            if (edit == "tail")
            {
                Assert.Equal(960, Assert.Single(parsed.Notes.Children.OfType<U.Hold>()).LastChild!.Tick.Original);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AirCarrier_UsesASingleNoteParentAndDiscardsItsRoleAfterPathEdits(bool editPath)
    {
        var chart = Chart();
        var slide = new U.Slide { Tick = 0, Lane = 0, Width = 4 };
        slide.AppendChild(new U.SlideJoint { Tick = 480, Lane = 4, Width = 4 });
        slide.AppendChild(new U.SlideJoint { Tick = 960, Lane = 8, Width = 4 });
        chart.Notes.AppendChild(slide);
        var carrier = new U.ExTap
        {
            Tick = 480,
            Lane = 4,
            Width = 4,
            Role = U.ExTapRole.AirActionCarrier,
            AirActionParent = U.AirActionCarrierParent.Slide
        };
        var air = new U.Air { PairNote = carrier };
        carrier.PairNote = air;
        chart.Notes.AppendChild(carrier);
        chart.Notes.AppendChild(air);
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var records = Records(bytes).ToArray();
            var airRecord = Assert.Single(records, offset => bytes[offset] == 7);
            Assert.Equal(2, bytes[airRecord - 20]);
            Assert.Equal(80, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(airRecord - 12)));
            if (editPath)
            {
                var head = Assert.Single(records, offset => bytes[offset] == 6 && bytes[offset + 1] == 1);
                bytes[head + 6] = 3;
                await File.WriteAllBytesAsync(path, bytes, ct);
            }
            var result = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(result.Succeeded, result.ToString());
            var parsed = result.Value!;
            var parent = Assert.IsType<U.ExTap>(Assert.Single(parsed.Notes.Children.OfType<U.Air>()).PairNote);
            Assert.Equal(editPath ? U.ExTapRole.Explicit : U.ExTapRole.AirActionCarrier, parent.Role);
            Assert.Equal((480, 4, 4), (parent.Tick.Original, parent.Lane, parent.Width));
            Assert.Null(Assert.IsType<U.SlideJoint>(Assert.Single(parsed.Notes.Children.OfType<U.Slide>()).LastChild).PairNote);
            var converted = new C2SChartConverter(new C2SConvertRequest(parsed)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            var outputAir = Assert.Single(converted.Value!.Notes.OfType<PenguinTools.Chart.Models.c2s.Air>());
            if (editPath)
            {
                Assert.IsType<PenguinTools.Chart.Models.c2s.ExTap>(outputAir.Parent);
            }
            else
            {
                Assert.IsType<PenguinTools.Chart.Models.c2s.Slide>(outputAir.Parent);
                Assert.Empty(converted.Value.Notes.OfType<PenguinTools.Chart.Models.c2s.ExTap>());
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NativeInferredRole_DoesNotNeedASnapshot()
    {
        var chart = Chart();
        chart.Notes.AppendChild(new U.ExTap { Tick = 0, Lane = 0, Width = 4, Role = U.ExTapRole.Explicit });
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct)).Succeeded);
            var result = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(result.Succeeded, result.ToString());
            Assert.Empty(result.Value!.Extras.MgxcNoteSnapshots);
            Assert.Equal(U.ExTapRole.Explicit, Assert.Single(result.Value.Notes.Children.OfType<U.ExTap>()).Role);
        }
        finally { File.Delete(path); }
    }

    private static U.Chart Chart()
    {
        var chart = new U.Chart();
        chart.Meta.Title = "Original";
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        return chart;
    }

    private static IEnumerable<int> Records(byte[] bytes)
    {
        var start = 12;
        while (!bytes.AsSpan(start, 4).SequenceEqual("dat2"u8))
        {
            start += 8 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(start + 4));
        }
        var end = start + 8 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(start + 4));
        for (var offset = start + 8; offset < end; offset += bytes[offset] == 10 && bytes[offset + 1] == 1 ? 24 : 20)
        {
            yield return offset;
        }
    }
}
