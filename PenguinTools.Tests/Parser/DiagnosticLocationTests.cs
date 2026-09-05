using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;

namespace PenguinTools.Tests.Parser;

public class DiagnosticLocationTests
{
    [Fact]
    public void AddingPath_PreservesTimedDiagnosticAndItsCause()
    {
        var cause = new InvalidDataException("bad note");
        var target = new object();
        var original = new TimedLocationDiagnostic(Severity.Error, Msg.Key("test.message"), 26, 480)
        {
            Target = target,
            RelatedException = cause
        };

        var located = original.WithPathFallback("chart.mgxc");

        Assert.IsType<TimedLocationDiagnostic>(located);
        Assert.Equal(480, located.Time);
        Assert.Equal("chart.mgxc(0x1A)", located.FormattedLocation);
        Assert.Same(target, located.Target);
        Assert.Same(cause, located.RelatedException);
        Assert.Null(original.Path);
        Assert.Same(located, located.WithPathFallback("other.ugc"));
    }

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
