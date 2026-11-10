namespace PenguinTools.Workflow;

public sealed record ExportOutputPaths(
    string MusicFolder,
    string StageFolder,
    string CueFileFolder,
    string EventFolder,
    string ReleaseTagPath)
{
    private string BundleRoot => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(MusicFolder)))!;
    public string WallFolder => Path.Combine(BundleRoot, "ddsFieldWall");
    public string WallBindingsPath => Path.Combine(BundleRoot, "wallbind.json");

    /// <summary>Resolves the on-disk bundle root from a working directory and output folder name.</summary>
    public static string ResolveBundleRootPath(string workingDirectory, string optionName)
    {
        var normalized = Path.TrimEndingDirectorySeparator(workingDirectory);
        var folder = Path.GetFileName(normalized);
        return folder == optionName ? workingDirectory : Path.Combine(workingDirectory, optionName);
    }

    public static ExportOutputPaths FromOptionDirectory(string rootPath)
    {
        return new ExportOutputPaths(
            Path.Combine(rootPath, "music"),
            Path.Combine(rootPath, "stage"),
            Path.Combine(rootPath, "cueFile"),
            Path.Combine(rootPath, "event"),
            Path.Combine(rootPath, "releaseTag"));
    }
}
