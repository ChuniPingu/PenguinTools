using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PenguinTools.Core;
using PenguinTools.Media;

namespace PenguinTools.Infrastructure;

public sealed class MuaMediaTool(string assetDirectory) : IMediaTool
{
    internal const double TargetLoudnessLufs = -8.5;
    internal const double TargetTruePeakDbtp = 0.0;
    private const double TargetLoudnessRangeLu = 11.0;
    private const double OffsetToleranceSeconds = 0.000_1;

    private string AssetDirectory { get; } = RequireDirectory(assetDirectory, nameof(assetDirectory));

    private string MuaDirectory => Path.Combine(AssetDirectory, "mua");

    private string FfmpegDirectory => Path.Combine(AssetDirectory, "ffmpeg");

    private string CriDirectory => Path.Combine(AssetDirectory, "cri");

    private string FfmpegExecutablePath => ResolveExecutable(FfmpegDirectory, "ffmpeg");
    private string ImgExecutablePath => ResolveMuaExecutable("mua_img");
    private string CriExecutablePath => ResolveCriExecutable();

    public async Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        ArgumentException.ThrowIfNullOrWhiteSpace(dst);

        var sourcePath = Path.GetFullPath(src);
        var destinationPath = Path.GetFullPath(dst);
        var outputDirectory = Path.GetDirectoryName(destinationPath)
                              ?? throw new InvalidOperationException("Audio output directory could not be resolved.");
        Directory.CreateDirectory(outputDirectory);
        var nonce = Guid.NewGuid().ToString("N");
        var statsFileName = $".penguintools-loudness-{nonce}.json";
        var statsPath = Path.Combine(outputDirectory, statsFileName);
        var temporaryPath = Path.Combine(outputDirectory, $".penguintools-audio-{nonce}.wav");

