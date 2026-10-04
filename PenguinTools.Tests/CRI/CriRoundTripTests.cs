using System.Globalization;
using System.Text;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.CRI;
using PenguinTools.Infrastructure;
using PenguinTools.Media;
using SonicAudioLib.Archives;
using SonicAudioLib.CriMw;
using VGAudio.Containers.Hca;
using Xunit;

namespace PenguinTools.Tests.CRI;

[Collection("CRI console")]
public class CriRoundTripTests
{
    public CriRoundTripTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Convert_and_extract_preserve_preview_and_cue_metadata()
    {
        using var dir = new TempDirectory();
        var wavPath = Path.Combine(dir.Path, "input.wav");
        WriteStereo48kWav(wavPath, sampleFrames: 2400);

        var acbPath = Path.Combine(dir.Path, "out.acb");
        var awbPath = Path.Combine(dir.Path, "out.awb");
        ConvertService.Convert(
            wavPath,
            acbPath,
            awbPath,
            "cueFile000001",
            new CriEncodingOptions(1234, 5678, ConvertService.DefaultHcaKey),
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(acbPath));
        Assert.True(File.Exists(awbPath));

        var cueSheet = new CriTable();
        cueSheet.Load(acbPath);
        Assert.Equal("cueFile000001", cueSheet.Rows[0]["Name"]);

        var cueTable = new CriTable();
        cueTable.Load(cueSheet.Rows[0]["CueTable"] as byte[]);
        Assert.Equal(2, cueTable.Rows.Count);
        Assert.Equal(0, Convert.ToInt32(cueTable.Rows[0]["CueId"], CultureInfo.InvariantCulture));
        Assert.Equal(1, Convert.ToInt32(cueTable.Rows[1]["CueId"], CultureInfo.InvariantCulture));
        Assert.Equal(50, Convert.ToInt32(cueTable.Rows[0]["Length"], CultureInfo.InvariantCulture));

        var decodedDir = Path.Combine(dir.Path, "decoded");
        var manifest = ExtractService.Extract(acbPath, decodedDir, awbPath, ConvertService.DefaultHcaKey,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, manifest.SchemaVersion);

        var cue = Assert.Single(manifest.Cues);
        Assert.Equal(0, cue.CueId);
        Assert.Equal("cueFile000001", cue.Name);
        Assert.Equal((uint)1234, cue.PreviewStartMs);
        Assert.Equal((uint)5678, cue.PreviewStopMs);
        Assert.Equal((ushort)2, cue.Channels);
        Assert.Equal(48000u, cue.SampleRate);
        Assert.Equal(2400u, cue.SampleFrames);
        Assert.True(File.Exists(cue.WavPath));

    }

