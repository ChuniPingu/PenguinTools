using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace PenguinTools.Core.Asset;

public enum AssetType
{
    [JsonStringEnumMemberName("genreNames")]
    GenreNames,

    [JsonStringEnumMemberName("notesFieldLine")]
    FieldLines,

    [JsonStringEnumMemberName("stageName")]
    StageNames,

    [JsonStringEnumMemberName("worldsEndTagName")]
    WeTagNames
}

public class AssetDictionary()
{
    private static readonly AssetJsonSerializerContext JsonContext = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });

    private readonly Dictionary<AssetType, SortedSet<Entry>> _database = CreateDatabase();

    public AssetDictionary(string path) : this()
    {
        if (!File.Exists(path)) return;
        var json = File.ReadAllText(path);
        Load(json);
    }

    public AssetDictionary(Stream stream) : this()
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();
        Load(json);
    }

    public IReadOnlySet<Entry> GenreNames => _database[AssetType.GenreNames];
    public IReadOnlySet<Entry> FieldLines => _database[AssetType.FieldLines];
    public IReadOnlySet<Entry> StageNames => _database[AssetType.StageNames];
    public IReadOnlySet<Entry> WeTagNames => _database[AssetType.WeTagNames];

    public SortedSet<Entry> this[AssetType type]
    {
        get => _database[type];
        set => _database[type] = value;
    }

    private void Load(string json)
    {
        var dict = JsonSerializer.Deserialize(json, JsonContext.AssetDatabase);
        if (dict == null) return;
        MergeWith(dict);
    }

    /// <summary>
    ///     Loads plus-tier JSON from disk when present and valid. Returns false if the file is missing,
    ///     empty, unreadable, or invalid JSON; <paramref name="dictionary" /> is empty in that case.
    /// </summary>
    public static bool TryLoadPlusAssetsFromFile(string path, out AssetDictionary dictionary)
    {
        dictionary = new AssetDictionary();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            var dict = JsonSerializer.Deserialize(json, JsonContext.AssetDatabase);
            if (dict is null) return false;

            dictionary.MergeWith(dict);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void MergeWith(Dictionary<AssetType, SortedSet<Entry>> databases)
    {
        foreach (var (assetType, sourceSet) in databases) _database[assetType].UnionWith(sourceSet);
    }

    public void MergeWith(params AssetDictionary[] databases)
    {
        foreach (var db in databases)
        foreach (var (assetType, sourceSet) in db._database)
            _database[assetType].UnionWith(sourceSet);
    }

    public void SubtractWith(params AssetDictionary[] databases)
    {
        foreach (var db in databases)
        foreach (var (assetType, sourceSet) in db._database)
            _database[assetType].ExceptWith(sourceSet);
    }

    public async Task SaveAsync(string path, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(_database, JsonContext.AssetDatabase);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public void Clear()
    {
        foreach (var set in _database.Values) set.Clear();
    }

    #region Collect

    public static async Task<Dictionary<AssetType, SortedSet<Entry>>> CollectAsync(string workDir,
        CancellationToken ct = default)
    {
        var specs = new (string FileName, AssetType Type)[]
        {
            ("Music.xml", AssetType.GenreNames),
            ("Music.xml", AssetType.WeTagNames),
            ("Music.xml", AssetType.StageNames),
            ("Stage.xml", AssetType.FieldLines)
        };
        return await CollectManyAsync(workDir, specs, ct);
    }

    private static async Task<List<Entry>> ReadEntriesAsync(string path, string entryName,
        CancellationToken ct = default)
    {
        var entries = new List<Entry>();
        XDocument doc;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
        }
        catch (XmlException)
        {
            return entries;
        }
        catch (IOException)
        {
            return entries;
        }

        ct.ThrowIfCancellationRequested();

        var node = doc.Root?.Element(entryName);
        if (node is null) return entries;

        var listNode = node.Element("list");
        if (listNode != null)
        {
            var stringIdNodes = listNode.Elements("StringID");
            foreach (var stringIdNode in stringIdNodes)
            {
                ct.ThrowIfCancellationRequested();

                var id = (stringIdNode.Element("id")?.Value ?? string.Empty).Trim();
                var str = (stringIdNode.Element("str")?.Value ?? string.Empty).Trim();
                var data = (stringIdNode.Element("data")?.Value ?? string.Empty).Trim();
                if (int.TryParse(id, out var val)) entries.Add(new Entry(val, str, data));
            }
        }
        else
        {
            var id = (node.Element("id")?.Value ?? string.Empty).Trim();
            var str = (node.Element("str")?.Value ?? string.Empty).Trim();
            var data = (node.Element("data")?.Value ?? string.Empty).Trim();
            if (int.TryParse(id, out var val)) entries.Add(new Entry(val, str, data));
        }

        return entries;
    }

    private static async Task<SortedSet<Entry>> CollectOneAsync(string root, string fileName, string entryName,
        CancellationToken ct = default)
    {
        var result = new SortedSet<Entry>();
        foreach (var scanRoot in EnumerateCollectionRoots(root))
        {
            var walker = Directory.EnumerateFiles(scanRoot, fileName, SearchOption.AllDirectories);
            foreach (var xmlFile in walker)
            {
                ct.ThrowIfCancellationRequested();
                var entries = await ReadEntriesAsync(xmlFile, entryName, ct);
                foreach (var entry in entries) result.Add(entry);
            }
        }

        return result;
    }

    public static async Task<Dictionary<AssetType, SortedSet<Entry>>> CollectManyAsync(string root,
        IEnumerable<(string FileName, AssetType Type)> specs, CancellationToken ct = default)
    {
        var aggregated = CreateDatabase();

        foreach (var (fileName, type) in specs)
        {
            ct.ThrowIfCancellationRequested();
            var entryName = GetEntryName(type);
            var entries = await CollectOneAsync(root, fileName, entryName, ct);
            aggregated[type].UnionWith(entries);
        }

        return aggregated;
    }

    private static Dictionary<AssetType, SortedSet<Entry>> CreateDatabase()
    {
        var database = new Dictionary<AssetType, SortedSet<Entry>>();
        foreach (var type in Enum.GetValues<AssetType>()) database[type] = [];
        return database;
    }

    private static string GetEntryName(AssetType type)
    {
        return type switch
        {
            AssetType.GenreNames => "genreNames",
            AssetType.FieldLines => "notesFieldLine",
            AssetType.StageNames => "stageName",
            AssetType.WeTagNames => "worldsEndTagName",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported asset type.")
        };
    }

    private static IEnumerable<string> EnumerateCollectionRoots(string root)
    {
        if (IsAllowedAssetFolder(root))
        {
            yield return root;
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            if (IsAllowedAssetFolder(directory))
                yield return directory;
    }

    private static bool IsAllowedAssetFolder(string path)
    {
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (folderName.Length != 4 || folderName[0] != 'A') return false;
        return int.TryParse(folderName.AsSpan(1), out var value) && value is >= 0 and <= 300;
    }

    #endregion
}