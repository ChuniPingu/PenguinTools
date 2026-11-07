using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

/// <summary>Output capabilities, independent of automatic input interpretation.</summary>
public enum EditorTarget
{
    Extended,
    Standard,
    Legacy
}

internal static class EditorTargetRules
{
    internal static int Version(EditorTarget target, bool ugc) =>
        target == EditorTarget.Legacy ? (ugc ? 8 : 2) : (ugc ? 9 : 4);

    internal static void Prepare(U.Chart chart, EditorTarget target, bool ugc, DiagnosticCollector diagnostics)
    {
        if (!Enum.IsDefined(target))
        {
            throw new DiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header, target, "editor target"));
        }
        if (target == EditorTarget.Extended)
        {
            return;
        }

        void Require(bool condition, string feature, int tick = 0)
        {
            if (!condition)
            {
                throw new TimedDiagnosticException(Msg.Create(MsgKeys.MgCrit_Editor_target_feature,
                    target.ToString().ToLowerInvariant(), feature), tick);
            }
        }

        foreach (var beat in chart.Events.Children.OfType<U.BeatEvent>())
        {
            Require(beat.Numerator > 0, "0/x", beat.Tick.Original);
        }
        foreach (var slide in chart.Notes.Children.OfType<U.Slide>())
        {
            Require(!chart.Extras.SegmentSlideLines || !SlideSegmentMetadata.IsMixed(SlideSegmentMetadata.Bits(slide)),
                "segmented NCL", slide.Tick.Original);
        }
        if (!ugc)
        {
            Require(!chart.Extras.ClickEnabled || chart.Extras.Count(chart.Meta.BgmInitialNumerator) == chart.Meta.BgmInitialNumerator,
                "custom metronome count (use UGC)");
        }
        var hasCurves = chart.Notes.Children.SelectMany(n => n.Children).Any(CurveExpansion.IsControl);
        if (hasCurves)
        {
            var before = chart.Notes.Children.Sum(n => n.Children.Count(CurveExpansion.IsControl));
            CurveExpansion.Expand(chart, preserveLegacy: target == EditorTarget.Legacy && !ugc);
            if (before != chart.Notes.Children.Sum(n => n.Children.Count(CurveExpansion.IsControl)))
            {
                diagnostics.Report(new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_Editor_curves_sampled)));
            }
        }
        if (ugc)
        {
            foreach (var path in chart.Notes.Children.Where(n => n is U.Slide or U.AirSlide))
            {
                var previousTick = path.Tick.Original;
                foreach (var point in path.Children)
                {
                    // Original UGC readers discard coincident path nodes even
                    // when their lane, width or AIR height differs.
                    Require(point.Tick.Original > previousTick,
                        "non-increasing SLIDE/AIR-SLIDE node time (use MGXC)", point.Tick.Original);
                    previousTick = point.Tick.Original;
                }
            }
        }
        foreach (var note in chart.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children)))
        {
            Require(note.Tick.Original >= 0, "negative note time", note.Tick.Original);
            var pathPoint = ugc && target != EditorTarget.Legacy && NoteWidthRules.IsClippedControl(note);
            Require(note.Lane >= -16 && note.Width >= (pathPoint ? 0 : 1) && note.Width <= 16 &&
                note.Lane + (long)note.Width <= 32,
                "unrepresentable geometry", note.Tick.Original);
            Require(note is not U.Air { Color: not (Color.DEF or Color.PNK) }
                and not U.AirHold { Color: not (Color.DEF or Color.PNK) }
                and not U.AirSlide { Color: not (Color.DEF or Color.PNK) },
                "custom AIR color", note.Tick.Original);
            Require(!ugc || note is not U.AirHold { Direction: not AirDirection.IR }
                and not U.AirSlide { Direction: not AirDirection.IR },
                "directional long AIR head (use MGXC)", note.Tick.Original);
            Require(!ugc || note is not U.AirHoldJoint { Joint: Joint.C, NextSibling: not null },
                "intermediate AIR-HOLD control (use MGXC)", note.Tick.Original);
            Require(!ugc || note is not U.SlideJoint { Joint: Joint.C, NextSibling: null },
                "nonjudging SLIDE tail (use MGXC)", note.Tick.Original);
            if (target == EditorTarget.Legacy)
            {
                Require(note.Lane >= 0 && note.Width > 0 && note.Lane + (long)note.Width <= 16,
                    "out-of-field geometry", note.Tick.Original);
                Require(note is not U.Slide { NoLine: true } and not U.SlideJoint { NoLine: true },
                    "NCL", note.Tick.Original);
                Require(!ugc || note is not U.AirCrash { Attr: AirLadderAttr.AxisY or AirLadderAttr.AxisZ },
                    "UGC AIR-CRUSH rotation", note.Tick.Original);
            }
            Require(!ugc || note is not U.AirCrash { HasExplicitJudgements: true },
                "UGC explicit AIR-CRUSH judgements", note.Tick.Original);
        }
        if (ugc && target == EditorTarget.Standard && chart.Notes.Children
                .SelectMany(n => n.Children).Any(n => n.Width == 0 && NoteWidthRules.IsClippedControl(n)))
        {
            diagnostics.Report(new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Warn_Editor_clipped_control_save)));
        }
        if (target == EditorTarget.Legacy)
        {
            chart.Meta.ExtendedField = false;
        }
    }
}
