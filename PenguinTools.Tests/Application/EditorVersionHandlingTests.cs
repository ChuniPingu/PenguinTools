using System.Text;
using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.CLI;
using PenguinTools.Core;
using PenguinTools.Core.Metadata;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed partial class PenguinToolsApplicationTests
{
    [Theory]
    [InlineData(".mgxc")]
    [InlineData(".ugc")]
    public async Task NewEditor_InspectionAndConversionParseNotes(string extension)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var input = Path.Combine(root, "new" + extension);
            var version = extension == ".mgxc" ? 4 : 9;
            await WriteNewEditorFixtureAsync(input, version, ct);
            var original = await File.ReadAllBytesAsync(input, ct);
            var inspected = await application.InspectChartAsync(new ChartInspectRequest(input), ct);
            Assert.True(inspected.Succeeded);
            Assert.NotNull(inspected.Value);
            Assert.True(inspected.Value.NotesParsed);
            Assert.Equal(version, inspected.Value.FormatVersion);
            Assert.NotNull(inspected.Value.Metadata);
            Assert.Equal("新谱面", inspected.Value.Chart.Title);
            Assert.Equal("Artist", inspected.Value.Chart.Artist);
            Assert.Equal("Designer", inspected.Value.Chart.Designer);
            Assert.Equal("Ultima", inspected.Value.Chart.Difficulty);
            Assert.Equal(1234, inspected.Value.Chart.SongId);
            Assert.Equal(14.5m, inspected.Value.Chart.Level);
            Assert.Equal(180m, inspected.Value.Chart.MainBpm);
            Assert.False(inspected.Diagnostics.HasError);
            Assert.DoesNotContain(inspected.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Warn_Chart_metadata_only);
            var json = JsonSerializer.Serialize(inspected.Value, CliJsonSerializerContext.Default.ChartInspectResult);
            using var payload = JsonDocument.Parse(json);
            Assert.True(payload.RootElement.GetProperty("notesParsed").GetBoolean());
            Assert.True(payload.RootElement.TryGetProperty("metadata", out _));

            var output = Path.Combine(root, "existing.c2s");
            await File.WriteAllTextAsync(output, "keep output", ct);
            var converted = await application.ConvertChartAsync(new ChartConvertRequest(input, output), cancellationToken: ct);
            Assert.True(converted.Succeeded);
            Assert.DoesNotContain(converted.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Chart_version_too_new);
            Assert.NotEqual("keep output", await File.ReadAllTextAsync(output, ct));
            Assert.Equal(original, await File.ReadAllBytesAsync(input, ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".mgxc", 5)]
    [InlineData(".ugc", 10)]
    public async Task UnverifiedEditorVersion_InspectionStillFails(string extension, int version)
    {
        var ct = TestContext.Current.CancellationToken;
        var input = TestTempPaths.Create(extension);
        using var application = PenguinToolsApplication.CreateDefault();
        try
        {
            await WriteNewEditorFixtureAsync(input, version, ct);
            var result = await application.InspectChartAsync(new ChartInspectRequest(input), ct);
            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Chart_version_too_new);
        }
        finally { File.Delete(input); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VersionSkips_RetainBatchResults_AndReportIncomplete(bool includeSupported)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            if (includeSupported)
            {
                var chart = await WriteChartFixtureAsync(input);
                await File.AppendAllTextAsync(chart, "@CMT\t#meta wall jacket.png wide\n", ct);
            }

            await WriteNewEditorFixtureAsync(Path.Combine(input, "new.ugc"), 10, ct);
            await WriteNewEditorFixtureAsync(Path.Combine(input, "new.mgxc"), 5, ct);
            var scan = await application.ScanOptionAsync(new OptionScanRequest(input), cancellationToken: ct);
            Assert.False(scan.Succeeded);
            Assert.NotNull(scan.Value);
            Assert.True(scan.Value.IsPartial);
            Assert.Equal(includeSupported ? 1 : 0, scan.Value.Books.Count);
            Assert.Equal(2, scan.Value.SkippedCharts.Count);
            Assert.All(scan.Value.SkippedCharts, d =>
            {
                Assert.Equal("warning", d.Severity);
                Assert.Equal(MsgKeys.Warn_Chart_skipped_version, d.Message.Key);
                Assert.True(File.Exists(d.Path));
            });
            var command = RootCommands.BuildRootCommand().Parse(["option", "scan", input, "--no-progress"]);
            Assert.Equal(CliExitCodes.Failure, await command.InvokeAsync(cancellationToken: ct));

            var output = Path.Combine(root, "output");
            var manifest = Path.Combine(output, "TEST", "wallbind.json");
            var oldDds = Path.Combine(output, "TEST", "ddsFieldWall", "ddsFieldWall1001000", "CHU_UI_Fieldwall_1001000.dds");
            if (includeSupported)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(oldDds)!);
                await File.WriteAllTextAsync(manifest, """{"version":1,"bindings":[{"musicId":1000,"wallId":1001000}]}""", ct);
                await File.WriteAllTextAsync(oldDds, "previous texture", ct);
            }
            var oldManifest = includeSupported ? await File.ReadAllTextAsync(manifest, ct) : null;
            var build = await application.BuildOptionAsync(new OptionBuildRequest(input, output, SkipConfig: true,
                Overrides: new OptionBuildOverrides(OptionName: "TEST", ConvertChart: true,
                    ConvertAudio: false, ConvertJacket: false, ConvertBackground: false,
                    GenerateEventXml: false, CustomReleaseTagXml: false, CustomGenre: false)), cancellationToken: ct);
            Assert.False(build.Succeeded);
            Assert.Equal(2, build.Diagnostics.Diagnostics.Count(d => d.Message.Key == MsgKeys.Warn_Chart_skipped_version));
            if (includeSupported)
            {
                Assert.NotNull(build.Value);
                Assert.True(build.Value.IsPartial);
                Assert.Equal(2, build.Value.SkippedCharts.Count);
                Assert.Equal(1, build.Value.ChartCount);
                Assert.Contains(build.Value.Artifacts, a => a.Path.EndsWith(".c2s", StringComparison.Ordinal));
                Assert.Equal(oldManifest, await File.ReadAllTextAsync(manifest, ct));
                Assert.Equal("previous texture", await File.ReadAllTextAsync(oldDds, ct));
            }
            else
            {
                Assert.Null(build.Value);
                Assert.Contains(build.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.App_No_charts_to_export);
                Assert.False(Directory.Exists(output));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".mgxc", 4, null)]
    [InlineData(".ugc", 9, null)]
    [InlineData(".mgxc", 4, ChartFormat.Ugc)]
    [InlineData(".ugc", 9, ChartFormat.Ugc)]
    public async Task OfficialEditor_ScanBuildAndExtractUseLatestFormats(string extension, int version, ChartFormat? outputFormat)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        var input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            await WriteNewEditorFixtureAsync(Path.Combine(input, "chart" + extension), version, ct);
            var scan = await application.ScanOptionAsync(new OptionScanRequest(input), cancellationToken: ct);
            Assert.True(scan.Succeeded, scan.ToString());
            Assert.Empty(scan.Value!.SkippedCharts);
            var build = await application.BuildOptionAsync(new OptionBuildRequest(input, Path.Combine(root, "built"), SkipConfig: true,
                Overrides: new OptionBuildOverrides(OptionName: "TEST", ConvertChart: true,
                    ConvertAudio: false, ConvertJacket: false, ConvertBackground: false,
                    GenerateEventXml: false, CustomReleaseTagXml: false, CustomGenre: false)), cancellationToken: ct);
            Assert.True(build.Succeeded, build.ToString());
            Assert.Equal(1, build.Value!.ChartCount);
            var c2s = Assert.Single(build.Value.Artifacts, a => a.Path.EndsWith(".c2s", StringComparison.Ordinal)).Path;
            // Exercise the extraction boundary with the just-built C2S and a minimal Music.xml.
            var xml = Path.Combine(root, "Music.xml");
            var doc = new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("MusicData",
                new System.Xml.Linq.XElement("name", new System.Xml.Linq.XElement("id", 1234), new System.Xml.Linq.XElement("str", "Test")),
                new System.Xml.Linq.XElement("fumens", new System.Xml.Linq.XElement("MusicFumenData",
                    new System.Xml.Linq.XElement("enable", true),
                    new System.Xml.Linq.XElement("type", new System.Xml.Linq.XElement("id", 3)),
                    new System.Xml.Linq.XElement("file", new System.Xml.Linq.XElement("path", c2s))))));
            await File.WriteAllTextAsync(xml, doc.ToString(), ct);
            var request = new MusicExtractRequest(xml, Path.Combine(root, "extracted"), NoAudio: true, NoJacket: true);
            if (outputFormat is { } format)
            {
                request = request with { OutputFormat = format };
            }
            var extracted = await application.ExtractMusicAsync(request, cancellationToken: ct);
            Assert.True(extracted.Succeeded, extracted.ToString());
            var artifact = Assert.Single(extracted.Value!.Artifacts);
            var outputExtension = outputFormat == ChartFormat.Ugc ? ".ugc" : ".mgxc";
            Assert.Equal("chart" + outputExtension, artifact.Kind);
            Assert.Equal(outputExtension, Path.GetExtension(artifact.Path));
            Assert.Equal(artifact.Path, Assert.Single(extracted.Value.Charts).OutputPath);
            var inspected = await application.InspectChartAsync(new ChartInspectRequest(artifact.Path), ct);
            Assert.True(inspected.Succeeded);
            Assert.True(inspected.Value!.NotesParsed);
            Assert.Equal(outputFormat == ChartFormat.Ugc ? 9 : 4, inspected.Value.FormatVersion);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(ChartFormat.Sus)]
    [InlineData(ChartFormat.C2s)]
    [InlineData((ChartFormat)999)]
    public async Task MusicExtraction_RejectsUnsupportedOutputBeforeCreatingFiles(ChartFormat format)
    {
        var root = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var store = new TrackingAssetStore(root);
        using var application = CreateInjectedApplication(root, store);
        try
        {
            var output = Path.Combine(root, "output");
            var result = await application.ExtractMusicAsync(new MusicExtractRequest(
                Path.Combine(root, "missing.xml"), output, OutputFormat: format),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Invalid_Header);
            Assert.False(Directory.Exists(output));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task WriteNewEditorFixtureAsync(string path, int version, CancellationToken ct)
    {
        if (Path.GetExtension(path) == ".ugc")
        {
            await File.WriteAllTextAsync(path,
                $"' Created with Margrete v1.9.3\n@VER\t{version}\n@TITLE\t新谱面\n@ARTIST\tArtist\n" +
                "@DESIGN\tDesigner\n@DIFF\t5\n@CONST\t14.5\n@SONGID\t1234\n@MAINBPM\t180\n" +
                "@BPM\t0'0\t180\n@BEAT\t0\t4\t4\n@ENDHEAD\n#0'480:tH4\n", ct);
            return;
        }
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("MGXC"u8);
        writer.Write(0);
        writer.Write(version);
        writer.Write("meta"u8);
        writer.Write(0);
        foreach (var (key, value) in new[] { ("titl", "新谱面"), ("arts", "Artist"), ("dsgn", "Designer"), ("sgid", "1234") })
        {
            writer.Write(Encoding.ASCII.GetBytes(key));
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write((short)4);
            writer.Write((short)bytes.Length);
            writer.Write(bytes);
        }
        writer.Write("diff"u8);
        writer.Write((short)1);
        writer.Write((short)5);
        foreach (var (key, value) in new[] { ("cnst", 14.5), ("mbpm", 180.0) })
        {
            writer.Write(Encoding.ASCII.GetBytes(key));
            writer.Write((short)3);
            writer.Write((short)0);
            writer.Write(value);
        }
        var end = stream.Position;
        stream.Position = 16;
        writer.Write(checked((int)(end - 20)));
        stream.Position = end;
        writer.Write("evnt"u8);
        writer.Write(0);
        writer.Write("dat2"u8);
        writer.Write(20);
        writer.Write(new byte[] { 1, 0, 0, 0, 0, 0, 4, 1 });
        writer.Write(80);
        writer.Write(480);
        writer.Write(0);
        var length = stream.Length;
        stream.Position = 4;
        writer.Write(checked((int)(length - 8)));
    }
}
