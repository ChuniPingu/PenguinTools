using System.Xml.Serialization;
using PenguinTools.Core.Asset;

namespace PenguinTools.Core.Xml;

[XmlRoot("EventData")]
public class EventXml : XmlElement
{
    public enum MusicType
    {
        Normal = 0,
        WldEnd = 1,
        Ultima = 2
    }

    internal EventXml()
    {
    }

    public EventXml(int eventId, MusicType type, IEnumerable<Entry> musics)
    {
        var eventName = type switch
        {
            MusicType.WldEnd => "WORLD'S END曲開放",
            MusicType.Ultima => "ULT曲開放",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
        DataName = $"event{eventId:00000000}";
        Name = new Entry(eventId, eventName);

        Substances.Type = 3;
        Substances.Music.MusicType = (int)type;
        Substances.Music.MusicNames.List.AddRange(musics);
    }

    protected override string FileName => "Event.xml";

    [XmlElement("netOpenName")] public Entry NetOpenName { get; set; } = XmlConstants.NetOpenName;

    [XmlElement("name")] public Entry Name { get; set; } = Entry.Default;

    [XmlElement("text")] public string Text { get; set; } = string.Empty;

    [XmlElement("ddsBannerName")] public Entry DdsBannerName { get; set; } = Entry.Default;

    [XmlElement("periodDispType")] public int PeriodDispType { get; set; } = 1;

    [XmlElement("alwaysOpen")] public bool AlwaysOpen { get; set; } = true;

    [XmlElement("teamOnly")] public bool TeamOnly { get; set; }

    [XmlElement("isKop")] public bool IsKop { get; set; }

    [XmlElement("priority")] public int Priority { get; set; }

    [XmlElement("substances")] public Substances Substances { get; set; } = new();
}

public class Substances
{
    [XmlElement("type")] public int Type { get; set; }

    [XmlElement("flag")] public ValueElement Flag { get; set; } = new();

    [XmlElement("information")] public Information Information { get; set; } = new();

    [XmlElement("map")] public Map Map { get; set; } = new();

    [XmlElement("music")] public MusicElement Music { get; set; } = new();

    [XmlElement("advertiseMovie")] public AdvertiseMovie AdvertiseMovie { get; set; } = new();

    [XmlElement("recommendMusic")] public RecommendMusic RecommendMusic { get; set; } = new();

    [XmlElement("release")] public ValueElement Release { get; set; } = new();

    [XmlElement("course")] public CourseElement Course { get; set; } = new();

    [XmlElement("quest")] public QuestElement Quest { get; set; } = new();

    [XmlElement("duel")] public DuelElement Duel { get; set; } = new();

    [XmlElement("cmission")] public CMissionElement CMission { get; set; } = new();

    [XmlElement("changeSurfBoardUI")] public ValueElement ChangeSurfBoardUi { get; set; } = new();

    [XmlElement("avatarAccessoryGacha")] public AvatarAccessoryGachaElement AvatarAccessoryGacha { get; set; } = new();

    [XmlElement("rightsInfo")] public RightsInfoElement RightsInfo { get; set; } = new();

    [XmlElement("playRewardSet")] public PlayRewardSetElement PlayRewardSet { get; set; } = new();

    [XmlElement("dailyBonusPreset")] public DailyBonusPresetElement DailyBonusPreset { get; set; } = new();

    [XmlElement("matchingBonus")] public MatchingBonusElement MatchingBonus { get; set; } = new();

    [XmlElement("unlockChallenge")] public UnlockChallengeElement UnlockChallenge { get; set; } = new();
}

public class Information
{
    [XmlElement("informationType")] public int InformationType { get; set; }

    [XmlElement("informationDispType")] public int InformationDispType { get; set; }

    [XmlElement("mapFilterID")] public Entry MapFilterId { get; set; } = Entry.Default;

    [XmlElement("courseNames")] public EntryCollection CourseNames { get; set; } = new();

    [XmlElement("text")] public string Text { get; set; } = string.Empty;

    [XmlElement("image")] public PathElement Image { get; set; } = new();

    [XmlElement("movieName")] public Entry MovieName { get; set; } = Entry.Default;

    [XmlElement("presentNames")] public EntryCollection PresentNames { get; set; } = new();
}

public class Map
{
    [XmlElement("tagText")] public string TagText { get; set; } = string.Empty;

    [XmlElement("mapName")] public Entry MapName { get; set; } = Entry.Default;

    [XmlElement("musicNames")] public EntryCollection MusicNames { get; set; } = new();
}

public class MusicElement
{
    [XmlElement("musicType")] public int MusicType { get; set; }

    [XmlElement("musicNames")] public EntryCollection MusicNames { get; set; } = new();
}

public class AdvertiseMovie
{
    [XmlElement("firstMovieName")] public Entry FirstMovieName { get; set; } = Entry.Default;

    [XmlElement("secondMovieName")] public Entry SecondMovieName { get; set; } = Entry.Default;
}

public class RecommendMusic
{
    [XmlElement("musicNames")] public EntryCollection MusicNames { get; set; } = new();
}

public class CourseElement
{
    [XmlElement("courseNames")] public EntryCollection CourseNames { get; set; } = new();
}

public class QuestElement
{
    [XmlElement("questNames")] public EntryCollection QuestNames { get; set; } = new();
}

public class DuelElement
{
    [XmlElement("duelName")] public Entry DuelName { get; set; } = Entry.Default;
}

public class CMissionElement
{
    [XmlElement("cmissionName")] public Entry CMissionName { get; set; } = Entry.Default;
}

public class AvatarAccessoryGachaElement
{
    [XmlElement("avatarAccessoryGachaName")]
    public Entry AvatarAccessoryGachaName { get; set; } = Entry.Default;
}

public class RightsInfoElement
{
    [XmlElement("rightsNames")] public EntryCollection RightsNames { get; set; } = new();
}

public class PlayRewardSetElement
{
    [XmlElement("playRewardSetName")] public Entry PlayRewardSetName { get; set; } = Entry.Default;
}

public class DailyBonusPresetElement
{
    [XmlElement("dailyBonusPresetName")] public Entry DailyBonusPresetName { get; set; } = Entry.Default;
}

public class MatchingBonusElement
{
    [XmlElement("timeTableName")] public Entry TimeTableName { get; set; } = Entry.Default;
}

public class UnlockChallengeElement
{
    [XmlElement("unlockChallengeName")] public Entry UnlockChallengeName { get; set; } = Entry.Default;
}
