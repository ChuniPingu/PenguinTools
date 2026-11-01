using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sParentResolutionTests
{
    [Theory]
    [InlineData("AIR 0 96 4 4 TAP DEF\nTAP 0 96 4 4")]
    [InlineData("HLD 0 96 4 4 96\nAIR 0 96 4 4 HLD DEF")]
    [InlineData("TAP 0 96 4 4\nAIR 0 96 4 4 TAP DEF\nAUR 0 96 4 4 TAP DEF")]
    public async Task MissingAvailableParent_ReportsWarningAndLeavesAirUnbound(string body)
    {
        var parsed = await Parse(body);
        var warning = Assert.Single(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.NotNull(warning.Line);
        Assert.Single(parsed.Value!.Notes.OfType<C.Air>(), air => air.Parent is null);
    }

    [Fact]
    public async Task DuplicateGroundNotes_AreConsumedInStatementOrder()
    {
        var parsed = await Parse("""
            TAP 0 96 4 4
            TAP 0 96 4 4
            AIR 0 96 4 4 TAP DEF
            AUR 0 96 4 4 TAP DEF
            """);
        AssertResolved(parsed);
        var taps = parsed.Value!.Notes.OfType<C.Tap>().ToArray();
        var airs = parsed.Value.Notes.OfType<C.Air>().ToArray();
        Assert.Same(taps[0], airs[0].Parent);
        Assert.Same(taps[1], airs[1].Parent);
    }

    [Theory]
    [InlineData("AIR 0 96 4 4 TAP DEF")]
    [InlineData("AHD 0 96 4 4 TAP 96 DEF")]
    [InlineData("ASD 0 96 4 4 TAP 5.0 96 8 4 5.0 DEF")]
    public async Task AirKinds_ShareOneGroundParentSlot(string first)
    {
        var parsed = await Parse($"TAP 0 96 4 4\n{first}\nAUL 0 96 4 4 TAP DEF");
        Assert.Single(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
        var actions = parsed.Value!.Notes.OfType<C.IPairable>().ToArray();
        Assert.NotNull(actions[0].Parent);
        Assert.Null(actions[1].Parent);
    }

    [Theory]
    [InlineData("SLD")]
    [InlineData("SXD")]
    [InlineData("SLC")]
    [InlineData("SXC")]
    public async Task SlideParentTokens_SelectTheSameGroundKind(string token)
    {
        var parsed = await Parse($"SLC 0 0 0 4 96 4 4 SLD\nAIR 0 96 4 4 {token} DEF");
        AssertResolved(parsed);
        Assert.Same(Assert.Single(parsed.Value!.Notes.OfType<C.Slide>()), Assert.Single(parsed.Value.Notes.OfType<C.Air>()).Parent);
    }

    [Theory]
    [InlineData("HLD")]
    [InlineData("HXD")]
    public async Task HoldParentTokens_SelectTheSameGroundKind(string token)
    {
        var parsed = await Parse($"HLD 0 0 4 4 96\nAIR 0 96 4 4 {token} DEF");
        AssertResolved(parsed);
        Assert.Same(Assert.Single(parsed.Value!.Notes.OfType<C.Hold>()), Assert.Single(parsed.Value.Notes.OfType<C.Air>()).Parent);
    }

    [Fact]
    public async Task SlideContinuation_KeepsTheRootAirSlotOccupied()
    {
        var parsed = await Parse("""
            SLC 0 0 0 4 96 4 4 SLD
            AIR 0 96 4 4 SLD DEF
            SLD 0 96 4 4 96 8 4 SLD
            AIR 0 192 8 4 SLD DEF
            """);
        Assert.Single(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
        var slides = parsed.Value!.Notes.OfType<C.Slide>().ToArray();
        var airs = parsed.Value.Notes.OfType<C.Air>().ToArray();
        Assert.Same(slides[0], airs[0].Parent);
        Assert.Null(airs[1].Parent);
    }

    [Fact]
    public async Task SlideContinuation_RemovesItsPreviousAttachPoint()
    {
        var parsed = await Parse("""
            SLC 0 0 0 4 96 4 4 SLD
            SLD 0 96 4 4 96 8 4 SLD
            AIR 0 96 4 4 SLD DEF
            """);
        Assert.Single(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
        Assert.Null(Assert.Single(parsed.Value!.Notes.OfType<C.Air>()).Parent);
    }

    [Theory]
    [InlineData("AHD")]
    [InlineData("AHX")]
    public async Task AirHoldContinuation_UsesGroundRootOrderAndCurrentTail(string parentToken)
    {
        var parsed = await Parse($"""
            HLD 0 0 4 4 48
            HLD 0 0 4 4 96
            AHX 0 96 4 4 HLD 96 DEF
            AHD 0 48 4 4 HLD 144 DEF
            AHD 0 192 4 4 {parentToken} 96 DEF
            AHX 0 192 4 4 {parentToken} 192 DEF
            """);
        AssertResolved(parsed);
        var actions = parsed.Value!.Notes.OfType<C.AirHold>().ToArray();
        Assert.Same(actions[1], actions[2].Parent);
        Assert.Same(actions[0], actions[3].Parent);
    }

    [Theory]
    [InlineData("ASC", "5.0", true)]
    [InlineData("ASD", "5.0", false)]
    [InlineData("ASC", "6.0", false)]
    public async Task AirSlideContinuation_RequiresTheCurrentEndpointTypeAndHeight(string parentToken, string height, bool resolves)
    {
        var parsed = await Parse($"""
            TAP 0 0 0 4
            ASC 0 0 0 4 TAP 1.0 96 4 4 5.0 DEF
            ASD 0 96 4 4 {parentToken} {height} 96 8 4 5.0 DEF
            """);
        var slides = parsed.Value!.Notes.OfType<C.AirSlide>().ToArray();
        if (resolves)
        {
            AssertResolved(parsed);
            Assert.Same(slides[0], slides[1].Parent);
        }
        else
        {
            Assert.Single(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
            Assert.Null(slides[1].Parent);
        }
    }

    [Fact]
    public async Task LaterSlideContinuation_DoesNotChangeAnEarlierAirBindingDuringConversion()
    {
        var parsed = await Parse("""
            SLC 0 0 0 4 96 4 4 SLD
            SLD 0 48 2 4 144 4 4 SLD
            AIR 0 192 4 4 SLD DEF
            SLD 0 96 4 4 96 4 4 SLD
            """);
        AssertResolved(parsed);
        var expected = parsed.Value!.Notes.OfType<C.Slide>().Single(s => s.Lane == 2);
        Assert.Same(expected, Assert.Single(parsed.Value.Notes.OfType<C.Air>()).Parent);
        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var air = Assert.Single(converted.Value!.Notes.Children.OfType<U.Air>());
        Assert.Equal(2, Assert.IsType<U.Slide>(air.PairNote!.Parent).Lane);
    }

    [Fact]
    public async Task IntermediateSlideAirAction_IsNotRedirectedToAnIdenticalTerminalSlide()
    {
        var parsed = await Parse("""
            SLD 0 0 0 4 96 4 4 SLD
            SLD 0 0 0 4 96 4 4 SLD
            AHD 0 96 4 4 SLD 192 DEF
            SLD 0 96 4 4 96 8 4 SLD
            """);
        AssertResolved(parsed);
        var slides = parsed.Value!.Notes.OfType<C.Slide>().ToArray();
        var air = Assert.Single(parsed.Value.Notes.OfType<C.AirHold>());
        Assert.Same(slides[0], air.Parent);
        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var action = Assert.Single(converted.Value!.Notes.Children.OfType<U.AirHold>());
        var carrier = Assert.IsType<U.ExTap>(action.PairNote);
        Assert.Equal(U.ExTapRole.AirActionCarrier, carrier.Role);
        Assert.Equal(U.AirActionCarrierParent.Slide, carrier.AirActionParent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnboundAirChains_DoNotCreateCarriersOrReappearAfterWriting(bool slide)
    {
        var body = slide ? """
            ASC 0 0 0 4 TAP 1.0 96 4 4 5.0 DEF
            ASC 0 96 4 4 ASC 5.0 96 8 4 5.0 DEF
            ASD 0 192 8 4 ASC 5.0 96 12 4 5.0 DEF
            """ : """
            AHD 0 0 0 4 TAP 96 DEF
            AHX 0 96 0 4 AHD 96 DEF
            AHD 0 192 0 4 AHX 96 DEF
            """;
        var parsed = await Parse("BPM 0 0 120\nTAP 1 0 12 4\n" + body);
        Assert.Equal(3, parsed.Diagnostics.Diagnostics.Count(d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved));
        Assert.All(parsed.Value!.Notes.OfType<C.IPairable>(), n => Assert.Null(n.Parent));
        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.IsType<U.Tap>(Assert.Single(converted.Value!.Notes.Children));
        var roundTripped = await WriteAndRead(converted.Value);
        Assert.DoesNotContain(roundTripped.Notes, n => n is C.IPairable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedAirContinuationAndDescendants_LeaveTheValidPrefixIntact(bool slide)
    {
        var body = slide ? """
            ASC 0 0 0 4 TAP 1.0 96 4 4 5.0 DEF
            ASC 0 96 4 4 ASC 6.0 96 8 4 5.0 DEF
            ASD 0 192 8 4 ASC 5.0 96 12 4 5.0 DEF
            """ : """
            AHD 0 0 0 4 TAP 96 DEF
            AHD 0 96 0 4 HLD 96 DEF
            AHD 0 192 0 4 AHD 96 DEF
            """;
        var parsed = await Parse("BPM 0 0 120\nTAP 0 0 0 4\n" + body);
        Assert.Equal(2, parsed.Diagnostics.Diagnostics.Count(d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved));
        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.DoesNotContain(converted.Value!.Notes.Children, n => n is U.ExTap);
        var action = Assert.Single(converted.Value.Notes.Children.OfType<U.NegativeNote>());
        Assert.Single(action.Children);
        Assert.Equal(480, action.Children[0].Tick.Original);
        var roundTripped = await WriteAndRead(converted.Value);
        Assert.Single(roundTripped.Notes.OfType<C.IPairable>());
    }

    [Fact]
    public async Task ReusedParentActions_AreWarnedAndDoNotOverrideTheAcceptedAction()
    {
        var parsed = await Parse("""
            TAP 0 0 4 4
            AHD 0 0 4 4 TAP 96
            ASD 0 0 4 4 TAP 5.0 96 6 4 5.0 DEF
            AUL 0 0 4 4 TAP
            ADR 0 0 4 4 TAP
            """);
        Assert.Equal(3, parsed.Diagnostics.Diagnostics.Count(d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved));
        var converted = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        Assert.Equal(2, converted.Value!.Notes.Children.Count);
        var hold = Assert.Single(converted.Value.Notes.Children.OfType<U.AirHold>());
        Assert.Same(Assert.Single(converted.Value.Notes.Children.OfType<U.Tap>()), hold.PairNote);
        Assert.Equal(AirDirection.IR, hold.Direction);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(96, true)]
    public async Task SlaParent_UsesTheAreaEnd(int tick, bool resolves)
    {
        var parsed = await Parse($"SLA 0 0 4 4 96 1\nAIR 0 {tick} 4 4 SLA DEF");
        var air = Assert.Single(parsed.Value!.Notes.OfType<C.Air>());
        if (resolves)
        {
            AssertResolved(parsed);
            Assert.Same(Assert.Single(parsed.Value.Notes.OfType<C.Sla>()), air.Parent);
        }
        else
        {
            Assert.Single(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
            Assert.Null(air.Parent);
        }
    }

    [Fact]
    public async Task AirCrashAxisChange_DoesNotCreateAnotherAirParentSlot()
    {
        var parsed = await Parse("""
            ALD 0 0 0 4 24 1.0 96 4 4 2.0 DEF DEF
            AIR 0 96 4 4 ALD DEF
            ALD 0 96 4 4 24 2.0 96 8 4 3.0 DEF AxisY
            AIR 0 192 8 4 ALD DEF
            """);
        Assert.Single(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);
        var airs = parsed.Value!.Notes.OfType<C.Air>().ToArray();
        Assert.Same(parsed.Value.Notes.OfType<C.AirCrash>().First(), airs[0].Parent);
        Assert.Null(airs[1].Parent);
    }

    private static void AssertResolved(OperationResult<C.Chart> parsed) =>
        Assert.DoesNotContain(parsed.Diagnostics.Diagnostics, d => d.Message.Key == MsgKeys.C2s_Parent_not_resolved);

    private static async Task<C.Chart> WriteAndRead(U.Chart chart)
    {
        var converted = new C2SChartConverter(new C2SConvertRequest(chart)).Convert();
        Assert.True(converted.Succeeded, converted.ToString());
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, converted.Value!)).WriteAsync(ct);
            Assert.True(written.Succeeded, written.ToString());
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            AssertResolved(parsed);
            return parsed.Value!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<OperationResult<C.Chart>> Parse(string body)
    {
        var path = TestTempPaths.Create(".c2s");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "VERSION 1.15.00 1.15.00\nRESOLUTION 384\n" + body, ct);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(ct);
            Assert.True(parsed.Succeeded, parsed.ToString());
            return parsed;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
