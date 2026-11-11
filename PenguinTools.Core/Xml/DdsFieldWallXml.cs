using PenguinTools.Core.Asset;

namespace PenguinTools.Core.Xml;

public sealed class DdsFieldWallXml : XmlElement
{
    public DdsFieldWallXml(int id)
    {
        Metadata.ResourceIds.Require(id, MsgKeys.Error_Wall_id_invalid);
        DataName = $"ddsFieldWall{id:0000}";
        Name = new Entry(id, $"custom_field_wall_{id}");
        Image = $"CHU_UI_Fieldwall_{id:0000}.dds";
    }

    protected override string FileName => "DDSFieldWall.xml";
    public Entry Name { get; }
    public Entry NetOpenName { get; set; } = XmlConstants.NetOpenName;
    public bool DisableFlag { get; set; }
    public PathElement Image { get; }
    public bool DefaultHave { get; set; }
    public string ExplainText { get; set; } = string.Empty;
}
