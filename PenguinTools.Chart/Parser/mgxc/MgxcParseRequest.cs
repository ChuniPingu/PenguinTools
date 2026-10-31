using PenguinTools.Core.Asset;

namespace PenguinTools.Chart.Parser.mgxc;

public sealed record MgxcParseRequest(string Path, AssetManager Assets,
    PenguinTools.Core.Metadata.ChartDialect Dialect = PenguinTools.Core.Metadata.ChartDialect.Auto);
