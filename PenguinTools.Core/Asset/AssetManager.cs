using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PenguinTools.Core.Asset;

public class AssetManager : INotifyPropertyChanged
{
    /// <summary>Conventional filename for collected (plus-tier) asset JSON written by the host.</summary>
    public const string PlusAssetsFileName = "assets.user.json";

    public AssetManager(Stream hardAssets, string? userAssetsPath = null)
    {
        ArgumentNullException.ThrowIfNull(hardAssets);

        MergeAssets = new AssetDictionary();
        HardAssets = new AssetDictionary(hardAssets);
        if (!string.IsNullOrWhiteSpace(userAssetsPath) &&
            AssetDictionary.TryLoadPlusAssetsFromFile(userAssetsPath, out var plus))
            PlusAssets = plus;
        else
            PlusAssets = new AssetDictionary();
        UserAssets = new AssetDictionary();
        Merge();
        NotifyAssetChanged();
    }

    // Asset Dictionary that merges all assets from various sources below
    public AssetDictionary MergeAssets { get; }

    // Assets embedded in the assembly, used for default values and initial setup.
    private AssetDictionary HardAssets { get; }

    // Assets loaded from an explicit --user-assets path (optional).
    private AssetDictionary PlusAssets { get; }

    // Assets from the user-defined.
    private AssetDictionary UserAssets { get; }

    public IReadOnlySet<Entry> this[AssetType type] => MergeAssets[type];
    public IReadOnlySet<Entry> GenreNames => MergeAssets.GenreNames;
    public IReadOnlySet<Entry> FieldLines => MergeAssets.FieldLines;
    public IReadOnlySet<Entry> StageNames => MergeAssets.StageNames;
    public IReadOnlySet<Entry> WeTagNames => MergeAssets.WeTagNames;

    /// <summary>
    ///     Scans a game install, subtracts entries already present in <paramref name="hardAssets" />,
    ///     and writes the delta JSON to <paramref name="outputPath" />. Does not mutate this manager.
    /// </summary>
    public static async Task CollectToFileAsync(
        string gameRoot,
        Stream hardAssets,
        string outputPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentNullException.ThrowIfNull(hardAssets);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Directory.Exists(gameRoot)) return;

        var collected = new AssetDictionary();
        collected.MergeWith(await AssetDictionary.CollectAsync(gameRoot, ct));
        collected.SubtractWith(new AssetDictionary(hardAssets));

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        await collected.SaveAsync(outputPath, ct);
    }

    private void Merge()
    {
        MergeAssets.Clear();
        MergeAssets.MergeWith(HardAssets, PlusAssets, UserAssets);
    }

    public void DefineEntry(AssetType type, Entry entry)
    {
        UserAssets[type].Add(entry);
        MergeAssets[type].Add(entry);
        NotifyAssetChanged(type);
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void NotifyAssetChanged(AssetType? type = null)
    {
        OnPropertyChanged(nameof(MergeAssets));

        if (type is not null)
        {
            OnPropertyChanged(type.ToString());
            return;
        }

        OnPropertyChanged(nameof(GenreNames));
        OnPropertyChanged(nameof(FieldLines));
        OnPropertyChanged(nameof(StageNames));
        OnPropertyChanged(nameof(WeTagNames));
    }

    #endregion
}
