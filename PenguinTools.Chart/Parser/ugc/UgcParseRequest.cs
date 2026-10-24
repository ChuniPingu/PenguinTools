using PenguinTools.Core.Asset;

namespace PenguinTools.Chart.Parser.ugc;

public sealed record UgcParseRequest(string Path, AssetManager Assets,
    PenguinTools.Core.Metadata.ChartDialect Dialect = PenguinTools.Core.Metadata.ChartDialect.Auto);
