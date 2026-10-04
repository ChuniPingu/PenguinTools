using System.Xml.Linq;
using PenguinTools.Core.Asset;

namespace PenguinTools.Core.Xml;

internal static class XmlDocumentFactory
{
    private const string DataNameElement = "dataName";

    internal static XDocument Create(XmlElement value)
    {
        return value switch
        {
            CueFileXml cueFile => CreateCueFile(cueFile),
            EventXml eventXml => CreateEvent(eventXml),
            MusicXml musicXml => CreateMusic(musicXml),
            ReleaseTag releaseTag => CreateReleaseTag(releaseTag),
            StageXml stageXml => CreateStage(stageXml),
            _ => throw new NotSupportedException($"Unsupported xml element type '{value.GetType().FullName}'.")
        };
    }

    private static XDocument CreateCueFile(CueFileXml value)
    {
        return CreateDocument("CueFileData",
        [
            Element(DataNameElement, value.DataName),
            EntryElement("name", value.Name),
            PathElement("acbFile", value.AcbFile),
            PathElement("awbFile", value.AwbFile)
        ]);
    }

    private static XDocument CreateEvent(EventXml value)
    {
        return CreateDocument("EventData",
        [
            Element(DataNameElement, value.DataName),
            EntryElement("netOpenName", value.NetOpenName),
            EntryElement("name", value.Name),
            Element("text", value.Text),
            EntryElement("ddsBannerName", value.DdsBannerName),
            Element("periodDispType", value.PeriodDispType),
            Element("alwaysOpen", value.AlwaysOpen),
            Element("teamOnly", value.TeamOnly),
            Element("isKop", value.IsKop),
            Element("priority", value.Priority),
            CreateSubstancesElement(value.Substances)
        ]);
    }

    private static XDocument CreateMusic(MusicXml value)
    {
        return CreateDocument("MusicData",
        [
            Element(DataNameElement, value.DataName),
            EntryElement("releaseTagName", value.ReleaseTagName),
            EntryElement("netOpenName", value.NetOpenName),
            Element("disableFlag", value.DisableFlag),
            Element("exType", value.ExType),
            EntryElement("name", value.Name),
            Element("sortName", value.SortName),
            EntryElement("artistName", value.ArtistName),
            EntryCollectionElement("genreNames", value.GenreNames),
            EntryElement("worksName", value.WorksName),
            EntryElement("labelName", value.LabelName),
            PathElement("jaketFile", value.JaketFile),
            Element("firstLock", value.FirstLock),
            Element("enableUltima", value.EnableUltima),
            Element("isGiftMusic", value.IsGiftMusic),
            Element("releaseDate", value.ReleaseDate),
            Element("priority", value.Priority),
            EntryElement("cueFileName", value.CueFileName),
            EntryElement("worldsEndTagName", value.WorldsEndTagName),
            Element("starDifType", value.StarDifType),
            EntryElement("stageName", value.StageName),
            new XElement("fumens", value.Fumens.Select(CreateMusicFumenElement))
        ]);
    }

    private static XDocument CreateReleaseTag(ReleaseTag value)
    {
        return CreateDocument("ReleaseTagData",
        [
            Element(DataNameElement, value.DataName),
            EntryElement("name", value.Name),
            Element("titleName", value.TitleName)
        ]);
    }

    private static XDocument CreateStage(StageXml value)
    {
        return CreateDocument("StageData",
        [
            Element(DataNameElement, value.DataName),
            EntryElement("netOpenName", value.NetOpenName),
            EntryElement("releaseTagName", value.ReleaseTagName),
            EntryElement("name", value.Name),
            EntryElement("notesFieldLine", value.NotesFieldLine),
            PathElement("notesFieldFile", value.NotesFieldFile),
            PathElement("baseFile", value.BaseFile),
            PathElement("objectFile", value.ObjectFile)
        ]);
    }

    private static XElement CreateMusicFumenElement(MusicFumenData value)
    {
        return new XElement("MusicFumenData",
            EntryElement("type", value.Type),
            Element("enable", value.Enable),
            PathElement("file", value.File),
            Element("level", value.Level),
            Element("levelDecimal", value.LevelDecimal),
            Element("notesDesigner", value.NotesDesigner),
            Element("defaultBpm", value.DefaultBpm));
    }

    private static XElement CreateSubstancesElement(Substances value)
    {
        return new XElement("substances",
            Element("type", value.Type),
            ValueElement("flag", value.Flag),
            CreateInformationElement(value.Information),
            CreateMapElement(value.Map),
            CreateMusicElement(value.Music),
            CreateAdvertiseMovieElement(value.AdvertiseMovie),
            CreateRecommendMusicElement(value.RecommendMusic),
            ValueElement("release", value.Release),
            CreateCourseElement(value.Course),
            CreateQuestElement(value.Quest),
            CreateDuelElement(value.Duel),
            CreateCMissionElement(value.CMission),
            ValueElement("changeSurfBoardUI", value.ChangeSurfBoardUi),
            CreateAvatarAccessoryGachaElement(value.AvatarAccessoryGacha),
            CreateRightsInfoElement(value.RightsInfo),
            CreatePlayRewardSetElement(value.PlayRewardSet),
            CreateDailyBonusPresetElement(value.DailyBonusPreset),
            CreateMatchingBonusElement(value.MatchingBonus),
            CreateUnlockChallengeElement(value.UnlockChallenge));
    }

