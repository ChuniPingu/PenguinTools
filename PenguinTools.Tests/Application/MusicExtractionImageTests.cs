using System.Xml.Linq;
using PenguinTools.Application;
using PenguinTools.Tests.Media;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed class MusicExtractionImageTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("penguintools-extract-image-");

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".unknown")]
    [InlineData(".png")]
    public async Task MusicExtraction_ProducesRealPngFromRasterOverride(string extension)
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(_root.FullName, "封面" + extension);
        await File.WriteAllBytesAsync(source, extension == ".png"
            ? RasterFixtures.Png(128, 128, 128, 2, 100000)
            : Convert.FromBase64String(RasterFixtures.Jpeg), ct);
        var chart = Path.Combine(_root.FullName, "chart.c2s");
        await File.WriteAllTextAsync(chart,
            "VERSION\t1.15.00\t1.15.00\nRESOLUTION\t384\nBPM_DEF\t120\nMET_DEF\t4\t4\n" +
            "BPM\t0\t0\t120\nMET\t0\t0\t4\t4\nTAP\t1\t0\t0\t4\n", ct);
        var xml = Path.Combine(_root.FullName, "Music.xml");
        var document = new XDocument(new XElement("MusicData",
            new XElement("name", new XElement("id", 1234), new XElement("str", "Image test")),
            new XElement("fumens", new XElement("MusicFumenData",
                new XElement("enable", true), new XElement("type", new XElement("id", 3)),
                new XElement("file", new XElement("path", chart))))));
        await File.WriteAllTextAsync(xml, document.ToString(), ct);
        using var application = PenguinToolsApplication.CreateDefault();
        var result = await application.ExtractMusicAsync(new MusicExtractRequest(
            xml, Path.Combine(_root.FullName, "output"), JacketPath: source, NoAudio: true), cancellationToken: ct);
        Assert.True(result.Succeeded, result.ToString());
        var output = Assert.Single(result.Value!.Artifacts, a => a.Kind == "jacket.png").Path;
        var chartOutput = Assert.Single(result.Value.Artifacts, a => a.Kind == "chart.mgxc").Path;
        var parsed = await new PenguinTools.Chart.Parser.mgxc.MgxcParser(new(chartOutput, TestAssets.Load()),
            TestMediaTool.Instance).ParseAsync(ct);
        Assert.True(parsed.Succeeded, parsed.ToString());
        Assert.Equal(output, parsed.Value!.Meta.FullJacketFilePath);
        var pixels = await RasterFixtures.ReadPngPixelsAsync(output, 2, 3, ct);
        if (extension == ".png")
        {
            Assert.Equal(new byte[] { 188, 188, 188, 2 }, pixels[..4]);
        }
        else
        {
            Assert.InRange(pixels[0], 29, 35);
            Assert.InRange(pixels[1], 93, 99);
            Assert.InRange(pixels[2], 157, 163);
            Assert.Equal(255, pixels[3]);
        }
    }

    public void Dispose() => _root.Delete(recursive: true);
}
