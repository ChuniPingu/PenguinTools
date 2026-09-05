namespace PenguinTools.Tests;

internal static class TestTempPaths
{
    public static string Create(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"PenguinTools-{Guid.NewGuid():N}{extension}");
    }
}
