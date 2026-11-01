using PenguinTools.Core.Asset;
using PenguinTools.Infrastructure;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

public sealed record MusicExportContext(
    AssetManager Assets,
    IAudioService Audio,
    IImageService Images,
    IAssetStore AssetStore,
    IInfrastructureAssetProvider AssetProvider);
