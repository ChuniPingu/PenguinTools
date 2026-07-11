using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Converter.c2s;

using umgr = Models.umgr;
using c2s = Models.c2s;

public partial class C2SChartConverter
{
    public C2SChartConverter(C2SConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Mgxc);

        Mgxc = request.Mgxc;
    }

    private IDiagnosticSink Diagnostic { get; } = new DiagnosticCollector();
    private umgr.Chart Mgxc { get; }
    private c2s.Chart C2s { get; } = new();
    private List<c2s.Note> Notes => C2s.Notes;
    private List<c2s.Event> Events => C2s.Events;

    public OperationResult<c2s.Chart> Convert()
    {
        Diagnostic.TimeCalculator = Mgxc.GetCalculator();
        C2s.Meta = Mgxc.Meta;

        foreach (var note in Mgxc.Notes.Children) ConvertNote(note);
        ResolvePairings();
        ConvertEvent(Mgxc);

        ValidateOverlappingAirParents();
        ValidateLongNoteLengths();
        ApplyBgmBarOffset();

        return ValidatePairings()
            ? OperationResult<c2s.Chart>.Success(C2s).WithDiagnostics(Diagnostic)
            : OperationResult<c2s.Chart>.Failure().WithDiagnostics(Diagnostic);
    }

    private void ValidateOverlappingAirParents()
    {
        var allSlides = Notes.OfType<c2s.Slide>();
        var allAirs = Notes.OfType<c2s.IPairable>().Where(p => p.Parent is c2s.Slide).Cast<c2s.Note>();

        var airsLookup = allAirs.GroupBy(a => (a.Tick, a.Lane, a.Width)).ToDictionary(g => g.Key, g => g.Count());
        var slidesLookup = allSlides.GroupBy(s => (s.EndTick, s.EndLane, s.EndWidth))
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (pos, airsCount) in airsLookup)
        {
            var slidesCount = slidesLookup.GetValueOrDefault(pos, 0);
            if (airsCount >= slidesCount) continue;
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, Msg.Key(MsgKeys.Mg_Overlapping_air_parent_slide),
                pos.Tick.Original));
        }
    }

    private void ValidateLongNoteLengths()
    {
        foreach (var longNote in Notes.OfType<c2s.LongNote>())
        {
            var length = longNote.Length.Original;
            if (length >= ChartResolution.SingleTick) continue;

            var tick = longNote.Tick.Original;
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Length_smaller_than_unit, length,
                ChartResolution.UmiguriTick / ChartResolution.SingleTick);
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick)
            {
                Target = longNote
            });
        }
    }

    private void ApplyBgmBarOffset()
    {
        if (!Mgxc.Meta.BgmEnableBarOffset) return;

        var offset = (int)Math.Round((decimal)ChartResolution.UmiguriTick / Mgxc.Meta.BgmInitialDenominator *
                                     Mgxc.Meta.BgmInitialNumerator);
        foreach (var e in Events.Where(e => e.Tick.Original != 0)) e.Tick = e.Tick.Original + offset;
        foreach (var n in Notes)
        {
            n.Tick = n.Tick.Original + offset;
            if (n is c2s.LongNote longNote) longNote.EndTick = longNote.EndTick.Original + offset;
        }
    }

    private bool ValidatePairings()
    {
        var hasError = false;
        foreach (var air in Notes.OfType<c2s.Air>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(
                new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_parent_null), air.Tick.Original)
                {
                    Target = air
                });
            hasError = true;
        }

        foreach (var airSlide in Notes.OfType<c2s.AirSlide>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null),
                airSlide.Tick.Original)
            {
                Target = airSlide
            });
            hasError = true;
        }

        return !hasError;
    }
}