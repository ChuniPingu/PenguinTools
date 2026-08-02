using System.ComponentModel;
using PenguinTools.Core.Asset;

namespace PenguinTools.Core.Metadata;

public partial record Meta
{
    public int? Id
    {
        get;
        set
        {
            if (StageId - 1000000 == Id) StageId = value + 1000000;
            if (UnlockEventId - 1000000 == Id) UnlockEventId = value + 1000000;
            field = value;
        }
    }

    public string Title { get; set; } = string.Empty;
    public string SortName { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public Entry? Genre { get; set; }
    public string Designer { get; set; } = string.Empty;
    public DateTime ReleaseDate { get; set; } = DateTime.Now;

    public Difficulty Difficulty { get; set; } = Difficulty.Master;
    public decimal Level { get; set; }

    public decimal MainBpm { get; set; }
    public int MainTil { get; set; }

    public int? UnlockEventId { get; set; }
    public Entry WeTag { get; set; } = Entry.Default;
    public StarDifficulty WeDifficulty { get; set; } = StarDifficulty.Na;
}

public enum Difficulty
{
    [Description("Basic")] Basic = 0,
    [Description("Advanced")] Advanced = 1,
    [Description("Expert")] Expert = 2,
    [Description("Master")] Master = 3,
    [Description("Ultima")] Ultima = 4,
    [Description("World's End")] WorldsEnd = 5
}

public enum StarDifficulty
{
    [Description("N/A")] Na = 0,
    [Description("⭐")] S1 = 1,
    [Description("⭐⭐")] S2 = 3,
    [Description("⭐⭐⭐")] S3 = 5,
    [Description("⭐⭐⭐⭐")] S4 = 7,
    [Description("⭐⭐⭐⭐⭐")] S5 = 9
}