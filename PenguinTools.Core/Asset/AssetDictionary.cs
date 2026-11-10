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
    WeTagNames,

    [JsonStringEnumMemberName("releaseTagName")]
    ReleaseTagNames,

    [JsonStringEnumMemberName("ddsFieldWallName")]
    FieldWallNames
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
        if (!File.Exists(path))
        {
            return;
        }

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
    public IReadOnlySet<Entry> ReleaseTagNames => _database[AssetType.ReleaseTagNames];
    public IReadOnlySet<Entry> FieldWallNames => _database[AssetType.FieldWallNames];

    public SortedSet<Entry> this[AssetType type]
    {
        get => _database[type];
        set => _database[type] = value;
    }

    private void Load(string json)
    {
        var dict = JsonSerializer.Deserialize(json, JsonContext.AssetDatabase);
        if (dict == null)
        {
            return;
        }

        MergeWith(dict);
    }

    /// <summary>
    ///     Loads plus-tier JSON from disk when present and valid. Returns false if the file is missing,
    ///     empty, unreadable, or invalid JSON; <paramref name="dictionary" /> is empty in that case.
    /// </summary>
    public static bool TryLoadPlusAssetsFromFile(string path, out AssetDictionary dictionary)
    {
        dictionary = new AssetDictionary();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

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

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var dict = JsonSerializer.Deserialize(json, JsonContext.AssetDatabase);
            if (dict is null)
            {
                return false;
            }

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
        foreach (var (assetType, sourceSet) in databases)
        {
            _database[assetType].UnionWith(sourceSet);
        }
    }

    public void MergeWith(params AssetDictionary[] databases)
    {
        foreach (var db in databases)
        {
            foreach (var (assetType, sourceSet) in db._database)
            {
                _database[assetType].UnionWith(sourceSet);
            }
        }
    }

    public void SubtractWith(params AssetDictionary[] databases)
    {
        foreach (var db in databases)
        {
            foreach (var (assetType, sourceSet) in db._database)
            {
                _database[assetType].ExceptWith(sourceSet);
            }
        }
    }

    public async Task SaveAsync(string path, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(_database, JsonContext.AssetDatabase);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public void Clear()
    {
        foreach (var set in _database.Values)
        {
            set.Clear();
        }
    }

    private const string MusicXmlFileName = "Music.xml";

    #region Collect

    public static async Task<Dictionary<AssetType, SortedSet<Entry>>> CollectAsync(string workDir,
        CancellationToken ct = default)
    {
        var specs = new (string FileName, AssetType Type)[]
        {
            (MusicXmlFileName, AssetType.GenreNames),
            (MusicXmlFileName, AssetType.WeTagNames),
            (MusicXmlFileName, AssetType.StageNames),
            (MusicXmlFileName, AssetType.ReleaseTagNames),
            ("Stage.xml", AssetType.FieldLines),
            ("DDSFieldWall.xml", AssetType.FieldWallNames)
        };
        return await CollectManyAsync(workDir, specs, ct);
    }

    private static async Task<XDocument?> ReadDocumentAsync(string path,
        CancellationToken ct = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, ct);
        }
        catch (XmlException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void ReadEntries(XDocument doc, string entryName, SortedSet<Entry> entries,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var node = doc.Root?.Element(entryName);
        if (node is null)
        {
            return;
        }

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
                if (int.TryParse(id, out var val))
                {
                    entries.Add(new Entry(val, str, data));
                }
            }
        }
        else
        {
            var id = (node.Element("id")?.Value ?? string.Empty).Trim();
            var str = (node.Element("str")?.Value ?? string.Empty).Trim();
            var data = (node.Element("data")?.Value ?? string.Empty).Trim();
            if (int.TryParse(id, out var val))
            {
                entries.Add(new Entry(val, str, data));
            }
        }
    }

    public static async Task<Dictionary<AssetType, SortedSet<Entry>>> CollectManyAsync(string root,
        IEnumerable<(string FileName, AssetType Type)> specs, CancellationToken ct = default)
    {
        var aggregated = CreateDatabase();
        List<CollectionRequest> requests = [];
        foreach (var (fileName, type) in specs)
        {
            ct.ThrowIfCancellationRequested();
            requests.Add(new CollectionRequest(fileName, type, GetEntryName(type)));
        }

        if (requests.Count == 0)
        {
            return aggregated;
        }

        var roots = EnumerateCollectionRoots(root, ct).ToArray();
        foreach (var group in requests.GroupBy(request => request.FileName, StringComparer.Ordinal))
        {
            foreach (var scanRoot in roots)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var xmlFile in Directory.EnumerateFiles(scanRoot, group.Key, SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var doc = await ReadDocumentAsync(xmlFile, ct);
                    if (doc is null)
                    {
                        continue;
                    }

                    foreach (var request in group)
                    {
                        ReadEntries(doc, request.EntryName, request.Entries, ct);
                    }
                }
            }
        }

        // Merge in caller order: different filename patterns may supply the same type and ID.
        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            aggregated[request.Type].UnionWith(request.Entries);
        }

        return aggregated;
    }

    private sealed record CollectionRequest(string FileName, AssetType Type, string EntryName)
    {
        internal SortedSet<Entry> Entries { get; } = [];
    }

    private static Dictionary<AssetType, SortedSet<Entry>> CreateDatabase()
    {
        var database = new Dictionary<AssetType, SortedSet<Entry>>();
        foreach (var type in Enum.GetValues<AssetType>())
        {
            database[type] = [];
        }

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
            AssetType.ReleaseTagNames => "releaseTagName",
            AssetType.FieldWallNames => "name",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported asset type.")
        };
    }

    private static IEnumerable<string> EnumerateCollectionRoots(string root, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (IsAllowedAssetFolder(root))
        {
            yield return root;
            yield break;
        }

        const int maxDepth = 8;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (path, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            var children = GetAccessibleDirectories(path);

            foreach (var directory in children)
            {
                ct.ThrowIfCancellationRequested();
                if (IsAllowedAssetFolder(directory))
                {
                    yield return directory;
                    continue;
                }

                if (depth + 1 < maxDepth)
                {
                    queue.Enqueue((directory, depth + 1));
                }
            }
        }
    }

    private static string[] GetAccessibleDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsAllowedAssetFolder(string path)
    {
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (folderName.Length != 4 || folderName[0] != 'A')
        {
            return false;
        }

        return int.TryParse(folderName.AsSpan(1), out var value) && value is >= 0 and <= 300;
    }

    #endregion
}
