using System.Text;
using System.Text.Json;
using PenguinTools.CRI;
using SonicAudioLib.CriMw;
using Xunit;

namespace PenguinTools.Tests.CRI;

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
            previewStartMs: 1234,
            previewStopMs: 5678,
            hcaKey: ConvertService.DefaultHcaKey);

        Assert.True(File.Exists(acbPath));
        Assert.True(File.Exists(awbPath));

        var cueSheet = new CriTable();
        cueSheet.Load(acbPath);
        Assert.Equal("cueFile000001", cueSheet.Rows[0]["Name"]);

        var cueTable = new CriTable();
        cueTable.Load(cueSheet.Rows[0]["CueTable"] as byte[]);
        Assert.Equal(2, cueTable.Rows.Count);
        Assert.Equal(0, Convert.ToInt32(cueTable.Rows[0]["CueId"]));
        Assert.Equal(1, Convert.ToInt32(cueTable.Rows[1]["CueId"]));
        Assert.Equal(50, Convert.ToInt32(cueTable.Rows[0]["Length"]));

        var decodedDir = Path.Combine(dir.Path, "decoded");
        var manifest = ExtractService.Extract(acbPath, decodedDir, awbPath, ConvertService.DefaultHcaKey);
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Single(manifest.Cues);

        var cue = manifest.Cues[0];
        Assert.Equal(0, cue.CueId);
        Assert.Equal("cueFile000001", cue.Name);
        Assert.Equal((uint)1234, cue.PreviewStartMs);
        Assert.Equal((uint)5678, cue.PreviewStopMs);
        Assert.Equal((ushort)2, cue.Channels);
        Assert.Equal(48000u, cue.SampleRate);
        Assert.Equal(2400u, cue.SampleFrames);
        Assert.True(File.Exists(cue.WavPath));

        var json = ExtractService.SerializeManifest(manifest);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("cueFile000001", document.RootElement.GetProperty("cues")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void ApplySubKey_matches_vgmstream_formula()
    {
        Assert.Equal(100UL, ExtractService.ApplySubKey(100, 0));
        const ushort subKey = 7;
        var multiplier = ((ulong)subKey << 16) | unchecked((ushort)(~subKey + 2));
        Assert.Equal(12345UL * multiplier, ExtractService.ApplySubKey(12345, subKey));
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
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
