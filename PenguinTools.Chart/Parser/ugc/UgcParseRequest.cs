using PenguinTools.Core.Asset;

namespace PenguinTools.Chart.Parser.ugc;

public sealed record UgcParseRequest(string Path, AssetManager Assets, string? Content = null)
{
    /// <summary>Omits editor-only regions when C2S export can derive them from notes.</summary>
    public bool ForC2sExport { get; init; }
}
