namespace PenguinTools.Infrastructure;

public static class AssetPaths
{
    public const string DefaultSubdirectory = "assets";
    public const string PathEnvironmentVariable = "PENGUIN_TOOLS_ASSETS_PATH";

    public static string Resolve(string? overrideDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
            return Path.GetFullPath(overrideDirectory.Trim());

        var fromEnv = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return Path.GetFullPath(fromEnv.Trim());

        return Path.Combine(AppContext.BaseDirectory, DefaultSubdirectory);
    }
}