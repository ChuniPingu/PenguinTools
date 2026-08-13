using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class DiagnosticLocationTests
{
    [Fact]
    public void FormattedLocation_UsesLineNumbers_ForTextFiles()
    {
        var diagnostic = new LocationDiagnostic(Severity.Warning, Msg.Key("test.message"), 12, "test.ugc");

        Assert.Equal("test.ugc(12)", diagnostic.FormattedLocation);
    }

    [Fact]
    public void FormattedLocation_UsesHexOffsets_ForMgxcFiles()
    {
        var diagnostic = new LocationDiagnostic(Severity.Warning, Msg.Key("test.message"), 26, "test.mgxc");

        Assert.Equal("test.mgxc(0x1A)", diagnostic.FormattedLocation);
    }
}