        try
        {
            var analysisArgs = CreateAnalysisArguments(sourcePath, offset, statsFileName);
            var analysis = await RunAsync(FfmpegExecutablePath, analysisArgs, ct, outputDirectory);
            analysis.ThrowIfFailed(MsgKeys.Error_Invalid_audio);

            if (!TryReadLoudnessStats(statsPath, out var stats, out var parseError))
            {
                var failure = new ProcessCommandResult(
                    CreateStartInfo(FfmpegExecutablePath, analysisArgs, outputDirectory),
                    (int)InterExitCode.Failure,
                    analysis.StandardOutput,
                    string.Join(Environment.NewLine, analysis.StandardError, parseError).Trim());
                failure.ThrowIfFailed(MsgKeys.Error_Invalid_audio);
                throw new UnreachableException();
            }

            var conversionArgs = CreateConversionArguments(sourcePath, temporaryPath, offset, CalculateGainDb(stats));
            var converted = await RunAsync(FfmpegExecutablePath, conversionArgs, ct, outputDirectory);
            converted.ThrowIfFailed(MsgKeys.Error_Invalid_audio);
            File.Move(temporaryPath, destinationPath, true);
            return converted;
        }
        finally
        {
            TryDelete(statsPath);
            TryDelete(temporaryPath);
        }
    }

    public async Task<ProcessCommandResult> CheckAudioValidAsync(string src, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        return await RunAsync(FfmpegExecutablePath, [
            "-hide_banner",
            "-nostdin",
            "-nostats",
            "-loglevel", "error",
            "-xerror",
            "-i", src,
            "-map", "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-f", "null",
            "-"
        ], ct);
    }

    public async Task<ProcessCommandResult> CheckImageValidAsync(string src, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        return await RunAsync(ImgExecutablePath, ["check", "-s", src], ct);
    }

    public async Task ConvertJacketAsync(string src, string dst, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        ArgumentException.ThrowIfNullOrWhiteSpace(dst);

        var ret = await RunAsync(ImgExecutablePath, ["jacket", "-s", src, "-d", dst], ct);
        ret.ThrowIfFailed(MsgKeys.Error_Invalid_jk_image);
    }

    public async Task ConvertStageAsync(string bg, string stDst, string nfDst, string?[]? fxPaths,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bg);
        ArgumentException.ThrowIfNullOrWhiteSpace(stDst);
        ArgumentException.ThrowIfNullOrWhiteSpace(nfDst);

        var args = new List<string>
        {
            "stage",
            "-b", bg,
            "-d", stDst,
            "-n", nfDst
        };

        for (var i = 0; fxPaths is not null && i < fxPaths.Length && i < 4; i++)
        {
            var fxPath = fxPaths[i];
            if (string.IsNullOrWhiteSpace(fxPath)) continue;

            args.Add($"--fx{i + 1}");
            args.Add(fxPath);
        }

        var ret = await RunAsync(ImgExecutablePath, args, ct);
        ret.ThrowIfFailed(MsgKeys.Error_Invalid_bg_image);
    }

    public async Task ExtractDdsAsync(string src, string dst, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        ArgumentException.ThrowIfNullOrWhiteSpace(dst);

        var ret = await RunAsync(ImgExecutablePath, ["extract-dds", "-s", src, "-d", dst], ct);
        ret.ThrowIfFailed(MsgKeys.Error_Invalid_bg_image);
    }

    public async Task<DdsDecodeResult> DecodeDdsAsync(string src, string dst, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        ArgumentException.ThrowIfNullOrWhiteSpace(dst);
        var ret = await RunAsync(ImgExecutablePath, ["decode-dds", "-s", src, "-d", dst], ct);
        ret.ThrowIfFailed(MsgKeys.Error_Invalid_bg_image);
        return new DdsDecodeResult(src, dst);
    }

    public async Task<CriExtractResult> ExtractCriAudioAsync(CriExtractOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var args = new List<string> { "extract", options.SourcePath, options.OutputDirectory };
        if (!string.IsNullOrWhiteSpace(options.PairedInputPath))
        {
            args.Add("--paired-input");
            args.Add(options.PairedInputPath);
        }

        if (options.HcaKey is { } key)
        {
            args.Add("--hca-key");
            args.Add(key.ToString(CultureInfo.InvariantCulture));
        }

        var ret = await RunAsync(CriExecutablePath, args, ct);
        ret.ThrowIfFailed(MsgKeys.Error_Invalid_audio);
        return JsonSerializer.Deserialize(ret.StandardOutput, InfrastructureJsonContext.Default.CriExtractResult)
               ?? throw new JsonException("PenguinTools.CRI returned an empty extraction manifest.");
    }

    public async Task ConvertCriAsync(
        string wav,
        string acb,
        string awb,
        string name,
        long previewStartMs,
        long previewStopMs,
        ulong hcaKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wav);
        ArgumentException.ThrowIfNullOrWhiteSpace(acb);
        ArgumentException.ThrowIfNullOrWhiteSpace(awb);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var ret = await RunAsync(CriExecutablePath, [
            "convert",
            "--wav", wav,
            "--acb", acb,
            "--awb", awb,
            "--name", name,
            "--preview-start-ms", previewStartMs.ToString(CultureInfo.InvariantCulture),
            "--preview-stop-ms", previewStopMs.ToString(CultureInfo.InvariantCulture),
            "--hca-key", hcaKey.ToString(CultureInfo.InvariantCulture)
        ], ct);
        ret.ThrowIfFailed(MsgKeys.Error_Invalid_audio);
    }

    private string ResolveMuaExecutable(string name)
    {
        return ResolveExecutable(MuaDirectory, name);
    }

    private string ResolveCriExecutable()
    {
        const string name = "PenguinTools.CRI";
        return ResolveExecutable(CriDirectory, name);
    }

    private static string ResolveExecutable(string directory, string name)
    {
        var fileName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        var path = Path.Combine(directory, fileName);
        ResourceStoreHelpers.EnsureExecutableIfNeeded(path, name);
        return path;
    }

    private static IReadOnlyList<string> CreateAnalysisArguments(string source, decimal offset,
        string statsFileName)
    {
        var filters = CreateOffsetFilters(offset);
        filters.Add(
            $"loudnorm=I={FormatNumber(TargetLoudnessLufs)}:LRA={FormatNumber(TargetLoudnessRangeLu)}:" +
            $"TP={FormatNumber(TargetTruePeakDbtp)}:linear=true:print_format=json:stats_file={statsFileName}");
        return [
            "-hide_banner",
            "-nostdin",
            "-nostats",
            "-loglevel", "error",
            "-xerror",
            "-i", source,
            "-map", "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-af", string.Join(',', filters),
            "-f", "null",
            "-"
        ];
    }

    private static IReadOnlyList<string> CreateConversionArguments(string source, string destination, decimal offset,
        double gainDb)
    {
        var filters = CreateOffsetFilters(offset);
        filters.Add($"volume={FormatNumber(gainDb)}dB:precision=double");
        filters.Add("aformat=sample_fmts=s16:sample_rates=48000:channel_layouts=stereo");
        return [
            "-hide_banner",
            "-nostdin",
            "-nostats",
            "-loglevel", "error",
            "-xerror",
            "-y",
            "-i", source,
            "-map", "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-af", string.Join(',', filters),
            "-c:a", "pcm_s16le",
            "-ar", "48000",
            "-ac", "2",
            "-f", "wav",
            destination
        ];
    }

    internal static double CalculateGainDb(FfmpegLoudnessStats stats)
    {
        var loudnessGain = TargetLoudnessLufs - stats.InputIntegratedLufs;
        var peakLimitedGain = TargetTruePeakDbtp - stats.InputTruePeakDbtp;
        return Math.Min(loudnessGain, peakLimitedGain);
    }

    private static List<string> CreateOffsetFilters(decimal offset)
    {
        var seconds = decimal.ToDouble(offset);
        if (Math.Abs(seconds) < OffsetToleranceSeconds) return [];
        if (seconds > 0)
        {
            var milliseconds = Math.Round(seconds * 1_000.0, MidpointRounding.AwayFromZero);
            return [$"adelay=delays={FormatNumber(milliseconds)}:all=1"];
        }

        return [$"atrim=start={FormatNumber(-seconds)}", "asetpts=PTS-STARTPTS"];
    }

    internal static bool TryReadLoudnessStats(string path, out FfmpegLoudnessStats stats, out string error)
    {
        stats = default;
        error = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var inputI = ReadJsonNumber(root, "input_i");
            var inputTp = ReadJsonNumber(root, "input_tp");
            var inputLra = ReadJsonNumber(root, "input_lra");
            if (!double.IsFinite(inputI) || !double.IsFinite(inputTp) || !double.IsFinite(inputLra))
                throw new InvalidDataException("FFmpeg returned non-finite loudness statistics.");
            stats = new FfmpegLoudnessStats(inputI, inputTp, inputLra);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidDataException
                                   or KeyNotFoundException)
        {
            error = $"Unable to read FFmpeg loudness statistics: {ex.Message}";
            return false;
        }
    }

    private static double ReadJsonNumber(JsonElement root, string propertyName)
    {
        var property = root.GetProperty(propertyName);
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetDouble(),
            JsonValueKind.String => double.Parse(property.GetString()!, CultureInfo.InvariantCulture),
            _ => throw new JsonException($"FFmpeg loudness property '{propertyName}' is not numeric.")
        };
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.#########", CultureInfo.InvariantCulture);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private static string RequireDirectory(string directoryPath, string paramName)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentNullException(paramName);
        return directoryPath;
    }

    private static async Task<ProcessCommandResult> RunAsync(string executablePath, IEnumerable<string> args,
        CancellationToken ct = default, string? workingDirectory = null)
    {
        var argumentList = args as IList<string> ?? [.. args];
        var startInfo = CreateStartInfo(executablePath, argumentList, workingDirectory);
        if (!File.Exists(executablePath))
            return new ProcessCommandResult(startInfo, (int)InterExitCode.Failure, string.Empty, string.Empty);

        using var proc = new Process();
        proc.StartInfo = startInfo;

        try
        {
            proc.Start();
        }
        catch (Exception)
        {
            return new ProcessCommandResult(startInfo, (int)InterExitCode.Failure, string.Empty, string.Empty);
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        try
        {
            await Task.WhenAll(proc.WaitForExitAsync(ct), stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            if (!proc.HasExited)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    // Best effort cleanup after a failed tool invocation.
                }
            }

            return new ProcessCommandResult(startInfo, (int)InterExitCode.Failure, string.Empty, string.Empty);
        }

        return new ProcessCommandResult(startInfo, proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath, IEnumerable<string> argumentList,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory)) psi.WorkingDirectory = workingDirectory;

        foreach (var arg in argumentList) psi.ArgumentList.Add(arg);
        return psi;
    }
}

internal readonly record struct FfmpegLoudnessStats(
    double InputIntegratedLufs,
    double InputTruePeakDbtp,
    double InputLoudnessRangeLu);
