using PenguinTools.Core.Metadata;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Workflow;

/// <summary>A difficulty view over the parsed chart, including its current metadata.</summary>
public sealed record OptionDifficulty(UmgrModel.Chart Chart)
{
    public Meta Meta => Chart.Meta;
    public Difficulty Difficulty => Meta.Difficulty;
    public int? SongId => Meta.Id;
}
