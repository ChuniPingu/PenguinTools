using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using SonicAudioLib.Archives;
using SonicAudioLib.CriMw;
using VGAudio.Codecs.CriHca;
using VGAudio.Containers.Hca;
using VGAudio.Containers.Wave;

namespace PenguinTools.CRI;

internal static class ConvertService
{
    public const ulong DefaultHcaKey = 32931609366120192UL;
    public const uint DefaultBitrate = 16384 * 8;

    public static void Convert(
        string wavPath,
        string acbPath,
        string awbPath,
        string name,
        long previewStartMs,
        long previewStopMs,
        ulong hcaKey,
        uint bitrate = DefaultBitrate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(acbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(awbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (previewStopMs < previewStartMs)
            throw new InvalidOperationException("preview stop must not be earlier than preview start");

        var previewStart = ClampPreview(previewStartMs);
        var previewStop = ClampPreview(previewStopMs);

        var waveReader = new WaveReader();
        var wav = waveReader.ReadFormat(wavPath);
        if (wav.ChannelCount != 2 || wav.SampleRate != 48000)
            throw new InvalidOperationException("WAV must be stereo 48 kHz PCM");

        var hcaWriter = new HcaWriter();
        var config = new HcaConfiguration
        {
            Bitrate = (int)bitrate,
            Quality = CriHcaQuality.Highest,
            TrimFile = false,
            EncryptionKey = new CriHcaKey(hcaKey)
        };

        using var hcaStream = new MemoryStream();
        hcaWriter.WriteToStream(wav, hcaStream, config);
        hcaStream.Seek(0, SeekOrigin.Begin);

        var cueSheetTable = new CriTable();
        using (var dummyAcb = OpenDummyAcb())
            cueSheetTable.Load(dummyAcb);

        cueSheetTable.Rows[0]["Name"] = name;

        var cueTable = new CriTable();
        cueTable.Load(cueSheetTable.Rows[0]["CueTable"] as byte[]);
        var lengthMs = (int)Math.Round(wav.SampleCount * 1000.0 / wav.SampleRate);
        cueTable.Rows[0]["Length"] = lengthMs;
        cueTable.WriterSettings = CriTableWriterSettings.Adx2Settings;
        cueSheetTable.Rows[0]["CueTable"] = cueTable.Save();

        var trackEventTable = new CriTable();
        trackEventTable.Load(cueSheetTable.Rows[0]["TrackEventTable"] as byte[]);
        var cmdData = trackEventTable.Rows[1]["Command"] as byte[]
                      ?? throw new InvalidOperationException("TrackEventTable preview command is missing");
        using (var cmdStream = new MemoryStream(cmdData))
        using (var bw = new BinaryWriter(cmdStream, Encoding.Default, leaveOpen: true))
        {
            cmdStream.Position = 3;
            bw.WriteUInt32BigEndian(previewStart);
            cmdStream.Position = 17;
            bw.WriteUInt32BigEndian(previewStop);
            trackEventTable.Rows[1]["Command"] = cmdStream.ToArray();
        }

        cueSheetTable.Rows[0]["TrackEventTable"] = trackEventTable.Save();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(awbPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(acbPath))!);

        var awbEntry = new CriAfs2Entry { Stream = hcaStream };
        var awbArchive = new CriAfs2Archive { awbEntry };
        using (var awbStream = File.Create(awbPath))
        {
            awbArchive.Save(awbStream);
            awbStream.Position = 0;

            var streamAwbHashTbl = new CriTable();
            streamAwbHashTbl.Load(cueSheetTable.Rows[0]["StreamAwbHash"] as byte[]);
            var sha = SHA1.HashData(awbStream);
            streamAwbHashTbl.Rows[0]["Name"] = name;
            streamAwbHashTbl.Rows[0]["Hash"] = sha;
            cueSheetTable.Rows[0]["StreamAwbHash"] = streamAwbHashTbl.Save();
        }

        var waveformTable = new CriTable();
        waveformTable.Load(cueSheetTable.Rows[0]["WaveformTable"] as byte[]);
        waveformTable.Rows[0]["SamplingRate"] = (ushort)wav.SampleRate;
        waveformTable.Rows[0]["NumSamples"] = wav.SampleCount;
        cueSheetTable.Rows[0]["WaveformTable"] = waveformTable.Save();

        cueSheetTable.WriterSettings = CriTableWriterSettings.Adx2Settings;
        using var acbStream = File.Create(acbPath);
        cueSheetTable.Save(acbStream);
    }

    private static uint ClampPreview(long value)
    {
        if (value < 0) return 0;
        if (value > uint.MaxValue) return uint.MaxValue;
        return (uint)value;
    }

    private static Stream OpenDummyAcb()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream("PenguinTools.CRI.Assets.dummy.acb");
        if (stream is null)
            throw new InvalidOperationException("Embedded dummy.acb resource is missing");
        return stream;
    }
}

file static class BinaryWriterExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32BigEndian(this BinaryWriter bw, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        bw.Write(buffer);
    }
}
