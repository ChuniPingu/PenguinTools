using System.Buffers.Binary;
using System.Text;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Writer.mgxc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class MgxcAirHeadTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public async Task LongAirInversionFollowsTheArrow(bool slide, int version)
    {
        var chart = new U.Chart();
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0 });
        var tap = new U.Tap { Tick = 480, Lane = 2, Width = 4 };
        U.NegativeNote air = slide ? new U.AirSlide { Height = 80 } : new U.AirHold();
        U.Note end = slide
            ? new U.AirSlideJoint { Tick = 960, Lane = 2, Width = 4, Height = 80 }
            : new U.AirHoldJoint { Tick = 960 };
        air.AppendChild(end);
        tap.MakePair(air);
        chart.Notes.AppendChild(tap);
        chart.Notes.AppendChild(air);
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            if (version == 2)
            {
                await LegacyEditorFixture.DowngradeAsync(path, ct);
            }
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var data = bytes.AsSpan().IndexOf("dat2"u8) + 8;
            // The editor's Invert operation changes the AIR record. The entire
            // AIR-HOLD or AIR-SLIDE inherits it despite the long record's flag.
            var arrow = Enumerable.Range(0, (bytes.Length - data) / 20)
                .Select(i => data + i * 20).Single(i => bytes[i] == 7);
            bytes[arrow + 3] = 1;
            await File.WriteAllBytesAsync(path, bytes, ct);
            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            var restored = Assert.Single(parsed.Value!.Notes.Children.OfType<U.NegativeNote>());
            if (slide)
            {
                var airSlide = Assert.IsType<U.AirSlide>(restored);
                Assert.Equal(Color.PNK, airSlide.Color);
            }
            else
            {
                Assert.Equal(Color.PNK, Assert.IsType<U.AirHold>(restored).Color);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false, "tap")]
    [InlineData(true, "tap")]
    [InlineData(false, "hold")]
    [InlineData(true, "hold")]
    [InlineData(false, "slide")]
    [InlineData(true, "slide")]
    [InlineData(false, "carrier")]
    [InlineData(true, "carrier")]
    public async Task LongAir_WritesOneExplicitAirImmediatelyBeforeHead(bool slide, string parentKind)
    {
        var chart = new U.Chart();
        chart.Meta.ExVersion = 1;
        chart.Meta.ExLongEnabled = true;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Tick = 0, Bar = 0 });
        chart.Events.AppendChild(new U.ScrollSpeedEvent { Tick = 0, Timeline = 3, Speed = 2 });
        U.PositiveNote parent;
        U.Note root;
        switch (parentKind)
        {
            case "hold":
                var hold = new U.Hold { Tick = 0, Lane = 2, Width = 3, Timeline = 3 };
                parent = new U.HoldJoint { Tick = 480, Timeline = 3 };
                hold.AppendChild(parent);
                root = hold;
                break;
            case "slide":
                var groundSlide = new U.Slide { Tick = 0, Lane = 0, Width = 3, Timeline = 3 };
                parent = new U.SlideJoint { Tick = 480, Lane = 2, Width = 3, Timeline = 3 };
                groundSlide.AppendChild(parent);
                root = groundSlide;
                break;
            case "carrier":
                parent = new U.ExTap
                {
                    Tick = 480,
                    Lane = 2,
                    Width = 3,
                    Timeline = 3,
                    Role = U.ExTapRole.AirActionCarrier,
                    AirActionParent = U.AirActionCarrierParent.Tap
                };
                root = parent;
                break;
            default:
                parent = new U.Tap { Tick = 480, Lane = 2, Width = 3, Timeline = 3 };
                root = parent;
                break;
        }

        U.NegativeNote action;
        if (slide)
        {
            var air = new U.AirSlide { Height = 37, Color = Color.PNK, Timeline = 3 };
            air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 4, Width = 3, Height = 50, Joint = Joint.C, Timeline = 3 });
            air.AppendChild(new U.AirSlideJoint { Tick = 1440, Lane = 6, Width = 3, Height = 70, Joint = Joint.D, Timeline = 3 });
            action = air;
        }
        else
        {
            var air = new U.AirHold { Color = Color.PNK, Timeline = 3 };
            air.AppendChild(new U.AirHoldJoint { Tick = 960, Joint = Joint.D, Timeline = 3 });
            air.AppendChild(new U.AirHoldJoint { Tick = 1440, Joint = Joint.D, Timeline = 3 });
            action = air;
        }
        parent.MakePair(action);
        chart.Notes.AppendChild(root);
        chart.Notes.AppendChild(action);
        var path = TestTempPaths.Create(".mgxc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new MgxcChartWriter(new MgxcWriteRequest(path, chart)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var records = ReadRecords(await File.ReadAllBytesAsync(path, ct));
            var head = records.FindIndex(r => r[0] == (slide ? 0x09 : 0x08) && r[1] == 0x01);

            // Inspect the actual binary payload: our parser folds this AIR into
            // the long note and also accepts files where the AIR was omitted.
            var arrow = Assert.Single(records, r => r[0] == 0x07);
            Assert.True(head > 0);
            Assert.Same(arrow, records[head - 1]);
            Assert.Equal(new byte[] { 0x07, 0, 2, 1, 0, 0, 3, 2, 80, 0, 0, 0, 0xE0, 1, 0, 0, 3, 0, 0, 0 }, arrow);
            Assert.Equal(480, BinaryPrimitives.ReadInt32LittleEndian(records[head].AsSpan(12)));
            Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(records[head].AsSpan(16)));

            var parsed = await new MgxcParser(new MgxcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Value!.Notes.Children.OfType<U.Air>());
            var restored = Assert.Single(parsed.Value.Notes.Children.OfType<U.NegativeNote>());
            Assert.NotNull(restored.PairNote);
            Assert.Same(restored, restored.PairNote.PairNote);
            Assert.Equal(2, restored.Children.Count);
            Assert.Equal(480, restored.Tick.Original);

            var converted = new C2SChartConverter(new C2SConvertRequest(parsed.Value)).Convert();
            Assert.True(converted.Succeeded, converted.ToString());
            Assert.Empty(converted.Value!.Notes.OfType<C.Air>());
            Assert.Equal(2, converted.Value.Notes.Count(n => slide ? n is C.AirSlide : n is C.AirHold));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static List<byte[]> ReadRecords(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        Assert.Equal("MGXC", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        Assert.Equal(bytes.Length - 8, reader.ReadInt32());
        Assert.Equal(4, reader.ReadInt32());
        while (stream.Position < stream.Length)
        {
            var block = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var end = reader.ReadInt32() + stream.Position;
            if (block != "dat2")
            {
                stream.Position = end;
                continue;
            }

            List<byte[]> records = [];
            while (stream.Position < end)
            {
                var record = reader.ReadBytes(20);
                Assert.Equal(20, record.Length);
                records.Add(record);
                if (record[0] == 0x0A && record[1] == 0x01)
                {
                    reader.ReadInt32();
                }
            }
            Assert.Equal(end, stream.Position);
            return records;
        }
        Assert.Fail("MGXC dat2 block is missing.");
        return [];
    }
}
