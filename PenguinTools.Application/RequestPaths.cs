namespace PenguinTools.Application;

internal static class RequestPaths
{
    internal static string FullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path.Trim());
    }

    internal static string? OptionalFullPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : FullPath(path);
    }

    internal static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
    }
}
