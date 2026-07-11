using PenguinTools.Core.Asset;
using PenguinTools.Infrastructure;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

public sealed record MusicExportContext(
    AssetManager Assets,
    IMediaTool MediaTool,
    IAssetStore AssetStore,
    IInfrastructureAssetProvider AssetProvider);