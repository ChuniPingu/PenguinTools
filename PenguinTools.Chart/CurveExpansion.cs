using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

/// <summary>Turns editor Bézier handles into the ordinary path points used by UGC and C2S.</summary>
internal static class CurveExpansion
{
    private readonly record struct Point(int Tick, double Left, double Right, float Height, int Parameter = 0);

    internal static bool IsControl(U.Note note) => note is U.SlideJoint { IsCurveControl: true }
        or U.AirSlideJoint { IsCurveControl: true };

    internal static void Expand(U.Chart chart, bool preserveLegacy = false)
    {
        Validate(chart);
        foreach (var root in chart.Notes.Children.Where(n => n is U.Slide or U.AirSlide))
        {
            ExpandPath(root, chart.Meta.ExtendedField, preserveLegacy);
        }
    }

    private static void ExpandPath(U.Note root, bool expanded, bool preserveLegacy)
    {
        var start = root;
        var handles = new List<U.Note>();
        foreach (var child in root.Children.ToArray())
        {
            if (IsControl(child))
            {
                handles.Add(child);
                continue;
            }
            if (handles.Count > 0)
            {
                var modes = handles.Select(Interpretation).ToArray();
                if (modes.All(m => m == U.CurveInterpretation.Legacy))
                {
                    if (preserveLegacy)
                    {
                        handles.Clear();
                        start = child;
                        continue;
                    }
                    throw new TimedDiagnosticException(Msg.Key(MsgKeys.MgCrit_Unsupported_legacy_curve), start.Tick.Original, start);
                }
                var segmentExpanded = modes.Any(m => m == U.CurveInterpretation.OfficialExtended) ||
                    (modes.All(m => m == U.CurveInterpretation.FormatDefault) && expanded);
                foreach (var point in Sample(start, handles, child, segmentExpanded))
                {
                    root.InsertBefore(CreateJoint(root, start, point), child);
                }
                foreach (var handle in handles)
                {
                    root.RemoveChild(handle);
                }
                handles.Clear();
            }
            start = child;
        }
    }

    private static U.Note CreateJoint(U.Note root, U.Note start, Point point)
    {
        U.Note generated = root is U.Slide
            ? new U.SlideJoint { Joint = Joint.C, NoLine = start is U.Slide { NoLine: true } or U.SlideJoint { NoLine: true } }
            : new U.AirSlideJoint { Joint = Joint.C, Height = (int)point.Height };
        generated.Tick = point.Tick;
        generated.Lane = Round(point.Left);
        generated.Width = Round(point.Right) - generated.Lane;
        generated.Timeline = start.Timeline;
        return generated;
    }

    internal static void Validate(U.Chart chart)
    {
        foreach (var root in chart.Notes.Children.Where(n => n is U.Slide or U.AirSlide))
        {
            ValidatePath(root);
        }
    }

    private static void ValidatePath(U.Note root)
    {
        var start = root;
        var pending = false;
        foreach (var child in root.Children)
        {
            if (IsControl(child))
            {
                if (child is U.PositiveNote { PairNote: not null })
                {
                    Invalid(child, "paired curve handle");
                }

                pending = true;
                continue;
            }
            if (pending && child.Tick.Original <= start.Tick.Original)
            {
                Invalid(child, "non-positive curve duration");
            }

            pending = false;
            start = child;
        }
        if (pending)
        {
            Invalid(root.LastChild!, "curve without an endpoint");
        }
    }

    private static void Invalid(U.Note point, string detail) =>
        throw new TimedDiagnosticException(Msg.Create(MsgKeys.Error_Invalid_Header, detail, "complete Bézier segment"), point.Tick.Original, point);

    private static float GetHeight(U.Note n) => n switch
    {
        U.AirSlide a => (float)a.Height,
        U.AirSlideJoint a => (float)a.Height,
        _ => 80
    };

    private static U.CurveInterpretation Interpretation(U.Note n) => n switch
    {
        U.SlideJoint s => s.CurveInterpretation,
        U.AirSlideJoint s => s.CurveInterpretation,
        _ => U.CurveInterpretation.FormatDefault
    };

    private static int? SamplingHeight(U.Note n) => n switch
    {
        U.AirSlide a => a.SamplingHeight,
        U.AirSlideJoint a => a.SamplingHeight,
        _ => null
    };

