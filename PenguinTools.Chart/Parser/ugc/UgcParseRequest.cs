using PenguinTools.Core.Asset;

namespace PenguinTools.Chart.Parser.ugc;

public sealed record UgcParseRequest(string Path, AssetManager Assets);
