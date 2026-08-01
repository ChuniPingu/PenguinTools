using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using Xunit;

namespace PenguinTools.Tests.Parser;

using c2s = PenguinTools.Chart.Models.c2s;
using umgr = PenguinTools.Chart.Models.umgr;

public sealed class SlideNoLineConversionTests
{
    [Fact]
    public async Task MixedNoLineSegments_WriteExpectedC2sMarkers()
    {
        var chart = CreateMixedNoLineSlideChart();

        var converted = new C2SChartConverter(
            new C2SConvertRequest(chart)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());

        var segments = converted.Value!.Notes
            .OfType<c2s.Slide>()
            .OrderBy(x => x.Tick.Original)
            .ToArray();

        Assert.Equal(3, segments.Length);

        Assert.Equal(
            new[] { true, false, true },
            segments.Select(x => x.NoLine).ToArray());

        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"penguin-tools-noline-{Guid.NewGuid():N}.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(outputPath, converted.Value))
                .WriteAsync(TestContext.Current.CancellationToken);

            Assert.True(written.Succeeded, written.ToString());

            Assert.Equal(
                new[] { "NCL", "SLD", "NCL" },
                await ReadSlideMarkersAsync(outputPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task MixedNoLineSegments_RoundTripThroughC2sPreservesMarkers()
    {
        var chart = CreateMixedNoLineSlideChart();
        var converted = new C2SChartConverter(
            new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());

        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"penguin-tools-noline-roundtrip-{Guid.NewGuid():N}.c2s");

        try
        {
            var written = await new C2SChartWriter(
                    new C2SWriteRequest(outputPath, converted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());

            var parsed = await new C2SParser(new C2SParseRequest(outputPath))
                .ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());

            var parsedSegments = parsed.Value!.Notes
                .OfType<c2s.Slide>()
                .OrderBy(x => x.Tick.Original)
                .ToArray();

            Assert.Equal(3, parsedSegments.Length);
            Assert.Equal(
                new[] { true, false, true },
                parsedSegments.Select(x => x.NoLine).ToArray());

            var toUmgr = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
            Assert.True(toUmgr.Succeeded, toUmgr.ToString());

            var umgrSlide = Assert.Single(toUmgr.Value!.Notes.Children.OfType<umgr.Slide>());
            Assert.True(umgrSlide.NoLine);

            var joints = umgrSlide.Children.OfType<umgr.SlideJoint>().ToArray();
            Assert.Equal(3, joints.Length);
            Assert.False(joints[0].NoLine);
            Assert.True(joints[1].NoLine);
            Assert.False(joints[2].NoLine);

            var reconverted = new C2SChartConverter(
                new C2SConvertRequest(toUmgr.Value)).Convert();
            Assert.True(reconverted.Succeeded, reconverted.ToString());

            var rewritePath = outputPath + ".rewrite.c2s";
            var rewritten = await new C2SChartWriter(
                    new C2SWriteRequest(rewritePath, reconverted.Value!))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(rewritten.Succeeded, rewritten.ToString());

            Assert.Equal(
                new[] { "NCL", "SLD", "NCL" },
                await ReadSlideMarkersAsync(rewritePath, TestContext.Current.CancellationToken));

            File.Delete(rewritePath);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static umgr.Chart CreateMixedNoLineSlideChart()
    {
        var chart = new umgr.Chart();

        // A -> B : NCL
        // B -> C : SLD
        // C -> D : NCL
        var slide = new umgr.Slide
        {
            Tick = 0,
            Lane = 0,
            Width = 4,
            NoLine = true
        };

        slide.AppendChild(new umgr.SlideJoint
        {
            Tick = 480,
            Lane = 2,
            Width = 4,
            Joint = Joint.D,
            NoLine = false
        });

        slide.AppendChild(new umgr.SlideJoint
        {
            Tick = 960,
            Lane = 4,
            Width = 4,
            Joint = Joint.D,
            NoLine = true
        });

        slide.AppendChild(new umgr.SlideJoint
        {
            Tick = 1440,
            Lane = 6,
            Width = 4,
            Joint = Joint.D
        });

        chart.Notes.AppendChild(slide);
        return chart;
    }

    private static async Task<string[]> ReadSlideMarkersAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return (await File.ReadAllLinesAsync(path, cancellationToken))
            .Select(line => line.Split('\t'))
            .Where(fields =>
                fields.Length > 8 &&
                fields[8] is "SLD" or "NCL")
            .Select(fields => fields[8])
            .ToArray();
    }
}
