using System.Diagnostics;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;
using Xunit;

namespace PenguinTools.Tests.Media;

public sealed class AudioConverterTests
{
    [Fact]
    public async Task NormalizationFailure_StopsBeforeCreatingOutput()
    {
        var directory = Directory.CreateTempSubdirectory("PenguinToolsTests-");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var source = Path.Combine(directory.FullName, "source.wav");
            await File.WriteAllTextAsync(source, "source", ct);
            var output = Path.Combine(directory.FullName, "output");
            var audio = new FailingAudioService();
            var converter = new AudioConverter(new(new Meta { Id = 1, BgmFilePath = source },
                output, Path.Combine(directory.FullName, "normalized.wav"), 0), audio);
            var failure = await Assert.ThrowsAsync<DiagnosticException>(() => converter.ConvertAsync(ct));
            Assert.Equal(MsgKeys.Error_Invalid_audio, failure.Descriptor.Key);
            Assert.False(audio.CriConversionStarted);
            Assert.False(Directory.Exists(output));
        }
        finally { directory.Delete(true); }
    }

    private sealed class FailingAudioService : IAudioService
    {
        public bool CriConversionStarted { get; private set; }

        public Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset,
            CancellationToken ct = default) => Task.FromResult(new ProcessCommandResult(
                new ProcessStartInfo { FileName = "ffmpeg" }, (int)InterExitCode.Failure, "", "decode failed"));

        public Task ConvertCriAsync(CriConvertRequest request, CancellationToken ct = default)
        {
            CriConversionStarted = true;
            return Task.CompletedTask;
        }

        public Task<MediaValidationResult> CheckAudioValidAsync(string src, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CriExtractResult> ExtractCriAudioAsync(CriExtractOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
