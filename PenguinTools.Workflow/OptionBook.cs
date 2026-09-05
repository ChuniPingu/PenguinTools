using PenguinTools.Core.Asset;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Workflow;

/// <summary>A book view whose metadata comes from its selected main difficulty.</summary>
public sealed record OptionBook(
    Difficulty MainDifficulty,
    IReadOnlyDictionary<Difficulty, OptionDifficulty> Difficulties)
{
    public Meta BookMeta => Difficulties[MainDifficulty].Meta;
    public bool IsCustomStage => BookMeta.IsCustomStage;
    public int? StageId => BookMeta.StageId;
    public Entry NotesFieldLine => BookMeta.NotesFieldLine;
    public Entry Stage => BookMeta.Stage;
    public string Title => BookMeta.Title;
}
