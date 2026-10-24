namespace PenguinTools.Core.Metadata;

public partial record Meta
{
    public string MgxcId { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string Copyright { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string LicenseUrl { get; set; } = string.Empty;
    public int ExVersion { get; set; } = 1;
    public bool ExLongEnabled { get; set; } = true;
    public bool WaitBgmEnd { get; set; }
    public string BackgroundScene { get; set; } = string.Empty;
    public bool BackgroundSync { get; set; } = true;
    public string FieldColorName { get; set; } = string.Empty;
    public int FieldColorIndex { get; set; } = -1;
    public string FieldImage { get; set; } = string.Empty;
    public string FieldScene { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string AuthorSites { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;

    public bool IsMain { get; set; } = true; // used in option convert

    public string FileName => Path.GetFileName(FilePath);

    private string GetFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(FilePath);
        return string.IsNullOrWhiteSpace(folder) ? path : Path.GetFullPath(Path.Combine(folder, path));
    }
}
