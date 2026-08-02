using System.Xml.Serialization;
using PenguinTools.Core.Asset;

namespace PenguinTools.Core.Xml;

[XmlRoot("ReleaseTagData")]
public class ReleaseTag : XmlElement<ReleaseTag>
{
    public const int DefaultId = 0;
    public const string DefaultTitleName = "CHUNITHM";
    public const int CustomDefaultId = 99;
    public const string CustomDefaultTitleName = "自制譜";

    public static readonly ReleaseTag Default = new(DefaultId);

    internal ReleaseTag()
    {
    }

    public ReleaseTag(int id, string titleName = DefaultTitleName)
    {
        Id = id;
        DataName = $"releaseTag{id:000000}";
        TitleName = string.IsNullOrWhiteSpace(titleName) ? DefaultTitleName : titleName.Trim();
    }

    protected override string FileName => "ReleaseTag.xml";

    [XmlIgnore]
    public int Id
    {
        get;
        set
        {
            field = value;
            DataName = $"releaseTag{value:000000}";
        }
    }

    [XmlElement("name")] public Entry Name => new(Id, TitleName);

    [XmlElement("titleName")] public string TitleName { get; set; } = string.Empty;
}
