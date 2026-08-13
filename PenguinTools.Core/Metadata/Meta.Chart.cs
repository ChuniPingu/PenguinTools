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

    public int? C2sJudgeTap { get; set; }
    public int? C2sJudgeHld { get; set; }
    public int? C2sJudgeSld { get; set; }
    public int? C2sJudgeAir { get; set; }
    public int? C2sJudgeFlk { get; set; }
    public int? C2sJudgeAll { get; set; }

    public int? C2sJudgeHldProxyBaseline { get; set; }
    public int? C2sJudgeSldProxyBaseline { get; set; }
    public int? C2sJudgeAirProxyBaseline { get; set; }

    public string? C2sSlaSnapshot { get; set; }
    public string? C2sSlpSnapshot { get; set; }
    public string? C2sSlaEditKey { get; set; }
    public string? C2sSlpEditKey { get; set; }

    public int? C2sMeterDefDenominator { get; set; }
    public int? C2sMeterDefNumerator { get; set; }

    public bool TryGetC2sJudgeSummary(
        out int tap,
        out int hld,
        out int sld,
        out int air,
        out int flk,
        out int all)
    {
        tap = C2sJudgeTap ?? -1;
        hld = C2sJudgeHld ?? -1;
        sld = C2sJudgeSld ?? -1;
        air = C2sJudgeAir ?? -1;
        flk = C2sJudgeFlk ?? -1;
        all = C2sJudgeAll ?? -1;

        if (tap < 0 || hld < 0 || sld < 0 || air < 0 || flk < 0 || all < 0)
            return false;

        return (long)tap + hld + sld + air + flk == all;
    }

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