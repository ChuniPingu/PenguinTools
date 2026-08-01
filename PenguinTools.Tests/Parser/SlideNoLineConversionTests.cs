using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
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

            var markers = (await File.ReadAllLinesAsync(
                    outputPath,
                    TestContext.Current.CancellationToken))
                .Select(line => line.Split('\t'))
                .Where(fields =>
                    fields.Length > 8 &&
                    fields[8] is "SLD" or "NCL")
                .Select(fields => fields[8])
                .ToArray();

            Assert.Equal(
                new[] { "NCL", "SLD", "NCL" },
                markers);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}