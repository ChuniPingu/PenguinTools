using PenguinTools.Chart.Parser;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class ChartMetaCommandsTests
{
    [Theory]
    [InlineData("#meta ignore", true)]
    [InlineData("#meta ignore true", true)]
    [InlineData("#meta ignore 1", true)]
    [InlineData("#meta ignore yes", true)]
    [InlineData("#meta ignore false", false)]
    [InlineData("#meta ignore 0", false)]
    [InlineData("#meta ignore no", false)]
    [InlineData("#meta main", false)]
    [InlineData("", false)]
    [InlineData("#meta ignore false\n#meta ignore", true)]
    [InlineData("#meta ignore\n#meta ignore false", false)]
    [InlineData("#meta main true\\n#meta ignore", true)]
    public void IsIgnored_ReadsLastIgnoreDirective(string comment, bool expected)
    {
        Assert.Equal(expected, ChartMetaCommands.IsIgnored(comment));
    }
}
