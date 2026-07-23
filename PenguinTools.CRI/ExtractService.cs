using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SonicAudioLib.Archives;
using SonicAudioLib.CriMw;
using VGAudio.Codecs.CriHca;
using VGAudio.Containers.Hca;
using VGAudio.Containers.Wave;
using VGAudio.Formats.Pcm16;

namespace PenguinTools.CRI;

internal static class ExtractService
{
    public static ExtractManifest Extract(
        string sourcePath,
        string outputDirectory,
        string? pairedInputPath,
        ulong hcaKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        string? acbName = null;
        uint? previewStartMs = null;
        uint? previewStopMs = null;
        string awbPath;

        if (extension == ".acb")
        {
            var cueSheet = new CriTable();
            cueSheet.Load(sourcePath);
            acbName = cueSheet.Rows.Count > 0 ? cueSheet.Rows[0]["Name"] as string : null;
            (previewStartMs, previewStopMs) = ReadPreview(cueSheet);

            awbPath = !string.IsNullOrWhiteSpace(pairedInputPath)
                ? pairedInputPath
                : Path.ChangeExtension(sourcePath, ".awb");
            if (!File.Exists(awbPath))
                throw new FileNotFoundException($"Paired AWB was not found at '{awbPath}'.", awbPath);
        }
        else if (extension == ".awb")
        {
            awbPath = sourcePath;
        }
        else
        {
            throw new InvalidOperationException($"unsupported CRI extraction source: {sourcePath}");
        }

        var decoded = DecodeAwb(awbPath, hcaKey);
        Directory.CreateDirectory(outputDirectory);

        var cues = new List<ExtractedCue>(decoded.Count);
        for (var index = 0; index < decoded.Count; index++)
        {
            var track = decoded[index];
            var stem = SanitizeName(acbName);
            if (string.IsNullOrEmpty(stem))
                stem = track.CueId.ToString();

            var filename = $"{index:D4}_{stem}.wav";
            var wavPath = Path.Combine(outputDirectory, filename);
            File.WriteAllBytes(wavPath, track.WavBytes);

            cues.Add(new ExtractedCue(
                track.CueId,
                acbName,
                wavPath,
                track.Channels,
                track.SampleRate,
                track.BitsPerSample,
                track.SampleFrames,
                previewStartMs,
                previewStopMs));
        }

        return new ExtractManifest(1, sourcePath, cues);
    }

    public static string SerializeManifest(ExtractManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, CriJsonContext.Default.ExtractManifest);
    }

    private static (uint? StartMs, uint? StopMs) ReadPreview(CriTable cueSheet)
    {
        try
        {
            if (cueSheet.Rows.Count == 0) return (null, null);
            if (cueSheet.Rows[0]["TrackEventTable"] is not byte[] trackEventBytes) return (null, null);

            var trackEventTable = new CriTable();
            trackEventTable.Load(trackEventBytes);
            if (trackEventTable.Rows.Count < 2) return (null, null);
            if (trackEventTable.Rows[1]["Command"] is not byte[] command || command.Length < 21)
                return (null, null);

            var start = BinaryPrimitives.ReadUInt32BigEndian(command.AsSpan(3, 4));
            var stop = BinaryPrimitives.ReadUInt32BigEndian(command.AsSpan(17, 4));
            return (start, stop);
        }
        catch
        {
            return (null, null);
        }
    }

    private static List<DecodedTrack> DecodeAwb(string awbPath, ulong hcaKey)
    {
        using var awbStream = File.OpenRead(awbPath);
        var archive = new CriAfs2Archive();
        archive.Read(awbStream);

        var effectiveKey = ApplySubKey(hcaKey, archive.SubKey);
        var tracks = new List<DecodedTrack>(archive.Count);
        foreach (var entry in archive)
        {
            using var hcaStream = entry.Open(awbStream);
            var reader = new HcaReader { EncryptionKey = new CriHcaKey(effectiveKey) };
            var audio = ReadHcaWithoutStdoutNoise(reader, hcaStream);
            var pcm = audio.GetFormat<Pcm16Format>();

            using var wavStream = new MemoryStream();
            new WaveWriter().WriteToStream(audio, wavStream);
            tracks.Add(new DecodedTrack(
                (int)entry.Id,
                wavStream.ToArray(),
                (ushort)pcm.ChannelCount,
                (uint)pcm.SampleRate,
                16,
                (uint)pcm.SampleCount));
        }

        return tracks;
    }

    private static VGAudio.Formats.AudioData ReadHcaWithoutStdoutNoise(HcaReader reader, Stream hcaStream)
    {
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(TextWriter.Null);
            return reader.Read(hcaStream);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    internal static ulong ApplySubKey(ulong keyCode, ushort subKey)
    {
        if (subKey == 0) return keyCode;
        var multiplier = ((ulong)subKey << 16) | unchecked((ushort)(~subKey + 2));
        return keyCode * multiplier;
    }

    private static string SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length == 0 || builder[^1] != '-')
                builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    private sealed record DecodedTrack(
        int CueId,
        byte[] WavBytes,
        ushort Channels,
        uint SampleRate,
        ushort BitsPerSample,
        uint SampleFrames);
}
