namespace PenguinTools.Core.IO;

public static class TempFileNames
{
    public static string MakeUnique(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var safeName = Path.GetFileName(fileName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(stem)) stem = "tmp";

        return $"{stem}.{Guid.NewGuid():N}{extension}";
    }
}