    private static int Round(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
    private static Point Endpoint(U.Note n) => new(n.Tick.Original, n.Lane, n.Lane + (double)n.Width, GetHeight(n));
    private static (int, int, int) Key(Point p) => (Round(p.Left), Round(p.Right), Round(p.Height / 5f));

    private static IEnumerable<Point> Sample(U.Note start, List<U.Note> handles, U.Note end, bool expanded)
    {
        var duration = (long)end.Tick.Original - start.Tick.Original;
        if (duration <= 0)
        {
            Invalid(end, "non-positive curve duration");
        }
        var nodes = new[] { start }.Concat(handles).Append(end).ToArray();
        var left = new (double X, double Y)[nodes.Length];
        var right = new (double X, double Y)[nodes.Length];
        var height = new (double X, double Y)[nodes.Length];
        var actualHeight = new (double X, double Y)[nodes.Length];
        var hasBasis = nodes.All(n => SamplingHeight(n).HasValue);
        for (var i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            var time = (n.Tick.Original - (double)start.Tick.Original) / duration;
            var width = (1 - time) * start.Width + time * end.Width;
            var center = n.Lane + n.Width * 0.5;
            left[i] = (center - width * 0.5, time);
            right[i] = (center + width * 0.5, time);
            actualHeight[i] = (GetHeight(n), time);
            height[i] = (hasBasis ? SamplingHeight(n)!.Value : GetHeight(n), time);
        }
        var points = SamplePoints(start, end, duration, expanded, left, right, height,
            hasBasis ? SamplingHeight(start)!.Value : GetHeight(start));
        var endpoint = Endpoint(end) with { Height = hasBasis ? SamplingHeight(end)!.Value : GetHeight(end) };
        Simplify(points, endpoint);
        return points.Skip(1).Select(p => hasBasis
            ? p with { Height = (float)(Evaluate(actualHeight, p.Parameter / 1024d).X / 16) * 16 }
            : p);
    }

    private static List<Point> SamplePoints(U.Note start, U.Note end, long duration, bool expanded,
        (double X, double Y)[] left, (double X, double Y)[] right, (double X, double Y)[] height, float startHeight)
    {
        var points = new List<Point> { Endpoint(start) with { Height = startHeight } };
        for (var i = 0; i < 1024; i++)
        {
            var t = i / 1024d;
            var l = Evaluate(left, t);
            var r = Evaluate(right, t);
            var x = Math.Clamp(l.X, expanded ? -16 : 0, expanded ? 32 : 16);
            var y = Math.Clamp(r.X, expanded ? -16 : 0, expanded ? 32 : 16);
            if (Math.Abs(x - Round(x)) > 0.05 || Math.Abs(y - Round(y)) > 0.05)
            {
                continue;
            }
            var tick = checked((int)(l.Y * duration + start.Tick.Original));
            if (tick <= points[^1].Tick || tick >= end.Tick.Original)
            {
                continue;
            }
            points.Add(new Point(tick, Round(x), Round(y), (float)(Evaluate(height, t).X / 16) * 16, i));
        }
        return points;
    }

    private static void Simplify(List<Point> points, Point endpoint)
    {
        for (var i = 1; i < points.Count;)
        {
            var next = i + 1 < points.Count ? points[i + 1] : endpoint;
            var nextKey = i + 1 < points.Count ? Key(next) : (Round(next.Left), Round(next.Right), (int)(next.Height / 5f));
            if (Key(points[i - 1]) == Key(points[i]) && Key(points[i]) == nextKey)
            {
                points.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
        for (var i = 1; i < points.Count;)
        {
            if (Key(points[i - 1]) == Key(points[i]) && points[i].Tick - points[i - 1].Tick < 240)
            {
                points.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    private static (double X, double Y) Evaluate((double X, double Y)[] nodes, double t)
    {
        var work = nodes.ToArray();
        for (var count = work.Length - 1; count > 0; count--)
        {
            for (var i = 0; i < count; i++)
            {
                work[i] = ((1 - t) * work[i].X + t * work[i + 1].X,
                    (1 - t) * work[i].Y + t * work[i + 1].Y);
            }
        }
        return work[0];
    }
}
