using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.Core;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed partial class PenguinToolsApplicationTests
{
    [Fact]
    public async Task FailedOptionBuild_KeepsAllWalls_ThenSuccessfulRemovalWritesEmptyManifest()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-wall-publication-");
        var input = Path.Combine(root.FullName, "input");
        Directory.CreateDirectory(input);
        using var store = new TrackingAssetStore(root.FullName);
        using var app = CreateInjectedApplication(root.FullName, store);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var chart = await WriteChartFixtureAsync(input);
            var text = await File.ReadAllTextAsync(chart, ct);
            await File.AppendAllTextAsync(chart, "@CMT\t#meta wall jacket.png wide\n", ct);
            var output = Path.Combine(root.FullName, "A123");
            var settings = new OptionBuildOverrides(OptionName: "A123", ConvertChart: true,
                ConvertJacket: false, ConvertAudio: false, ConvertBackground: false, ConvertWall: true,
                GenerateEventXml: false, CustomReleaseTagXml: false);
            var request = new OptionBuildRequest(input, output, SkipConfig: true, Overrides: settings);
            var initial = await app.BuildOptionAsync(request, cancellationToken: ct);
            Assert.True(initial.Succeeded, initial.ToString());
            var manifest = Path.Combine(output, "wallbind.json");
            var dds = Path.Combine(output, "ddsFieldWall", "ddsFieldWall1001000", "CHU_UI_Fieldwall_1001000.dds");
            var oldManifest = await File.ReadAllBytesAsync(manifest, ct);
            await File.WriteAllTextAsync(dds, "previous texture", ct);

            var brokenChart = Path.Combine(input, "broken.ugc");
            await File.WriteAllTextAsync(brokenChart, text.Replace("@SONGID\t1000", "@SONGID\t2000")
                .Replace("@BGM\taudio.wav", "@BGM\tmissing.wav"), ct);
            var failed = await app.BuildOptionAsync(request with
            {
                Overrides = settings with { ConvertAudio = true }
            }, cancellationToken: ct);
            Assert.False(failed.Succeeded);
            Assert.True(failed.Diagnostics.HasError);
            Assert.Equal(oldManifest, await File.ReadAllBytesAsync(manifest, ct));
            Assert.Equal("previous texture", await File.ReadAllTextAsync(dds, ct));

            File.Delete(brokenChart);
            await File.WriteAllTextAsync(chart, text, ct);
            var cleared = await app.BuildOptionAsync(request, cancellationToken: ct);
            Assert.True(cleared.Succeeded, cleared.ToString());
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, ct));
            Assert.Empty(json.RootElement.GetProperty("bindings").EnumerateArray());
            Assert.Equal("previous texture", await File.ReadAllTextAsync(dds, ct));
        }
        finally { root.Delete(true); }
    }
}
