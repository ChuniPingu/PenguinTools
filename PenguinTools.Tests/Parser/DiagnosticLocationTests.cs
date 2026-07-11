using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class DiagnosticLocationTests
{
    [Fact]
    public void FormattedLocation_UsesLineNumbers_ForTextFiles()
    {
        var diagnostic = new LocationDiagnostic(Severity.Warning, Msg.Key("test.message"), 12, @"D:\charts\test.ugc");

        Assert.Equal(@"D:\charts\test.ugc(12)", diagnostic.FormattedLocation);
    }

    [Fact]
    public void FormattedLocation_UsesHexOffsets_ForMgxcFiles()
    {
        var diagnostic = new LocationDiagnostic(Severity.Warning, Msg.Key("test.message"), 26, @"D:\charts\test.mgxc");

        Assert.Equal(@"D:\charts\test.mgxc(0x1A)", diagnostic.FormattedLocation);
    }
}