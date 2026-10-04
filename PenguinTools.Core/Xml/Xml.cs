using System.Xml.Linq;
using System.Xml.Serialization;
using PenguinTools.Core.Asset;

namespace PenguinTools.Core.Xml;

public abstract class XmlElement
{
    protected abstract string FileName { get; }

    [XmlElement("dataName")] public string DataName { get; set; } = string.Empty;

    public async Task<string> SaveDirectoryAsync(string baseFolder)
    {
        var folder = Path.Combine(baseFolder, DataName);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, FileName);
        var document = XmlDocumentFactory.Create(this);

        await using var stream = File.Create(path);
        await document.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
        return folder;
    }
}

public class PathElement
{
    [XmlElement("path")] public string Path { get; set; } = string.Empty;

    public static implicit operator string(PathElement elem)
    {
        return elem.Path;
    }

    public static implicit operator PathElement(string value)
    {
        return new PathElement { Path = value };
    }
}

public class ValueElement
{
    [XmlElement("value")] public int Value { get; set; }

    public static implicit operator int(ValueElement elem)
    {
        return elem.Value;
    }

    public static implicit operator ValueElement(int value)
    {
        return new ValueElement { Value = value };
    }
}

public class EntryCollection
{
    [XmlArray("list")]
    [XmlArrayItem("StringID")]
    public List<Entry> List { get; private init; } = [];

    public static implicit operator List<Entry>(EntryCollection elem)
    {
        return elem.List;
    }

    public static implicit operator EntryCollection(List<Entry> value)
    {
        return new EntryCollection { List = value };
    }
}
