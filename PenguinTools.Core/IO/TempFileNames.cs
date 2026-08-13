namespace PenguinTools.Core.IO;

public static class TempFileNames
{
    public static string MakeUnique(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var safeName = GetFileNameOnly(fileName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(stem)) stem = "tmp";

        return $"{stem}.{Guid.NewGuid():N}{extension}";
    }

    private static string GetFileNameOnly(string path)
    {
        var separatorIndex = path.AsSpan().LastIndexOfAny('/', '\\');
        return separatorIndex >= 0 ? path[(separatorIndex + 1)..] : path;
    }
}
