using System.Buffers.Binary;
using System.Text;
using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Chart.Writer.mgxc;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class AutomaticEditorTargetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(EditorTarget.Legacy, false)]
    [InlineData(EditorTarget.Standard, false)]
    [InlineData(EditorTarget.Extended, false)]
    [InlineData(EditorTarget.Legacy, true)]
    [InlineData(EditorTarget.Standard, true)]
    [InlineData(EditorTarget.Extended, true)]
    public async Task DefaultWriters_FollowInputAcrossThreeCycles(EditorTarget target, bool blank)
    {
        var mgxc = TestTempPaths.Create(".mgxc");
        var ugc = TestTempPaths.Create(".ugc");
        var c2s = TestTempPaths.Create(".c2s");
        try
        {
            var source = new U.Chart();
            source.Meta.BgmEnableBarOffset = blank;
            source.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
            source.Events.AppendChild(new U.BeatEvent { Numerator = 3, Denominator = 4 });
            source.Notes.AppendChild(new U.Tap { Tick = 1440, Width = 4 });
            Assert.True((await new MgxcChartWriter(new(mgxc, source, target)).WriteAsync(Ct)).Succeeded);
            var chart = await Read(mgxc);
            var expected = await C2s(c2s, chart);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Assert.Equal(target, chart.Extras.SourceEditorTarget);
                var written = await new UgcChartWriter(ugc, chart).WriteAsync(Ct);
                Assert.True(written.Succeeded, written.ToString());
                Assert.StartsWith(target == EditorTarget.Legacy ? "@VER\t8" : "@VER\t9",
                    await File.ReadAllTextAsync(ugc, Ct));
                chart = await Read(ugc);
                Assert.Equal(target, chart.Extras.SourceEditorTarget);
                written = await new MgxcChartWriter(new(mgxc, chart)).WriteAsync(Ct);
                Assert.True(written.Succeeded, written.ToString());
                var bytes = await File.ReadAllBytesAsync(mgxc, Ct);
                Assert.Equal(target == EditorTarget.Legacy ? 2 : 4,
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
                chart = await Read(mgxc);
                Assert.Equal(expected, await C2s(c2s, chart));
            }
        }
        finally { File.Delete(mgxc); File.Delete(ugc); File.Delete(c2s); }
    }

    [Theory]
    [InlineData(8, "", EditorTarget.Legacy)]
    [InlineData(9, "", EditorTarget.Standard)]
    [InlineData(8, "'MGR_ORIGIN=ZERO_V1\n'PenguinTools dialect: original\n", EditorTarget.Legacy)]
    [InlineData(9, "'MGR_ORIGIN=ZERO_V1\n", EditorTarget.Standard)]
    [InlineData(9, "' Created with Margrete v2.4.0\n", EditorTarget.Extended)]
    [InlineData(8, "' Created with Margrete v2.3.3\n", EditorTarget.Extended)]
    [InlineData(9, "'PenguinTools editor: extended\n", EditorTarget.Extended)]
    [InlineData(9, "'PenguinTools editor: extended\n' Created with Margrete v1.9.3\n", EditorTarget.Standard)]
    public async Task SourceEvidence_IsIndependentOfCoordinateOrigin(int version, string comments, EditorTarget expected)
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, comments + $"@VER\t{version}\n@TICKS\t480\n" +
                "@FLAG\tSOFFSET\tFALSE\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n" +
                (version == 8 ? "#0'0:t04\n" : "#0'0:tG4\n"), Ct);
            Assert.Equal(expected, (await Read(path)).Extras.SourceEditorTarget);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("meter")]
    [InlineData("count")]
    [InlineData("ncl")]
    [InlineData("rotation")]
    public async Task UnmarkedExtensions_DoNotSelectALossyLegacyTarget(string feature)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".mgxc");
        try
        {
            await File.WriteAllTextAsync(input, "@VER\t8\n@TICKS\t480\n@FLAG\tSOFFSET\tFALSE\n" +
                "@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n" + (feature switch
                {
                    "meter" => "@BEAT\t1\t0\t4\n@BEAT\t2\t4\t4\n#2'0:t04\n",
                    "count" => "@CLKCNT\t7\n#0'0:t04\n",
                    "rotation" => "' Created with Margrete v1.8.0\n#0'0:C0480Y,240\n#480>c048\n",
                    _ => "#0'0:s04\n#480>n04\n"
                }), Ct);
            var chart = await Read(input);
            Assert.Equal(EditorTarget.Extended, chart.Extras.SourceEditorTarget);
            var result = await new MgxcChartWriter(new(output, chart)).WriteAsync(Ct);
            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(EditorTarget.Extended, (await Read(output)).Extras.SourceEditorTarget);
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Fact]
    public async Task UnmarkedMgxcWithFixedOrigin_KeepsSharedCapabilities()
    {
        var path = TestTempPaths.Create(".mgxc");
        var c2s = TestTempPaths.Create(".c2s");
        try
        {
            var source = new U.Chart();
            source.Extras.FixedZeroOrigin = true;
            source.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
            source.Notes.AppendChild(new U.Tap { Tick = 480, Width = 4 });
            Assert.True((await new MgxcChartWriter(new(path, source, EditorTarget.Standard)).WriteAsync(Ct)).Succeeded);
            var expected = await C2s(c2s, await Read(path));
            var bytes = await File.ReadAllBytesAsync(path, Ct);
            var marker = bytes.AsSpan().IndexOf("xh32"u8);
            Assert.True(marker >= 20);
            byte[] historical = [.. bytes.AsSpan(0, marker), .. bytes.AsSpan(marker + 8)];
            foreach (var offset in new[] { 4, 16 })
            {
                BinaryPrimitives.WriteInt32LittleEndian(historical.AsSpan(offset),
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)) - 8);
            }
            await File.WriteAllBytesAsync(path, historical, Ct);
            var chart = await Read(path);
            Assert.True(chart.Extras.FixedZeroOrigin);
            Assert.Equal(EditorTarget.Standard, chart.Extras.SourceEditorTarget);
            Assert.True((await new MgxcChartWriter(new(path, chart)).WriteAsync(Ct)).Succeeded);
            Assert.Equal(expected, await C2s(c2s, await Read(path)));
        }
        finally { File.Delete(path); File.Delete(c2s); }
    }

    [Fact]
    public async Task AddingAnExtensionAfterParsing_PromotesAutomaticOutput()
    {
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            var source = new U.Chart();
            source.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
            source.Events.AppendChild(new U.BeatEvent { Numerator = 4, Denominator = 4 });
            source.Notes.AppendChild(new U.Tap { Width = 4 });
            Assert.True((await new MgxcChartWriter(new(path, source, EditorTarget.Legacy)).WriteAsync(Ct)).Succeeded);
            var chart = await Read(path);
            Assert.Equal(EditorTarget.Legacy, chart.Extras.SourceEditorTarget);
            chart.Extras.ClickCount = 7;
            Assert.True((await new MgxcChartWriter(new(path, chart)).WriteAsync(Ct)).Succeeded);
            var restored = await Read(path);
            Assert.Equal(EditorTarget.Extended, restored.Extras.SourceEditorTarget);
            Assert.Equal(7, restored.Extras.ClickCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UgcOnlyLimits_PromoteAutomaticOutputFromOriginalMgxc()
    {
        var mgxc = TestTempPaths.Create(".mgxc");
        var ugc = TestTempPaths.Create(".ugc");
        try
        {
            var source = new U.Chart();
            source.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
            source.Events.AppendChild(new U.BeatEvent { Numerator = 4, Denominator = 4 });
            var tap = new U.Tap { Tick = 480, Lane = 2, Width = 4 };
            // MGXC stores the arrow on the AIR base; UGC v9 AIR-HOLD has none.
            var hold = new U.AirHold { Direction = AirDirection.DW };
            hold.AppendChild(new U.AirHoldJoint { Tick = 960 });
            tap.MakePair(hold);
            source.Notes.AppendChild(tap);
            source.Notes.AppendChild(hold);
            Assert.True((await new MgxcChartWriter(new(mgxc, source, EditorTarget.Standard)).WriteAsync(Ct)).Succeeded);
            var chart = await Read(mgxc);
            Assert.Equal(EditorTarget.Standard, chart.Extras.SourceEditorTarget);
            Assert.Equal(AirDirection.DW, Assert.Single(chart.Notes.Children.OfType<U.AirHold>()).Direction);

            var standard = await new UgcChartWriter(ugc, chart, EditorTarget.Standard).WriteAsync(Ct);
            Assert.False(standard.Succeeded);
            Assert.Contains(standard.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Editor_target_feature);

            var automatic = await new UgcChartWriter(ugc, chart).WriteAsync(Ct);
            Assert.True(automatic.Succeeded, automatic.ToString());
            Assert.StartsWith("@VER\t9", await File.ReadAllTextAsync(ugc, Ct));
            var restored = await Read(ugc);
            Assert.Equal(EditorTarget.Extended, restored.Extras.SourceEditorTarget);
            Assert.Equal(AirDirection.DW, Assert.Single(restored.Notes.Children.OfType<U.AirHold>()).Direction);
        }
        finally { File.Delete(mgxc); File.Delete(ugc); }
    }

    [Fact]
    public async Task ConflictingCreators_DoNotGuessAnOutputFamily()
    {
        var path = TestTempPaths.Create(".ugc");
        try
        {
            await File.WriteAllTextAsync(path, "@VER\t9\n' Created with Margrete v1.9.3\n" +
                "' Created with Margrete v2.4.0\n@BPM\t0'0\t120\n#0'0:tG4\n", Ct);
            var result = await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Error_Invalid_Header);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(EditorTarget.Legacy)]
    [InlineData(EditorTarget.Standard)]
    public async Task UnknownMetadataAndTrailingBytes_KeepNativeInputReadable(EditorTarget target)
    {
        var path = TestTempPaths.Create(".mgxc");
        try
        {
            var chart = new U.Chart();
            chart.Meta.Title = "Native metadata";
            chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
            chart.Notes.AppendChild(new U.Tap { Width = 4 });
            Assert.True((await new MgxcChartWriter(new(path, chart, target)).WriteAsync(Ct)).Succeeded);
            var original = await File.ReadAllBytesAsync(path, Ct);
            var unknown = new byte[308];
            "XTRA"u8.CopyTo(unknown);
            BinaryPrimitives.WriteUInt16LittleEndian(unknown.AsSpan(4), 4);
            BinaryPrimitives.WriteUInt16LittleEndian(unknown.AsSpan(6), 300);
            Encoding.UTF8.GetBytes(new string('a', 300)).CopyTo(unknown, 8);
            byte[] extended = [.. original.AsSpan(0, 20), .. unknown, .. original.AsSpan(20), 1, 2, 3, 4];
            BinaryPrimitives.WriteInt32LittleEndian(extended.AsSpan(4),
                BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4)) + unknown.Length);
            BinaryPrimitives.WriteInt32LittleEndian(extended.AsSpan(16),
                BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(16)) + unknown.Length);
            await File.WriteAllBytesAsync(path, extended, Ct);
            var result = await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal("Native metadata", result.Value!.Meta.Title);
            Assert.IsType<U.Tap>(Assert.Single(result.Value.Notes.Children));
            Assert.Equal(target, result.Value.Extras.SourceEditorTarget);
            Assert.Contains(result.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.Mg_Unrecognized_meta);
            Assert.DoesNotContain(result.Diagnostics.Diagnostics, d => d.Severity == Severity.Error);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("4", EditorTarget.Extended)] // lanes -12..-8: entirely outside
    [InlineData("i", EditorTarget.Extended)] // lanes 28..32: entirely outside
    [InlineData("E", EditorTarget.Standard)] // lanes -2..2: partially playable
    [InlineData("U", EditorTarget.Standard)] // lanes 14..18: partially playable
    public async Task ExtendedFieldNotes_MustTouchPlayableLanesForOriginalEditor(string lane, EditorTarget expected)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(".mgxc");
        try
        {
            await File.WriteAllTextAsync(input, "@VER\t9\n@TICKS\t480\n@FLAG\tSOFFSET\tFALSE\n" +
                "@FLAG\tEXFIELD\tTRUE\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n#0'0:tG4\n" +
                $"#0'480:t{lane}4\n", Ct);
            var chart = await Read(input);
            Assert.Equal(expected, chart.Extras.SourceEditorTarget);
            var standard = await new MgxcChartWriter(new(output, chart, EditorTarget.Standard)).WriteAsync(Ct);
            Assert.Equal(expected == EditorTarget.Standard, standard.Succeeded);
            if (!standard.Succeeded)
            {
                Assert.Contains(standard.Diagnostics.Diagnostics,
                    d => d.Message.Key == MsgKeys.MgCrit_Editor_target_feature);
            }
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyTargets_RejectAirCrushRotationInBothFormats(bool ugc)
    {
        var input = TestTempPaths.Create(".ugc");
        var output = TestTempPaths.Create(ugc ? ".ugc" : ".mgxc");
        try
        {
            await File.WriteAllTextAsync(input, "@VER\t9\n@TICKS\t480\n@FLAG\tSOFFSET\tFALSE\n" +
                "@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n#0'0:CG480Y,240\n#480>cG48\n", Ct);
            var chart = await Read(input);
            var legacy = ugc
                ? await new UgcChartWriter(output, chart, EditorTarget.Legacy).WriteAsync(Ct)
                : await new MgxcChartWriter(new(output, chart, EditorTarget.Legacy)).WriteAsync(Ct);
            Assert.False(legacy.Succeeded);
            Assert.Contains(legacy.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.MgCrit_Editor_target_feature);
            var standard = ugc
                ? await new UgcChartWriter(output, chart, EditorTarget.Standard).WriteAsync(Ct)
                : await new MgxcChartWriter(new(output, chart, EditorTarget.Standard)).WriteAsync(Ct);
            Assert.True(standard.Succeeded, standard.ToString());
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    private static async Task<U.Chart> Read(string path)
    {
        var result = Path.GetExtension(path) == ".mgxc"
            ? await new MgxcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct)
            : await new UgcParser(new(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(Ct);
        Assert.True(result.Succeeded, result.ToString());
        return result.Value!;
    }

    private static async Task<string> C2s(string path, U.Chart chart)
    {
        var result = new C2SChartConverter(new(chart)).Convert();
        Assert.True(result.Succeeded, result.ToString());
        Assert.True((await new C2SChartWriter(new(path, result.Value!)).WriteAsync(Ct)).Succeeded);
        return await File.ReadAllTextAsync(path, Ct);
    }
}
