using PenguinTools.Chart;
using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.ugc;
using PenguinTools.Chart.Writer.ugc;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class UgcEditPreservationTests
{
    [Theory]
    [InlineData("@TITLE\tOriginal", "@TITLE\tEdited")]
    [InlineData("@ARTIST\tOriginal", "@ARTIST\tEdited")]
    [InlineData("@DESIGN\tOriginal", "@DESIGN\tEdited")]
    [InlineData("@CMT\tOriginal", "@CMT\tEdited")]
    public async Task PresentationEdit_RetainsNoteRestoration(string before, string after)
    {
        var chart = Chart();
        chart.Meta.Title = chart.Meta.Artist = chart.Meta.Designer = chart.Meta.Comment = "Original";
        chart.Notes.AppendChild(Crash(0));
        AddExHold(chart, 1920);
        var parsed = await Edit(chart, text => text.Replace(before, after, StringComparison.Ordinal));
        Assert.Contains("Edited", new[] { parsed.Meta.Title, parsed.Meta.Artist, parsed.Meta.Designer, parsed.Meta.Comment });
        Assert.True(parsed.Extras.BinarySnapshotValid);
        var crash = Assert.Single(parsed.Notes.Children.OfType<U.AirCrash>());
        Assert.Equal(1200.25m, Assert.IsType<U.AirCrashJoint>(crash.LastChild).Height);
        Assert.Equal(AirLadderAttr.Trace, crash.Attr);
        var c2s = new C2SChartConverter(new C2SConvertRequest(parsed)).Convert();
        Assert.True(c2s.Succeeded);
        Assert.Equal(ExEffect.DW, Assert.Single(c2s.Value!.Notes.OfType<C.ExTap>()).Effect);
        Assert.Equal(ExEffect.UP, Assert.Single(c2s.Value.Notes.OfType<C.Hold>()).Effect);
    }

    [Fact]
    public async Task ChangedPath_OnlyInvalidatesItsConnectedGroup()
    {
        var chart = Chart();
        chart.Notes.AppendChild(Crash(0));
        chart.Notes.AppendChild(Crash(1920));
        AddExHold(chart, 3840);
        var parsed = await Edit(chart, text => ReplaceFirst(text, "#480>cK4XC", "#600>cK4XC"));
        Assert.False(parsed.Extras.BinarySnapshotValid);
        var crashes = parsed.Notes.Children.OfType<U.AirCrash>().OrderBy(n => n.Tick.Original).ToArray();
        Assert.Equal(AirLadderAttr.DEF, crashes[0].Attr);
        Assert.Equal(600, crashes[0].LastChild!.Tick.Original);
        Assert.Equal(1200, ((U.AirCrashJoint)crashes[0].LastChild!).Height);
        Assert.Equal(AirLadderAttr.Trace, crashes[1].Attr);
        Assert.Equal(1200.25m, ((U.AirCrashJoint)crashes[1].LastChild!).Height);
        var converted = new C2SChartConverter(new C2SConvertRequest(parsed)).Convert();
        Assert.True(converted.Succeeded);
        Assert.Single(converted.Value!.Notes.OfType<C.ExTap>());
        Assert.Equal(ExEffect.UP, Assert.Single(converted.Value.Notes.OfType<C.Hold>()).Effect);

        var rewritten = await Edit(parsed, text => text);
        var rewrittenCrashes = rewritten.Notes.Children.OfType<U.AirCrash>().OrderBy(n => n.Tick.Original).ToArray();
        Assert.Equal(600, rewrittenCrashes[0].LastChild!.Tick.Original);
        Assert.Equal(AirLadderAttr.DEF, rewrittenCrashes[0].Attr);
        Assert.Equal(1200, ((U.AirCrashJoint)rewrittenCrashes[0].LastChild!).Height);
        Assert.Equal(AirLadderAttr.Trace, rewrittenCrashes[1].Attr);
        Assert.Equal(1200.25m, ((U.AirCrashJoint)rewrittenCrashes[1].LastChild!).Height);
    }

    [Fact]
    public async Task UnrelatedInsertion_PreservesProjectedSingleNote()
    {
        var chart = Chart();
        chart.Notes.AppendChild(new U.Tap { Tick = 0, Lane = 40, Width = 4 });
        chart.Notes.AppendChild(Crash(1920));
        var parsed = await Edit(chart, text => text + "\n#2'0:tG4\n");
        Assert.False(parsed.Extras.BinarySnapshotValid);
        var taps = parsed.Notes.Children.OfType<U.Tap>().OrderBy(n => n.Tick.Original).ToArray();
        Assert.Equal(2, taps.Length);
        Assert.Equal(40, taps[0].Lane);
        Assert.Equal(0, taps[1].Lane);
        Assert.Equal(AirLadderAttr.Trace, Assert.Single(parsed.Notes.Children.OfType<U.AirCrash>()).Attr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CarrierEditOrInsertion_InvalidatesCoveredLongEffect(bool insert)
    {
        var chart = Chart();
        var hold = new U.Hold { Tick = 0, Lane = 4, Width = 4 };
        hold.AppendChild(new U.HoldJoint { Tick = 960 });
        chart.Notes.AppendChild(new U.ExTap { Tick = 0, Lane = 2, Width = 8, Effect = ExEffect.DW, Role = U.ExTapRole.Explicit });
        chart.Notes.AppendChild(hold);
        chart.Notes.AppendChild(Crash(1920));
        var parsed = await Edit(chart, text => insert ? text + "\n#0'0:xI8U\n" : text.Replace("#0'0:xI8D", "#0'0:xI8U", StringComparison.Ordinal));
        Assert.Equal(ExEffect.UP, Assert.Single(parsed.Notes.Children.OfType<U.Hold>()).Effect);
        Assert.Equal(AirLadderAttr.Trace, Assert.Single(parsed.Notes.Children.OfType<U.AirCrash>()).Attr);
    }

    [Fact]
    public async Task GroundEdit_InvalidatesPairedAirProjection()
    {
        var chart = Chart();
        var slide = new U.Slide { Tick = 0, Lane = 0, Width = 2 };
        var tail = new U.SlideJoint { Tick = 480, Lane = 4, Width = 2 };
        slide.AppendChild(tail);
        var air = new U.AirSlide { Height = 1200.25m };
        tail.MakePair(air);
        air.AppendChild(new U.AirSlideJoint { Tick = 960, Lane = 6, Width = 2, Height = 1200.25m });
        chart.Notes.AppendChild(slide);
        chart.Notes.AppendChild(air);
        chart.Notes.AppendChild(Crash(1920));
        var parsed = await Edit(chart, text => text.Replace("#0'0:sG20", "#0'0:sH20", StringComparison.Ordinal));
        var returned = Assert.Single(parsed.Notes.Children.OfType<U.AirSlide>());
        Assert.Equal(1200, returned.Height);
        Assert.Equal(1200, Assert.IsType<U.AirSlideJoint>(returned.LastChild).Height);
        Assert.Same(Assert.Single(parsed.Notes.Children.OfType<U.Slide>()).LastChild, returned.PairNote);
        Assert.Equal(AirLadderAttr.Trace, Assert.Single(parsed.Notes.Children.OfType<U.AirCrash>()).Attr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeZeroMeter_SurvivesTimingAndNoteEdits(bool editTiming)
    {
        var chart = Chart();
        chart.Events.AppendChild(new U.BeatEvent { Bar = 1, Tick = 1920, Numerator = 0, Denominator = 4 });
        var parsed = await Edit(chart, text => editTiming
            ? text.Replace("@BEAT\t0\t4\t4", "@BEAT\t0\t3\t4", StringComparison.Ordinal)
            : text + "\n#2'0:tG4\n");
        Assert.False(parsed.Extras.BinarySnapshotValid);
        var beats = parsed.Events.Children.OfType<U.BeatEvent>().ToArray();
        Assert.Equal(2, beats.Length);
        Assert.Equal(editTiming ? 3 : 4, beats[0].Numerator);
        Assert.Equal(0, beats[1].Numerator);
        Assert.Equal(editTiming ? 1440 : 1920, beats[1].Tick.Original);
        if (!editTiming)
        {
            Assert.Equal(1920, Assert.Single(parsed.Notes.Children.OfType<U.Tap>()).Tick.Original);
        }
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("inside")]
    [InlineData("timeline")]
    public async Task SlaSnapshot_RequiresUnchangedAffectedNotes(string edit)
    {
        var chart = Chart();
        var hold = new U.Hold { Tick = 0, Lane = 4, Width = 4, Timeline = 1, Effect = ExEffect.UP };
        hold.AppendChild(new U.HoldJoint { Tick = 960, Timeline = 1 });
        chart.Notes.AppendChild(hold);
        chart.Meta.C2sSlaSnapshot = "0,1,0,16,1920";
        chart.Meta.C2sSlaEditKey = C2sRoundTripKeys.FormatSlaEditKey(chart);
        var parsed = await Edit(chart, text => edit switch
        {
            "outside" => text + "\n@USETIL\t0\n#2'0:tG4\n",
            "inside" => text + "\n@USETIL\t0\n#0'480:tG4\n",
            _ => text.Replace("@USETIL\t1", "@USETIL\t2", StringComparison.Ordinal)
        });
        var converted = new C2SChartConverter(new(parsed)).Convert();
        Assert.True(converted.Succeeded);
        if (edit == "outside")
        {
            var sla = Assert.Single(converted.Value!.Notes.OfType<C.Sla>());
            Assert.Equal(1920, sla.Length.Original);
            Assert.Equal(16, sla.Width);
        }
        else
        {
            Assert.Null(converted.Value!.Meta.C2sSlaSnapshot);
        }
    }

    [Theory]
    [InlineData("@FLAG\tHIPRECISION\tTRUE", "@FLAG\tHIPRECISION\tFALSE")]
    [InlineData("@BEAT\t0\t4\t4", "@BEAT\t0\t3\t4")]
    [InlineData("@FLAG\tSOFFSET\t0", "@FLAG\tSOFFSET\t1")]
    public async Task InterpretationEdit_InvalidatesSnapshots(string before, string after)
    {
        var chart = Chart();
        chart.Notes.AppendChild(Crash(1920));
        var parsed = await Edit(chart, text => text.Replace(before, after, StringComparison.Ordinal));
        Assert.False(parsed.Extras.BinarySnapshotValid);
        Assert.Equal(AirLadderAttr.DEF, Assert.Single(parsed.Notes.Children.OfType<U.AirCrash>()).Attr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownSnapshotVersion_DoesNotRestoreState(bool editTitle)
    {
        var chart = Chart();
        chart.Meta.Title = "Original";
        chart.Notes.AppendChild(Crash(0));
        var parsed = await Edit(chart, text =>
        {
            var lines = text.Split('\n');
            var index = Array.FindIndex(lines, l => l.StartsWith("@COPYRIGHT\t", StringComparison.Ordinal));
            var extras = ChartExtras.FromCopyright(lines[index][11..]);
            extras.UgcSnapshotVersion = int.MaxValue;
            lines[index] = "@COPYRIGHT\t" + extras.ToCopyright();
            var updated = string.Join('\n', lines);
            return editTitle ? updated.Replace("@TITLE\tOriginal", "@TITLE\tEdited", StringComparison.Ordinal) : updated;
        });
        Assert.False(parsed.Extras.BinarySnapshotValid);
        Assert.Equal(AirLadderAttr.DEF, Assert.Single(parsed.Notes.Children.OfType<U.AirCrash>()).Attr);
    }

    private static U.Chart Chart()
    {
        var chart = new U.Chart();
        chart.Meta.BgmEnableBarOffset = false;
        chart.Events.AppendChild(new U.BpmEvent { Tick = 0, Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 });
        return chart;
    }

    private static U.AirCrash Crash(int tick)
    {
        var crash = new U.AirCrash { Tick = tick, Lane = 0, Width = 4, Height = 80, Attr = AirLadderAttr.Trace };
        crash.AppendChild(new U.AirCrashJoint { Tick = tick + 480, Lane = 4, Width = 4, Height = 1200.25m });
        return crash;
    }

    private static void AddExHold(U.Chart chart, int tick)
    {
        var hold = new U.Hold { Tick = tick, Lane = 4, Width = 4, Effect = ExEffect.UP };
        hold.AppendChild(new U.HoldJoint { Tick = tick + 960 });
        chart.Notes.AppendChild(new U.ExTap { Tick = tick, Lane = 4, Width = 4, Effect = ExEffect.DW, Role = U.ExTapRole.Explicit });
        chart.Notes.AppendChild(hold);
    }

    private static string ReplaceFirst(string text, string before, string after)
    {
        var index = text.IndexOf(before, StringComparison.Ordinal);
        Assert.True(index >= 0);
        return text[..index] + after + text[(index + before.Length)..];
    }

    private static async Task<U.Chart> Edit(U.Chart chart, Func<string, string> edit)
    {
        var path = TestTempPaths.Create(".ugc");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Assert.True((await new UgcChartWriter(path, chart).WriteAsync(ct)).Succeeded);
            await File.WriteAllTextAsync(path, edit(await File.ReadAllTextAsync(path, ct)), ct);
            var parsed = await new UgcParser(new UgcParseRequest(path, TestAssets.Load()), TestMediaTool.Instance).ParseAsync(ct);
            Assert.True(parsed.Succeeded);
            return parsed.Value!;
        }
        finally { File.Delete(path); }
    }
}
