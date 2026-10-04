using PenguinTools.Core;

namespace PenguinTools.Infrastructure;

/// <summary>
///     Resolves the temp work directory.
///     Override the parent directory with <c>PENGUIN_TOOLS_TEMP</c>.
/// </summary>
public sealed class ApplicationPaths : IApplicationPaths
{
    public const string TempEnvironmentVariable = "PENGUIN_TOOLS_TEMP";

    private const string DefaultTempSubfolder = "PenguinTools.Temp";

    private ApplicationPaths(string tempWorkPath)
    {
        TempWorkPath = tempWorkPath;
    }

    public string TempWorkPath { get; }

    public static ApplicationPaths Create()
    {
        // AssetStore owns and deletes this directory. Sharing its root across applications
        // would let one CLI invocation delete another invocation's active image workspaces.
        var tempWorkPath = Path.Combine(ResolveTempWorkPath(), "session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkPath);
        return new ApplicationPaths(tempWorkPath);
    }

    private static string ResolveTempWorkPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable(TempEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv.Trim());
        }

        return Path.Combine(Path.GetTempPath(), DefaultTempSubfolder);
    }
}
