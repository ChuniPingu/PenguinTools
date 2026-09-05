using PenguinTools.Core.Metadata;
using PenguinTools.Workflow;
using Xunit;
using UmgrChart = PenguinTools.Chart.Models.umgr.Chart;

namespace PenguinTools.Tests.Workflow;

public sealed class OptionBookTests
{
    [Fact]
    public void SelectingMainDifficultyAndReplacingMetadata_CannotLeaveStaleBookValues()
    {
        var master = new UmgrChart { Meta = new Meta { Difficulty = Difficulty.Master, Title = "Master" } };
        var ultima = new UmgrChart { Meta = new Meta { Difficulty = Difficulty.Ultima, Title = "Ultima" } };
        var book = new OptionBook(Difficulty.Master, new Dictionary<Difficulty, OptionDifficulty>
        {
            [Difficulty.Master] = new(master),
            [Difficulty.Ultima] = new(ultima)
        });

        var selected = book with { MainDifficulty = Difficulty.Ultima };
        ultima.Meta = ultima.Meta with { Id = 42, Title = "Updated", StageId = 123, IsCustomStage = true };

        Assert.Equal("Master", book.Title);
        Assert.Equal("Updated", selected.Title);
        Assert.Equal(123, selected.StageId);
        Assert.True(selected.IsCustomStage);
        Assert.Equal(42, selected.Difficulties[Difficulty.Ultima].SongId);
        Assert.Same(ultima.Meta, selected.BookMeta);
    }
}
