using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class EditorCompatibility
{
    internal const string UgcMarker = "'PenguinTools dialect: ";

    internal static ChartDialect OutputDialect(ChartDialect requested, ChartExtras extras) =>
        requested != ChartDialect.Auto ? requested :
        extras.EncodingDialect != ChartDialect.Auto ? extras.EncodingDialect : ChartDialect.Extended;

    internal static void Validate(U.Chart chart, ChartDialect dialect, bool ugc)
    {
        if (!Enum.IsDefined(dialect))
        {
            throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header, dialect, "Original or Extended editor format"));
        }
        if (dialect == ChartDialect.Extended &&
            chart.Extras.EncodingDialect == ChartDialect.Original &&
            chart.Meta.BgmEnableBarOffset && !chart.Extras.UsesFixedCoordinateOrigin &&
            chart.Meta.BgmInitialDenominator > 0 &&
            Math.Round((decimal)ChartResolution.UmiguriTick / chart.Meta.BgmInitialDenominator *
                chart.Meta.BgmInitialNumerator) != 0)
        {
            // Selecting an output encoding cannot silently remove the original
            // editor's preroll by marking unchanged source coordinates as fixed.
            throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header,
                "Original opening-measure coordinates for Extended output",
                "Original output or fixed-origin source"));
        }
        ValidateMeterPositions(chart, ugc);
        if (dialect != ChartDialect.Original)
        {
            return;
        }

        void Require(bool condition, string feature, object target)
        {
            if (!condition)
            {
                throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header, feature,
                    "a feature supported by Original format"), target);
            }
        }

        foreach (var beat in chart.Events.Children.OfType<U.BeatEvent>())
        {
            Require(beat.Numerator > 0 && beat.Denominator > 0, "zero-length measure", beat);
        }
        foreach (var note in chart.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children)))
        {
            Require(note.Lane >= 0 && note.Width > 0 && note.Lane + (long)note.Width <= 16,
                "out-of-bounds coordinate", note);
            Require(note is not U.Slide { NoLine: true } and not U.SlideJoint { NoLine: true }, "NCL", note);
            Require(!ugc || note is not U.AirCrash { Attr: AirLadderAttr.AxisY or AirLadderAttr.AxisZ }, "UGC AIR-CRUSH axis", note);
            if (note is U.Air air)
            {
                Require(air.Color is Color.DEF or Color.PNK, "AIR color", note);
            }
            if (note is U.AirHold hold)
            {
                Require(hold.Color is Color.DEF or Color.PNK, "AIR-HOLD color", note);
            }
            if (note is U.AirSlide slide)
            {
                Require(slide.Color is Color.DEF or Color.PNK, "AIR-SLIDE color", note);
            }
            if (ugc)
            {
                if (note is U.AirCrash crash)
                {
                    // The original UGC importer drops coincident AIR-CRUSH path points.
                    var previousTick = crash.Tick.Original;
                    foreach (var child in crash.Children)
                    {
                        Require(child.Tick.Original > previousTick, "UGC coincident AIR-CRUSH path points", child);
                        previousTick = child.Tick.Original;
                    }
                }
                var height = note switch
                {
                    U.AirSlide a => a.Height,
                    U.AirSlideJoint a => a.Height,
                    U.AirCrash a => a.Height,
                    U.AirCrashJoint a => a.Height,
                    _ => 0m
                };
                Require(height is >= 0 and <= 1295 && height == decimal.Truncate(height), "UGC height", note);
            }
        }
    }

    private static void ValidateMeterPositions(U.Chart chart, bool ugc)
    {
        // Source C2S meters carry absolute ticks. Native editors encode musical
        // bar ordinals; a snapshot must not hide an unrepresentable edit position.
        if (chart.Extras.Meters.Count == 0 || chart.Extras.MeterEditKey != ChartExtras.BeatKey(chart))
        {
            return;
        }
        var meters = chart.Extras.Meters.Where(m => m.Numerator >= 0 && m.Denominator > 0)
            .OrderBy(m => m.Tick).ToArray();
        var beats = chart.Events.Children.OfType<U.BeatEvent>().OrderBy(b => b.Bar).ToArray();
        if (meters.Length != beats.Length)
        {
            throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header,
                "time-signature positions", ugc ? "UGC musical-bar coordinates" : "MGXC musical-bar coordinates"));
        }
        long tick = 0;
        var bar = 0;
        var numerator = 4;
        var denominator = 4;
        for (var i = 0; i < beats.Length; i++)
        {
            tick += 1920L * numerator / denominator * (beats[i].Bar - bar);
            if (tick != meters[i].Tick)
            {
                throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header,
                    $"time signature at native tick {meters[i].Tick}",
                    ugc ? "UGC musical-bar coordinates" : "MGXC musical-bar coordinates"), beats[i]);
            }
            bar = beats[i].Bar;
            numerator = beats[i].Numerator;
            denominator = beats[i].Denominator;
        }
    }
}
