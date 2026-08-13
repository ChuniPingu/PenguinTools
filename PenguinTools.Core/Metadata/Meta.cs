namespace PenguinTools.Core.Metadata;

public partial record Meta
{
    public string MgxcId { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;

    public bool IsMain { get; set; } = true; // used in option convert

    public string FileName => Path.GetFileName(FilePath);

    private string GetFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) return path;
        var folder = Path.GetDirectoryName(FilePath);
        return string.IsNullOrWhiteSpace(folder) ? path : Path.GetFullPath(Path.Combine(folder, path));
    }
}