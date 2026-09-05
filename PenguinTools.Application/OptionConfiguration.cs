using System.Text.Json;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.IO;
using PenguinTools.Workflow;

namespace PenguinTools.Application;

internal static class OptionConfiguration
{
    internal static async Task<(string? ConfigPath, OptionDocument? Document,
            DiagnosticSnapshot Diagnostics)>
        LoadForScanAsync(string input, CancellationToken cancellationToken)
    {
        var candidate = Path.Combine(input, "options.json");
        if (!File.Exists(candidate)) return (null, null, DiagnosticSnapshot.Empty);

        try
        {
            var document = await LoadAsync(candidate, cancellationToken);
            return (candidate, document, DiagnosticSnapshot.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            var collector = new DiagnosticCollector();
            collector.Report(new PathDiagnostic(Severity.Warning,
                Msg.Create(MsgKeys.Warn_Config_invalid, ex.Message), candidate));
            return (candidate, null, DiagnosticSnapshot.Create(collector));
        }
    }

    internal static string? ResolveLoadPath(OptionBuildRequest request, string input)
    {
        if (request.SkipConfig) return null;
        if (!string.IsNullOrWhiteSpace(request.ConfigPath)) return Path.GetFullPath(request.ConfigPath.Trim());
        var candidate = Path.Combine(input, "options.json");
        return File.Exists(candidate) ? candidate : null;
    }

    internal static string ResolveSavePath(OptionBuildRequest request, string input, string? loadedConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(request.ConfigPath)) return Path.GetFullPath(request.ConfigPath.Trim());
        if (loadedConfigPath is not null) return loadedConfigPath;
        return Path.Combine(input, "options.json");
    }

    internal static async Task<OptionDocument> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Option configuration file was not found.", path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, ApplicationJsonContext.Default.OptionDocument,
                   cancellationToken)
               ?? throw new JsonException("Option configuration file was empty.");
    }

    internal static Task SaveAsync(string path, OptionDocument document,
        CancellationToken cancellationToken)
    {
        return AtomicFile.WriteAsync(path,
            (stream, ct) => JsonSerializer.SerializeAsync(stream, document,
                ApplicationJsonContext.Default.OptionDocument, ct), cancellationToken);
    }
}
