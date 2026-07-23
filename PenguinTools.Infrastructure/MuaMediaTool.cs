using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PenguinTools.Core;
using PenguinTools.Media;

namespace PenguinTools.Infrastructure;

public sealed class MuaMediaTool(string assetDirectory) : IMediaTool
{
    private string AssetDirectory { get; } = RequireDirectory(assetDirectory, nameof(assetDirectory));

    private string MuaDirectory => Path.Combine(AssetDirectory, "mua");

    private string CriDirectory => Path.Combine(AssetDirectory, "cri");

    private string WavExecutablePath => ResolveMuaExecutable("mua_wav");
    private string ImgExecutablePath => ResolveMuaExecutable("mua_img");
    private string CriExecutablePath => ResolveCriExecutable();

    public async Task<ProcessCommandResult> NormalizeAudioAsync(string src, string dst, decimal offset,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        ArgumentException.ThrowIfNullOrWhiteSpace(dst);

        var ret = await RunAsync(WavExecutablePath, [
            "normalize",
            "-s", src,
            "-d", dst,
            "-o", Math.Round(offset, 6).ToString(CultureInfo.InvariantCulture)
        ], ct);

        ret.ThrowIfFailed(MsgKeys.Error_Invalid_audio);
        return ret;
    }

    public async Task<ProcessCommandResult> CheckAudioValidAsync(string src, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(src);
        return await RunAsync(WavExecutablePath, ["check", "-s", src], ct);
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
        var fileName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        var path = Path.Combine(MuaDirectory, fileName);
        ResourceStoreHelpers.EnsureExecutableIfNeeded(path, name);
        return path;
    }

    private string ResolveCriExecutable()
    {
        const string name = "PenguinTools.CRI";
        var fileName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        var path = Path.Combine(CriDirectory, fileName);
        ResourceStoreHelpers.EnsureExecutableIfNeeded(path, name);
        return path;
    }

    private static string RequireDirectory(string directoryPath, string paramName)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentNullException(paramName);
        return directoryPath;
    }

    private static async Task<ProcessCommandResult> RunAsync(string executablePath, IEnumerable<string> args,
        CancellationToken ct = default)
    {
        var argumentList = args as IList<string> ?? [.. args];
        var startInfo = CreateStartInfo(executablePath, argumentList);
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

    private static ProcessStartInfo CreateStartInfo(string executablePath, IList<string> argumentList)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in argumentList) psi.ArgumentList.Add(arg);
        return psi;
    }
}
