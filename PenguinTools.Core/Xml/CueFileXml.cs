using System.Xml.Serialization;
using PenguinTools.Core.Asset;

namespace PenguinTools.Core.Xml;

[XmlRoot("CueFileData")]
public class CueFileXml : XmlElement
{
    internal CueFileXml()
    {
    }

    public CueFileXml(int id)
    {
        DataName = $"cueFile{id:000000}";
        Name = new Entry(id, $"music{id:0000}");
        AcbFile = $"music{id:0000}.acb";
        AwbFile = $"music{id:0000}.awb";
    }

    protected override string FileName => "CueFile.xml";

    [XmlElement("name")] public Entry Name { get; set; } = Entry.Default;

    [XmlElement("acbFile")] public PathElement AcbFile { get; set; } = new();

    [XmlElement("awbFile")] public PathElement AwbFile { get; set; } = new();
}
