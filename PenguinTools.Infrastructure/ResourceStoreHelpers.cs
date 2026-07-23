using System.Diagnostics;

namespace PenguinTools.Infrastructure;

internal static class ResourceStoreHelpers
{
    private static readonly HashSet<string> ExternalExecutables = new(StringComparer.Ordinal)
    {
        "mua_wav",
        "mua_img",
        "PenguinTools.CRI"
    };

    public static void EnsureExecutableIfNeeded(string path, string resourceName)
    {
        if (OperatingSystem.IsWindows()) return;
        if (!ExternalExecutables.Contains(resourceName)) return;

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public static void ClearDirectory(string directoryPath, bool deleteRoot)
    {
        if (!Directory.Exists(directoryPath)) return;

        foreach (var entryPath in Directory.GetFileSystemEntries(directoryPath))
            try
            {
                if (Directory.Exists(entryPath))
                    Directory.Delete(entryPath, true);
                else
                    File.Delete(entryPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

        if (!deleteRoot) return;

        try
        {
            Directory.Delete(directoryPath, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}