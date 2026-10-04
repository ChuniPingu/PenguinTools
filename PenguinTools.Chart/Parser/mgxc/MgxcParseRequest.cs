using PenguinTools.Core.Asset;

namespace PenguinTools.Chart.Parser.mgxc;

public sealed record MgxcParseRequest(string Path, AssetManager Assets);