    [Fact]
    public void ApplySubKey_matches_vgmstream_formula()
    {
        Assert.Equal(100UL, ExtractService.ApplySubKey(100, 0));
        const ushort subKey = 7;
        var multiplier = ((ulong)subKey << 16) | unchecked((ushort)(~subKey + 2));
        Assert.Equal(12345UL * multiplier, ExtractService.ApplySubKey(12345, subKey));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, 123456789UL)]
    public async Task Media_tool_round_trip_works_without_external_CRI_executable(bool useAwb, ulong? key)
    {
        using var dir = new TempDirectory();
        var wavPath = Path.Combine(dir.Path, "input with spaces.wav");
        WriteStereo48kWav(wavPath, sampleFrames: 2400);
        var acbPath = Path.Combine(dir.Path, "output with spaces.acb");
        var awbPath = Path.Combine(dir.Path, "output with spaces.awb");
        var tool = new MediaTool(Path.Combine(dir.Path, "absent assets"));
        var ct = TestContext.Current.CancellationToken;

        await tool.ConvertCriAsync(new CriConvertRequest(wavPath, acbPath, awbPath, "cueFile000002", 100, 200,
            key ?? ConvertService.DefaultHcaKey), ct);
        var result = await tool.ExtractCriAudioAsync(new CriExtractOptions(
            useAwb ? awbPath : acbPath, Path.Combine(dir.Path, "decoded"), HcaKey: key), ct);

        var cue = Assert.Single(result.Cues);
        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal(useAwb ? awbPath : acbPath, result.Source);
        Assert.Equal(48000u, cue.SampleRate);
        Assert.Equal(2400u, cue.SampleFrames);
        Assert.Equal((ushort)2, cue.Channels);
        Assert.Equal((ushort)16, cue.BitsPerSample);
        Assert.Equal(useAwb ? null : "cueFile000002", cue.Name);
        Assert.Equal(useAwb ? (uint?)null : 100u, cue.PreviewStartMs);
        Assert.Equal(useAwb ? (uint?)null : 200u, cue.PreviewStopMs);
        Assert.True(File.Exists(cue.WavPath));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "absent assets")));
    }

    [Fact]
    public async Task Media_tool_reports_CRI_failure_as_invalid_audio()
    {
        using var dir = new TempDirectory();
        var tool = new MediaTool(dir.Path);
        var ct = TestContext.Current.CancellationToken;

        var convertError = await Assert.ThrowsAsync<DiagnosticException>(() => tool.ConvertCriAsync(new CriConvertRequest(
            Path.Combine(dir.Path, "missing.wav"), Path.Combine(dir.Path, "out.acb"),
            Path.Combine(dir.Path, "out.awb"), "cue", 0, 1, ConvertService.DefaultHcaKey), ct));
        var extractError = await Assert.ThrowsAsync<DiagnosticException>(() => tool.ExtractCriAudioAsync(
            new CriExtractOptions(Path.Combine(dir.Path, "missing.awb"), Path.Combine(dir.Path, "decoded")), ct));

        Assert.Equal(MsgKeys.Error_Invalid_audio, convertError.Descriptor.Key);
        Assert.Equal(MsgKeys.Error_Invalid_audio, extractError.Descriptor.Key);
        Assert.Contains("missing.wav", Assert.IsType<string>(convertError.Target));
        Assert.Contains("missing.awb", Assert.IsType<string>(extractError.Target));
    }

    [Fact]
    public async Task Media_tool_propagates_CRI_cancellation_before_creating_outputs()
    {
        using var dir = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var tool = new MediaTool(dir.Path);
        var acbPath = Path.Combine(dir.Path, "out.acb");
        var awbPath = Path.Combine(dir.Path, "out.awb");
        var decodedPath = Path.Combine(dir.Path, "decoded");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.ConvertCriAsync(new CriConvertRequest(
            "missing.wav", acbPath, awbPath, "cue", 0, 1, ConvertService.DefaultHcaKey), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.ExtractCriAudioAsync(
            new CriExtractOptions("missing.awb", decodedPath), cancellation.Token));

        Assert.False(File.Exists(acbPath));
        Assert.False(File.Exists(awbPath));
        Assert.False(Directory.Exists(decodedPath));
    }

    [Fact]
    public void Hca_reader_console_diagnostics_can_be_disabled()
    {
        using var dir = new TempDirectory();
        var wavPath = Path.Combine(dir.Path, "input.wav");
        WriteStereo48kWav(wavPath, sampleFrames: 2400);
        var awbPath = Path.Combine(dir.Path, "out.awb");
        ConvertService.Convert(wavPath, Path.Combine(dir.Path, "out.acb"), awbPath, "cue",
            new CriEncodingOptions(0, 1, ConvertService.DefaultHcaKey), TestContext.Current.CancellationToken);
        using var awbStream = File.OpenRead(awbPath);
        var archive = new CriAfs2Archive();
        archive.Read(awbStream);
        using var entry = archive[0].Open(awbStream);
        using var hcaStream = new MemoryStream();
        entry.CopyTo(hcaStream);
        hcaStream.Position = 0;
        var metadata = new HcaReader().ReadMetadata(hcaStream);
        var frame = hcaStream.GetBuffer().AsSpan(metadata.HeaderSize, metadata.Hca.FrameSize);
        frame.Clear();
        frame[^3] = 1;
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            hcaStream.Position = 0;
            new HcaReader { Decrypt = false }.Read(hcaStream);
            Assert.Contains("CRC mismatch", output.ToString());

            output.GetStringBuilder().Clear();
            hcaStream.Position = 0;
            new HcaReader { Decrypt = false, LogCrcErrors = false }.Read(hcaStream);
            Assert.Empty(output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static void WriteStereo48kWav(string path, int sampleFrames)
    {
        var dataSize = sampleFrames * 4;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(48000);
        writer.Write(192000);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        for (var i = 0; i < sampleFrames; i++)
        {
            var sample = (short)(Math.Sin(i * 440.0 * Math.PI * 2.0 / 48000.0) * 8000.0);
            writer.Write(sample);
            writer.Write(sample);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "PenguinTools.CRI.Tests",
            Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}

[CollectionDefinition("CRI console", DisableParallelization = true)]
public sealed class CriConsoleCollection;