    private static XElement CreateInformationElement(Information value)
    {
        return new XElement("information",
            Element("informationType", value.InformationType),
            Element("informationDispType", value.InformationDispType),
            EntryElement("mapFilterID", value.MapFilterId),
            EntryCollectionElement("courseNames", value.CourseNames),
            Element("text", value.Text),
            PathElement("image", value.Image),
            EntryElement("movieName", value.MovieName),
            EntryCollectionElement("presentNames", value.PresentNames));
    }

    private static XElement CreateMapElement(Map value)
    {
        return new XElement("map",
            Element("tagText", value.TagText),
            EntryElement("mapName", value.MapName),
            EntryCollectionElement("musicNames", value.MusicNames));
    }

    private static XElement CreateMusicElement(MusicElement value)
    {
        return new XElement("music",
            Element("musicType", value.MusicType),
            EntryCollectionElement("musicNames", value.MusicNames));
    }

    private static XElement CreateAdvertiseMovieElement(AdvertiseMovie value)
    {
        return new XElement("advertiseMovie",
            EntryElement("firstMovieName", value.FirstMovieName),
            EntryElement("secondMovieName", value.SecondMovieName));
    }

    private static XElement CreateRecommendMusicElement(RecommendMusic value)
    {
        return new XElement("recommendMusic",
            EntryCollectionElement("musicNames", value.MusicNames));
    }

    private static XElement CreateCourseElement(CourseElement value)
    {
        return new XElement("course",
            EntryCollectionElement("courseNames", value.CourseNames));
    }

    private static XElement CreateQuestElement(QuestElement value)
    {
        return new XElement("quest",
            EntryCollectionElement("questNames", value.QuestNames));
    }

    private static XElement CreateDuelElement(DuelElement value)
    {
        return new XElement("duel",
            EntryElement("duelName", value.DuelName));
    }

    private static XElement CreateCMissionElement(CMissionElement value)
    {
        return new XElement("cmission",
            EntryElement("cmissionName", value.CMissionName));
    }

    private static XElement CreateAvatarAccessoryGachaElement(AvatarAccessoryGachaElement value)
    {
        return new XElement("avatarAccessoryGacha",
            EntryElement("avatarAccessoryGachaName", value.AvatarAccessoryGachaName));
    }

    private static XElement CreateRightsInfoElement(RightsInfoElement value)
    {
        return new XElement("rightsInfo",
            EntryCollectionElement("rightsNames", value.RightsNames));
    }

    private static XElement CreatePlayRewardSetElement(PlayRewardSetElement value)
    {
        return new XElement("playRewardSet",
            EntryElement("playRewardSetName", value.PlayRewardSetName));
    }

    private static XElement CreateDailyBonusPresetElement(DailyBonusPresetElement value)
    {
        return new XElement("dailyBonusPreset",
            EntryElement("dailyBonusPresetName", value.DailyBonusPresetName));
    }

    private static XElement CreateMatchingBonusElement(MatchingBonusElement value)
    {
        return new XElement("matchingBonus",
            EntryElement("timeTableName", value.TimeTableName));
    }

    private static XElement CreateUnlockChallengeElement(UnlockChallengeElement value)
    {
        return new XElement("unlockChallenge",
            EntryElement("unlockChallengeName", value.UnlockChallengeName));
    }

    private static XDocument CreateDocument(string rootName, IEnumerable<XElement> content)
    {
        var root = new XElement(rootName,
            new XAttribute(XNamespace.Xmlns + "xsi", XmlConstants.XmlnsXsi),
            new XAttribute(XNamespace.Xmlns + "xsd", XmlConstants.XmlnsXsd),
            content);

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static XElement Element(string name, object? value)
    {
        return new XElement(name, value ?? string.Empty);
    }

    private static XElement EntryElement(string name, Entry? value)
    {
        var resolved = value ?? Entry.Default;
        return new XElement(name,
            Element("id", resolved.Id),
            Element("str", resolved.Str),
            Element("data", resolved.Data));
    }

    private static XElement EntryCollectionElement(string name, EntryCollection? value)
    {
        var entries = value?.List ?? [];
        return new XElement(name,
            new XElement("list", entries.Select(entry => new XElement("StringID",
                Element("id", entry.Id),
                Element("str", entry.Str),
                Element("data", entry.Data)))));
    }

    private static XElement PathElement(string name, PathElement? value)
    {
        return new XElement(name, Element("path", value?.Path ?? string.Empty));
    }

    private static XElement ValueElement(string name, ValueElement? value)
    {
        return new XElement(name, Element("value", value?.Value ?? 0));
    }
}