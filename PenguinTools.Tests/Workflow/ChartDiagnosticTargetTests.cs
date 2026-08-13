using PenguinTools.Core.Metadata;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Workflow;

public sealed class ChartDiagnosticTargetTests
{
    [Fact]
    public void FromMeta_CreatesLightweightTarget()
    {
        var meta = new Meta
        {
            Id = 1234,
            MgxcId = "1234",
            Title = "Test Song",
            Difficulty = Difficulty.Ultima,
            Designer = "Tester",
            FilePath = "ULTIMA.mgxc",
            IsMain = true
        };

        var target = ChartDiagnosticTarget.FromMeta(meta);

        Assert.Equal(1234, target.SongId);
        Assert.Equal("1234", target.MgxcId);
        Assert.Equal("Test Song", target.Title);
        Assert.Equal(Difficulty.Ultima, target.Difficulty);
        Assert.Equal("Tester", target.Designer);
        Assert.Equal("ULTIMA.mgxc", target.FilePath);
        Assert.True(target.IsMain);
    }
}