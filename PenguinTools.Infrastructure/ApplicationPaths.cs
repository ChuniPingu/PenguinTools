using PenguinTools.Core;

namespace PenguinTools.Infrastructure;

/// <summary>
///     Resolves temp and user-data directories.
///     Override with <c>PENGUIN_TOOLS_TEMP</c> and <c>PENGUIN_TOOLS_USER_DATA</c>.
/// </summary>
public sealed class ApplicationPaths : IApplicationPaths
{
    public const string TempEnvironmentVariable = "PENGUIN_TOOLS_TEMP";
    public const string UserDataEnvironmentVariable = "PENGUIN_TOOLS_USER_DATA";

    private const string DefaultTempSubfolder = "PenguinTools.Temp";
    private const string AppFolderName = "PenguinTools";

    private ApplicationPaths(string tempWorkPath, string userDataPath)
    {
        TempWorkPath = tempWorkPath;
        UserDataPath = userDataPath;
    }

    public string TempWorkPath { get; }
    public string UserDataPath { get; }

    public static ApplicationPaths Create()
    {
        var tempWorkPath = ResolveTempWorkPath();
        var userDataPath = ResolveUserDataPath();
        Directory.CreateDirectory(tempWorkPath);
        Directory.CreateDirectory(userDataPath);
        return new ApplicationPaths(tempWorkPath, userDataPath);
    }

    private static string ResolveTempWorkPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable(TempEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return Path.GetFullPath(fromEnv.Trim());

        return Path.Combine(Path.GetTempPath(), DefaultTempSubfolder);
    }

    private static string ResolveUserDataPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable(UserDataEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return Path.GetFullPath(fromEnv.Trim());

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, AppFolderName);
    }
}