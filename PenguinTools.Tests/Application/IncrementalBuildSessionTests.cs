using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PenguinTools.Application;
using PenguinTools.CLI;
using PenguinTools.Core;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed partial class PenguinToolsApplicationTests
{
    private const string SessionChart = "@VER\t8\n@TICKS\t480\n@TITLE\tSession\n@DESIGN\tDesigner\n@DIFF\t3\n" +
        "@SONGID\t1234\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n@FLAG\tSOFFSET\tTRUE\n" +
        "@BGM\taudio.wav\n@JACKET\tjacket.png\n#0'0:t02\n";

    [Fact]
    public async Task IncrementalSession_NoteEditsReuseMediaAndPublishIndependentSnapshots()
    {
        using var fixture = new SessionFixture();
        var first = await fixture.Build(SessionChart);
        var original = await File.ReadAllBytesAsync(first.ChartPath!, TestContext.Current.CancellationToken);
        var second = await fixture.Build(SessionChart.Replace("#0'0:t02", "#0'120:t02"));
        Assert.Contains("chart", second.Changes);
        Assert.DoesNotContain("audio", second.Changes);
        Assert.DoesNotContain("jacket", second.Changes);
        Assert.Equal(1, fixture.Media.AudioConversions);
        Assert.Equal(1, fixture.Media.JacketConversions);
        Assert.Equal(1, fixture.Media.AudioValidations);
        Assert.Equal(1, fixture.Media.ImageValidations);
        Assert.Equal(Path.Combine(fixture.Root, "audio.wav"), second.Metadata.FullBgmFilePath);
        Assert.Equal(original, await File.ReadAllBytesAsync(first.ChartPath!, TestContext.Current.CancellationToken));
        Assert.NotEqual(first.ChartPath, second.ChartPath);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "document.ugc")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "options.json")));
        var duplicate = await fixture.Build(SessionChart.Replace("#0'0:t02", "#0'120:t02"));
        Assert.Empty(duplicate.Changes);
        Assert.All(duplicate.Artifacts, item => Assert.Equal("reused", item.Status));
        Assert.Equal(3, duplicate.Revision);
    }

    [Fact]
    public async Task IncrementalSession_AbsoluteMediaAliasesShareRequestHashes()
    {
        using var fixture = new SessionFixture();
        var alias = Path.Combine(fixture.Root, ".", "jacket.png");
        var content = SessionChart + "@BGIMG\t" + alias + "\n";
        var first = await fixture.Build(content);
        Assert.Contains(first.Artifacts, item => item.Kind == "stage");
        var edited = await fixture.Build(content.Replace("#0'0:t02", "#0'120:t02"));
        Assert.Equal(["chart"], edited.Changes);
        Assert.Equal(1, fixture.Media.JacketConversions);
        Assert.Equal(1, fixture.Media.StageConversions);
        Assert.Equal(1, fixture.Media.AudioConversions);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "jacket.png"), "image-2", TestContext.Current.CancellationToken);
        var changed = await fixture.Build(content);
        Assert.Contains("jacket", changed.Changes);
        Assert.Contains("stage", changed.Changes);
        Assert.Equal(2, fixture.Media.JacketConversions);
        Assert.Equal(2, fixture.Media.StageConversions);
    }

    [Fact]
    public async Task IncrementalSession_ChangedSourceDuringExportRetriesBeforeCommitting()
    {
        using var fixture = new SessionFixture();
        var first = await fixture.Build(SessionChart);
        var original = Assert.Single(first.Artifacts, item => item.Path.EndsWith(".awb", StringComparison.Ordinal));
        var originalBytes = await File.ReadAllBytesAsync(original.Path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "jacket.png"), "image-2", TestContext.Current.CancellationToken);
        var edits = 0;
        fixture.Media.BeforeJacketConversion = () =>
        {
            if (edits++ == 0)
            {
                var source = Path.Combine(fixture.Root, "audio.wav");
                var timestamp = File.GetLastWriteTimeUtc(source);
                File.WriteAllText(source, "audio-2");
                File.SetLastWriteTimeUtc(source, timestamp);
            }
        };
        var updated = await fixture.Build(SessionChart);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(2, edits);
        Assert.Equal(2, fixture.Media.AudioConversions);
        var audio = Assert.Single(updated.Artifacts, item => item.Path.EndsWith(".awb", StringComparison.Ordinal));
        Assert.StartsWith("audio-2:", await File.ReadAllTextAsync(audio.Path, TestContext.Current.CancellationToken));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(original.Path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetDirectories(fixture.TempDirectory, "b-*")
            .SelectMany(directory => Directory.GetDirectories(directory)), directory => Path.GetFileName(directory) != "m");
    }

    [Fact]
    public async Task IncrementalSession_ContinuouslyChangingSourcePreservesCommittedState()
    {
        using var fixture = new SessionFixture();
        var first = await fixture.Build(SessionChart);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "jacket.png"), "image-2", TestContext.Current.CancellationToken);
        var edits = 0;
        fixture.Media.BeforeJacketConversion = () => File.WriteAllText(Path.Combine(fixture.Root, "audio.wav"), $"audio-{++edits + 1}");
        var request = fixture.Request(SessionChart);
        var rejected = await fixture.Session.PrepareAsync(request, TestContext.Current.CancellationToken);
        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Diagnostics.Diagnostics, item => item.Message.Key == MsgKeys.App_Inputs_changed_during_export);
        Assert.Equal(2, edits);
        Assert.False(Directory.Exists(request.OutputDirectory));
        Assert.All(first.Artifacts, item => Assert.True(File.Exists(item.Path)));
        fixture.Media.BeforeJacketConversion = null;
        var corrected = await fixture.Build(SessionChart);
        Assert.Equal(first.Revision + 1, corrected.Revision);
        var audio = Assert.Single(corrected.Artifacts, item => item.Path.EndsWith(".awb", StringComparison.Ordinal));
        Assert.StartsWith("audio-3:", await File.ReadAllTextAsync(audio.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IncrementalSession_CorruptCachedOutputRebuildsWithoutChangingPublishedSnapshot()
    {
        using var fixture = new SessionFixture();
        var first = await fixture.Build(SessionChart);
        var original = Assert.Single(first.Artifacts, item => item.Path.EndsWith(".awb", StringComparison.Ordinal));
        var originalBytes = await File.ReadAllBytesAsync(original.Path, TestContext.Current.CancellationToken);
        var work = Assert.Single(Directory.GetDirectories(fixture.TempDirectory, "b-*"));
        var cached = Assert.Single(Directory.GetFiles(work, "*.awb", SearchOption.AllDirectories));
        await File.WriteAllTextAsync(cached, "corrupt", TestContext.Current.CancellationToken);
        var corrected = await fixture.Build(SessionChart.Replace("#0'0:t02", "#0'120:t02"));
        Assert.Equal(2, fixture.Media.AudioConversions);
        var audio = Assert.Single(corrected.Artifacts, item => item.Path.EndsWith(".awb", StringComparison.Ordinal));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(audio.Path, TestContext.Current.CancellationToken));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(original.Path, TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(audio.Path, "caller-owned edit", TestContext.Current.CancellationToken);
        var duplicate = await fixture.Build(SessionChart.Replace("#0'0:t02", "#0'120:t02"));
        var repeatedAudio = Assert.Single(duplicate.Artifacts, item => item.Path.EndsWith(".awb", StringComparison.Ordinal));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(repeatedAudio.Path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("@BPM\t0'0\t120", "@BPM\t0'0\t150")]
    [InlineData("@BEAT\t0\t4\t4", "@BEAT\t0\t3\t4")]
    [InlineData("@FLAG\tSOFFSET\tTRUE", "@FLAG\tSOFFSET\tFALSE")]
    [InlineData("@BGM\taudio.wav", "@BGM\taudio.wav\n@BGMOFS\t0.25")]
    public async Task IncrementalSession_TimingDependenciesInvalidateAudio(string from, string to)
    {
        using var fixture = new SessionFixture();
        await fixture.Build(SessionChart);
        var edited = await fixture.Build(SessionChart.Replace(from, to));
        Assert.Contains("audio", edited.Changes);
        Assert.Equal(2, fixture.Media.AudioConversions);
        Assert.Equal(1, fixture.Media.JacketConversions);
        Assert.Equal(1, fixture.Media.AudioValidations);
    }

    [Fact]
    public async Task IncrementalSession_ChangedAndRemovedImagesDoNotRetainStaleArtifacts()
    {
        using var fixture = new SessionFixture();
        var first = await fixture.Build(SessionChart);
        var jacket = Path.Combine(fixture.Root, "jacket.png");
        var timestamp = File.GetLastWriteTimeUtc(jacket);
        await File.WriteAllTextAsync(jacket, "image-2", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(jacket, timestamp);
        var updated = await fixture.Build(SessionChart);
        Assert.Equal(["jacket"], updated.Changes);
        Assert.Equal(2, fixture.Media.ImageValidations);
        Assert.Equal(2, fixture.Media.JacketConversions);
        Assert.Equal(1, fixture.Media.AudioConversions);
        var oldImage = Assert.Single(first.Artifacts, item => item.Kind == "jacket");
        Assert.Equal("image-1", await File.ReadAllTextAsync(oldImage.Path, TestContext.Current.CancellationToken));

        File.Delete(jacket);
        var removed = await fixture.Build(SessionChart.Replace("@JACKET\tjacket.png", "@JACKET\t"));
        var removal = Assert.Single(removed.Artifacts, item => item.Kind == "jacket");
        Assert.Equal("removed", removal.Status);
        Assert.False(File.Exists(removal.Path));
        Assert.Contains("jacket", removed.Changes);
        Assert.Equal(1, fixture.Media.AudioConversions);
        await File.WriteAllTextAsync(jacket, "image-3", TestContext.Current.CancellationToken);
        var restored = await fixture.Build(SessionChart);
        Assert.Equal("changed", Assert.Single(restored.Artifacts, item => item.Kind == "jacket").Status);
        Assert.Equal(3, fixture.Media.JacketConversions);
    }

    [Fact]
    public async Task IncrementalSession_FailureKeepsCommittedCacheAndSnapshot()
    {
        using var fixture = new SessionFixture();
        var first = await fixture.Build(SessionChart);
        var original = await File.ReadAllBytesAsync(first.ChartPath!, TestContext.Current.CancellationToken);
        var failedRequest = fixture.Request(SessionChart.Replace("@SONGID\t1234", "@SONGID\tmissing"));
        var failed = await fixture.Session.PrepareAsync(failedRequest, TestContext.Current.CancellationToken);
        Assert.False(failed.Succeeded);
        Assert.False(Directory.Exists(failedRequest.OutputDirectory));
        var corrected = await fixture.Build(SessionChart);
        Assert.Equal(2, corrected.Revision);
        Assert.Empty(corrected.Changes);
        Assert.Equal(1, fixture.Media.AudioConversions);
        Assert.Equal(1, fixture.Media.JacketConversions);
        Assert.Equal(original, await File.ReadAllBytesAsync(first.ChartPath!, TestContext.Current.CancellationToken));
        var collision = fixture.Request(SessionChart) with { OutputDirectory = Path.GetDirectoryName(first.OptionDirectory)! };
        Assert.False((await fixture.Session.PrepareAsync(collision, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(original, await File.ReadAllBytesAsync(first.ChartPath!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IncrementalSession_ConfigurationChangesInvalidateOnlyAffectedResources()
    {
        using var fixture = new SessionFixture();
        await fixture.Build(SessionChart);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "options.json"),
            "{\"hcaEncryptionKey\":12345,\"convertJacket\":false}", TestContext.Current.CancellationToken);
        var updated = await fixture.Build(SessionChart);
        Assert.DoesNotContain("chart", updated.Changes);
        Assert.Contains("audio", updated.Changes);
        Assert.Equal("removed", Assert.Single(updated.Artifacts, item => item.Kind == "jacket").Status);
        Assert.Equal(2, fixture.Media.AudioConversions);
    }

    [Fact]
    public async Task IncrementalSession_StageAndWallReuseAndRemovalFollowCurrentReferences()
    {
        using var fixture = new SessionFixture();
        var content = SessionChart + "@BGIMG\tjacket.png\n@CMT\t#meta wall jacket.png wide\n";
        var first = await fixture.Build(content);
        Assert.Contains(first.Artifacts, item => item.Kind == "stage");
        Assert.Contains(first.Artifacts, item => item.Kind == "wall" && item.Path.EndsWith(".dds", StringComparison.Ordinal));
        var next = await fixture.Build(content.Replace("#0'0:t02", "#0'120:t02"));
        Assert.DoesNotContain("stage", next.Changes);
        Assert.DoesNotContain("wall", next.Changes);
        Assert.Equal(1, fixture.Media.StageConversions);
        Assert.Equal(1, fixture.Media.WallConversions);
        var removed = await fixture.Build(SessionChart);
        Assert.Contains("stage", removed.Changes);
        Assert.Contains("wall", removed.Changes);
        Assert.All(removed.Artifacts.Where(item => item.Status == "removed"), item => Assert.False(File.Exists(item.Path)));
        Assert.False(Directory.Exists(Path.Combine(removed.OptionDirectory, "stage")));
        Assert.False(Directory.Exists(Path.Combine(removed.OptionDirectory, "ddsFieldWall")));
    }

    [Fact]
    public async Task IncrementalSession_ExportFailureDoesNotCommitCandidateCache()
    {
        using var fixture = new SessionFixture();
        var first = await fixture.Build(SessionChart);
        var image = Path.Combine(fixture.Root, "jacket.png");
        await File.WriteAllTextAsync(image, "image-2", TestContext.Current.CancellationToken);
        fixture.Media.FailJacket = true;
        var request = fixture.Request(SessionChart);
        Assert.False((await fixture.Session.PrepareAsync(request, TestContext.Current.CancellationToken)).Succeeded);
        Assert.False(Directory.Exists(request.OutputDirectory));
        fixture.Media.FailJacket = false;
        await File.WriteAllTextAsync(image, "image-1", TestContext.Current.CancellationToken);
        var recovered = await fixture.Build(SessionChart);
        Assert.Equal(first.Revision + 1, recovered.Revision);
        Assert.Empty(recovered.Changes);
        Assert.Equal(2, fixture.Media.JacketConversions);
        Assert.Equal(1, fixture.Media.AudioConversions);
    }

    [Fact]
    public async Task IncrementalSession_SavedMgxcKeepsSourceBytesAndResolvesMedia()
    {
        using var fixture = new SessionFixture();
        var input = Path.Combine(fixture.Root, "chart.mgxc");
        var chart = OriginFixture(true);
        chart.Meta.Id = 1234;
        chart.Meta.Title = "Saved";
        chart.Meta.BgmFilePath = "audio.wav";
        chart.Meta.JacketFilePath = "jacket.png";
        Assert.True((await new PenguinTools.Chart.Writer.mgxc.MgxcChartWriter(
            new PenguinTools.Chart.Writer.mgxc.MgxcWriteRequest(input, chart)).WriteAsync(TestContext.Current.CancellationToken)).Succeeded);
        var original = await File.ReadAllBytesAsync(input, TestContext.Current.CancellationToken);
        var request = fixture.Request("") with { Content = null, Format = ChartFormat.Mgxc, InputPath = "chart.mgxc" };
        var first = await fixture.Session.PrepareAsync(request, TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded);
        Assert.Equal(Path.Combine(fixture.Root, "audio.wav"), first.Value!.Metadata.FullBgmFilePath);
        var next = await fixture.Session.PrepareAsync(request with { OutputDirectory = Path.Combine(fixture.Root, "saved-next") }, TestContext.Current.CancellationToken);
        Assert.True(next.Succeeded);
        Assert.Empty(next.Value!.Changes);
        Assert.Equal(1, fixture.Media.AudioConversions);
        Assert.Equal(original, await File.ReadAllBytesAsync(input, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IncrementalSession_MissingMediaCannotFreezeDuplicateSnapshot()
    {
        using var fixture = new SessionFixture();
        var image = Path.Combine(fixture.Root, "jacket.png");
        File.Delete(image);
        var first = await fixture.Build(SessionChart);
        Assert.DoesNotContain(first.Artifacts, item => item.Kind == "jacket");
        await fixture.Build(SessionChart);
        Assert.Equal(2, fixture.Media.ImageValidations);
        await File.WriteAllTextAsync(image, "appeared", TestContext.Current.CancellationToken);
        var next = await fixture.Build(SessionChart);
        Assert.Contains("jacket", next.Changes);
        Assert.Equal("changed", Assert.Single(next.Artifacts, item => item.Kind == "jacket").Status);
        Assert.Equal(3, fixture.Media.ImageValidations);
        Assert.Equal(1, fixture.Media.JacketConversions);
    }

    [Fact]
    public async Task IncrementalSession_LongTempRootSupportsNativeWorkingDirectory()
    {
        using var fixture = new SessionFixture(longTempRoot: true);
        fixture.Media.CheckNativeWorkingDirectory = true;
        await fixture.Build(SessionChart);
        using (var second = fixture.CreateSession())
        {
            var request = fixture.Request(SessionChart);
            var result = await second.PrepareAsync(request, TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, string.Join('\n', result.Diagnostics.Diagnostics.Select(item =>
                item.Message + " " + string.Join(", ", item.Message.Args?.Values ?? []))));
            Assert.Equal(2, Directory.GetDirectories(fixture.TempDirectory, "b-*").Length);
        }
        Assert.Equal(2, fixture.Media.NativeWorkingDirectories.Distinct().Count());
        Assert.All(fixture.Media.NativeWorkingDirectories, path => Assert.True(path.Length < 260));
        Assert.Single(Directory.GetDirectories(fixture.TempDirectory, "b-*"));
    }

    [Fact]
    public async Task IncrementalSession_JsonLinesSurviveFailureAndClearCorrectedWarnings()
    {
        using var fixture = new SessionFixture();
        var requests = new[]
        {
            new SessionRequest(10, "prepare", "document", fixture.Root, "ugc", SessionChart + "#0'240:Z02\n",
                OutputDirectory: Path.Combine(fixture.Root, "json1"), Overrides: new("A001")),
            new SessionRequest(11, "prepare", "document", fixture.Root, "ugc", SessionChart.Replace("@VER\t8", "@VER\t99999"),
                OutputDirectory: Path.Combine(fixture.Root, "json2"), Overrides: new("A001")),
            new SessionRequest(12, "prepare", "document", fixture.Root, "ugc", SessionChart,
                OutputDirectory: Path.Combine(fixture.Root, "json3"), Overrides: new("A001")),
            new SessionRequest(13, "close")
        };
        using var input = new StringReader(string.Join('\n', requests.Select(request =>
            JsonSerializer.Serialize(request, CliJsonSerializerContext.Default.SessionRequest))));
        using var output = new StringWriter();
        Assert.Equal(0, await SessionCommands.RunAsync(fixture.Session, input, output, TestContext.Current.CancellationToken));
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        using var ready = JsonDocument.Parse(lines[0]);
        Assert.Equal(1, ready.RootElement.GetProperty("protocol").GetInt32());
        var first = JsonSerializer.Deserialize(lines[1], CliJsonSerializerContext.Default.SessionResponse)!;
        var failed = JsonSerializer.Deserialize(lines[2], CliJsonSerializerContext.Default.SessionResponse)!;
        var corrected = JsonSerializer.Deserialize(lines[3], CliJsonSerializerContext.Default.SessionResponse)!;
        Assert.True(first.Success);
        Assert.False(failed.Success);
        Assert.Equal(11, failed.Id);
        Assert.True(corrected.Success);
        Assert.Equal(2, corrected.Result!.Revision);
        var warningKeys = first.Diagnostics.Select(item => item.Message.Key).Except(corrected.Diagnostics.Select(item => item.Message.Key));
        Assert.NotEmpty(warningKeys);
        Assert.Equal(1, fixture.Media.AudioConversions);
        var quoted = JsonSerializer.Deserialize("{\"id\":1,\"method\":\"prepare\",\"overrides\":{\"hcaEncryptionKey\":\"32931609366120192\"}}",
            CliJsonSerializerContext.Default.SessionRequest)!;
        Assert.Equal(32931609366120192UL, quoted.Overrides!.HcaEncryptionKey);
    }

    [Fact]
    public async Task IncrementalSession_ProcessUsesUtf8ForUnescapedPaths()
    {
        var root = Directory.CreateTempSubdirectory("penguintools-session-谱面-");
        var outputDirectory = Path.Combine(root.FullName, "输出");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = cancellation.Token;
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(RootCommands).Assembly.Location);
        start.ArgumentList.Add("session");
        using var process = Process.Start(start)!;
        try
        {
            using var greeting = JsonDocument.Parse((await process.StandardOutput.ReadLineAsync(ct))!);
            Assert.True(greeting.RootElement.GetProperty("ready").GetBoolean());
            var request = new SessionRequest(1, "prepare", "文档", root.FullName, "ugc",
                SessionChart.Replace("@BGM\taudio.wav\n", "").Replace("@JACKET\tjacket.png\n", ""),
                OutputDirectory: outputDirectory, Overrides: new("A001", ConvertAudio: false,
                    ConvertJacket: false, ConvertBackground: false, ConvertWall: false));
            var context = new CliJsonSerializerContext(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var line = JsonSerializer.Serialize(request, context.SessionRequest);
            Assert.Contains("谱面", line);
            await process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
            var response = JsonSerializer.Deserialize((await process.StandardOutput.ReadLineAsync(ct))!,
                CliJsonSerializerContext.Default.SessionResponse)!;
            Assert.True(response.Success, string.Join(", ", response.Diagnostics.Select(item =>
                item.Message + " " + string.Join(", ", item.Message.Args?.Values ?? []))));
            Assert.Equal(Path.Combine(outputDirectory, "A001"), response.Result!.OptionDirectory);
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"close\"}".AsMemory(), ct);
            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
            root.Delete(true);
        }
    }

    private sealed class SessionFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "PenguinToolsTests", Guid.NewGuid().ToString("N"));
        internal SessionMediaService Media { get; } = new();
        internal IncrementalBuildSession Session { get; }
        internal string TempDirectory { get; }
        private readonly TrackingAssetStore _store;
        private readonly PenguinToolsApplication _application;
        private int _build;

        internal SessionFixture(bool longTempRoot = false)
        {
            Directory.CreateDirectory(Root);
            TempDirectory = longTempRoot ? Path.Combine(Root, new string('w', Math.Max(1, 223 - Root.Length - 1))) : Root;
            Directory.CreateDirectory(TempDirectory);
            File.WriteAllText(Path.Combine(Root, "audio.wav"), "audio-1");
            File.WriteAllText(Path.Combine(Root, "jacket.png"), "image-1");
            _store = new(Root);
            _application = new(new PenguinToolsApplicationDependencies(new TestPaths(TempDirectory), _store,
                TestAssets.Load(), Media, Media, new TestAssetProvider(Root)));
            Session = _application.CreateBuildSession();
        }

        internal IncrementalBuildSession CreateSession() => _application.CreateBuildSession();

        internal IncrementalBuildRequest Request(string content) => new("document", Root, ChartFormat.Ugc,
            Path.Combine(Root, "build-" + ++_build), content, Overrides: new(OptionName: "A001"));

        internal async Task<IncrementalBuildResult> Build(string content)
        {
            var result = await Session.PrepareAsync(Request(content), TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, string.Join('\n', result.Diagnostics.Diagnostics.Select(item =>
                item.Message + " " + string.Join(", ", item.Message.Args?.Values ?? []) + " " + item.RelatedException)));
            Assert.All(result.Value!.Artifacts.Where(item => item.Status != "removed"), item => Assert.True(File.Exists(item.Path)));
            return result.Value;
        }

        public void Dispose()
        {
            Session.Dispose();
            Assert.Empty(Directory.EnumerateDirectories(TempDirectory, "b-*"));
            _application.Dispose();
            _store.Dispose();
            Directory.Delete(Root, true);
        }
    }

    private sealed class SessionMediaService : IAudioService, IImageService
    {
        internal int AudioConversions { get; private set; }
        internal int JacketConversions { get; private set; }
        internal int AudioValidations { get; private set; }
        internal int ImageValidations { get; private set; }
        internal int StageConversions { get; private set; }
        internal int WallConversions { get; private set; }
        internal Action? BeforeJacketConversion { get; set; }
        internal bool FailJacket { get; set; }
        internal bool CheckNativeWorkingDirectory { get; set; }
        internal List<string> NativeWorkingDirectories { get; } = [];

        public Task<MediaValidationResult> CheckAudioValidAsync(string source, CancellationToken ct = default)
        {
            AudioValidations++;
            return Task.FromResult(File.Exists(source) ? MediaValidationResult.Valid : MediaValidationResult.Invalid("missing"));
        }
        public Task<MediaValidationResult> CheckImageValidAsync(string source, CancellationToken ct = default)
        {
            ImageValidations++;
            return Task.FromResult(File.Exists(source) ? MediaValidationResult.Valid : MediaValidationResult.Invalid("missing"));
        }
        public async Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset, CancellationToken ct = default)
        {
            AudioConversions++;
            if (CheckNativeWorkingDirectory)
            {
                var directory = Path.GetDirectoryName(dst)!;
                NativeWorkingDirectories.Add(directory);
                var start = new ProcessStartInfo("dotnet", "--version")
                {
                    WorkingDirectory = directory,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(start)!;
                await process.WaitForExitAsync(ct);
                Assert.Equal(0, process.ExitCode);
            }
            await File.WriteAllTextAsync(dst, await File.ReadAllTextAsync(src, ct) + ":" + offset, ct);
            return new(new ProcessStartInfo("test"), 0, "", "");
        }
        public async Task ConvertCriAsync(CriConvertRequest request, CancellationToken ct = default)
        {
            await File.WriteAllTextAsync(request.Acb, $"{request.HcaKey}:{request.PreviewStartMs}:{request.PreviewStopMs}", ct);
            await File.WriteAllTextAsync(request.Awb, await File.ReadAllTextAsync(request.Wav, ct), ct);
        }
        public async Task ConvertJacketAsync(string source, string destination, CancellationToken ct = default)
        {
            JacketConversions++;
            BeforeJacketConversion?.Invoke();
            if (FailJacket)
            {
                throw new IOException("Image conversion failed.");
            }
            await File.WriteAllTextAsync(destination, await File.ReadAllTextAsync(source, ct), ct);
        }
        public async Task ConvertWallAsync(string source, string destination, WallWidth width, CancellationToken ct = default)
        {
            WallConversions++;
            await File.WriteAllTextAsync(destination, await File.ReadAllTextAsync(source, ct) + width, ct);
        }
        public async Task ConvertStageAsync(string background, string destination, string? notesFieldDestination,
            IReadOnlyList<string?>? effects = null, int backgroundOffset = 160, string? templatePath = null, CancellationToken ct = default)
        {
            StageConversions++;
            await File.WriteAllTextAsync(destination, await File.ReadAllTextAsync(background, ct) + backgroundOffset, ct);
            if (notesFieldDestination is not null)
            {
                await File.WriteAllTextAsync(notesFieldDestination, "notes-field", ct);
            }
        }
        public Task<CriExtractResult> ExtractCriAudioAsync(CriExtractOptions options, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ExtractDdsAsync(string source, string outputDirectory, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DdsDecodeResult> DecodeDdsAsync(string source, string destination, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ConvertRasterToPngAsync(string source, string destination, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
