using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core.Metadata;
using Xunit;

namespace PenguinTools.Tests.Parser;

public sealed class WallMetadataTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("penguintools-wall-meta-");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Header = "@VER\t9\n@TICKS\t480\n@SONGID\t9978\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n";

    [Theory]
    [InlineData("", null, WallWidth.Wide)]
    [InlineData("normal", null, WallWidth.Normal)]
    [InlineData("wide 9", 9, WallWidth.Wide)]
    [InlineData("normal 0", 0, WallWidth.Normal)]
    [InlineData("9", 9, WallWidth.Wide)]
    public async Task OrderedComments_KeepAutoIdsAndReadWallOptions(string options, int? explicitId, WallWidth width)
    {
        var path = Path.Combine(_root.FullName, "input.ugc");
        await File.WriteAllTextAsync(path, Header +
            "@CMT\t#meta ignore\n@CMT\t#meta ignore false\n@CMT\t#meta wall \"images/墙 面.png\" " +
            options + "\n@CMT\t#meta main false\n", Ct);
        var parsed = await Parse(path);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var meta = parsed.Value.Meta;
        Assert.False(meta.IsMain);
        Assert.Equal(4, meta.Comment.Split('\n').Length);
        Assert.Equal("images/墙 面.png", meta.WallFilePath);
        Assert.Equal(width, meta.WallWidth);
        Assert.Equal(explicitId, meta.WallId);
        Assert.Equal(explicitId ?? 1009978, meta.EffectiveWallId);
        meta.Id = 99;
        Assert.Equal(explicitId ?? 1000099, meta.EffectiveWallId);
        meta.Id = int.MaxValue;
        Assert.Equal(explicitId, meta.EffectiveWallId);
    }

    [Fact]
    public async Task InvalidOptionalWall_DoesNotFailOrErasePreviousValidDirective()
    {
        var path = Path.Combine(_root.FullName, "invalid.ugc");
        await File.WriteAllTextAsync(path, Header +
            "@CMT\t#meta wall first.png normal\n@CMT\t#meta wall second.png sideways\n", Ct);
        var parsed = await Parse(path);
        Assert.True(parsed.Succeeded, parsed.ToString());
        Assert.Equal("first.png", parsed.Value.Meta.WallFilePath);
        Assert.Equal(WallWidth.Normal, parsed.Value.Meta.WallWidth);
        Assert.Contains(parsed.Diagnostics.Diagnostics, d => d.Severity == Core.Diagnostic.Severity.Warning);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Export_RebasesOnlyMediaPaths_WithoutChangingSource(bool mgxc, bool legacySeparators)
    {
        var input = Path.Combine(_root.FullName, "input.ugc");
        var comment = "#meta wall \"images/墙 面.png\" normal\n#meta bg \"images/bg.png\"\nPlain comment";
        if (legacySeparators)
        {
            comment = comment.Replace("\n#meta", "\\r\\n#meta", StringComparison.Ordinal);
        }
        await File.WriteAllTextAsync(input, Header + string.Join('\n', comment.Split('\n').Select(line => "@CMT\t" + line)) + "\n", Ct);
        var parsed = await Parse(input);
        Assert.True(parsed.Succeeded, parsed.ToString());
        var chart = parsed.Value;
        Directory.CreateDirectory(Path.Combine(_root.FullName, "elsewhere"));
        var output = Path.Combine(_root.FullName, "elsewhere", mgxc ? "output.mgxc" : "output.ugc");
        var written = mgxc
            ? await new MgxcChartWriter(new MgxcWriteRequest(output, chart)).WriteAsync(Ct)
            : await new UgcChartWriter(output, chart).WriteAsync(Ct);
        Assert.True(written.Succeeded, written.ToString());
        Assert.Equal(comment, chart.Meta.Comment);
        if (!mgxc)
        {
            var text = await File.ReadAllTextAsync(output, Ct);
            Assert.Equal(3, text.Split('\n').Count(line => line.StartsWith("@CMT\t", StringComparison.Ordinal)));
            text = string.Join('\n', text.Split('\n').Where(line => !line.StartsWith("@COPYRIGHT\t", StringComparison.Ordinal)));
            await File.WriteAllTextAsync(output, text, Ct);
        }
        var roundTrip = mgxc
            ? await new MgxcParser(new MgxcParseRequest(output, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct)
            : await Parse(output);
        Assert.True(roundTrip.Succeeded, roundTrip.ToString());
        Assert.Equal(Path.GetFullPath(Path.Combine(_root.FullName, "images", "墙 面.png")), roundTrip.Value.Meta.FullWallFilePath);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root.FullName, "images", "bg.png")), roundTrip.Value.Meta.FullBgiFilePath);
        Assert.Null(roundTrip.Value.Meta.WallId);
        Assert.Contains("Plain comment", roundTrip.Value.Meta.Comment);
    }

    private static Task<Core.OperationResult<Chart.Models.umgr.Chart>> Parse(string path) =>
        new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);

    public void Dispose() => _root.Delete(true);
}
