using System.Globalization;
using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser;
using PenguinTools.Core.Diagnostic;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Converter.c2s;

public partial class C2SChartConverter
{
    private readonly List<SoflanPoint> _soflanPoints = [];

    private int SoflanTickOffset => Mgxc.Meta.BgmEnableBarOffset && !Mgxc.Extras.UsesFixedCoordinateOrigin
        ? (int)Math.Round((decimal)ChartResolution.UmiguriTick * Mgxc.Meta.BgmInitialNumerator / Mgxc.Meta.BgmInitialDenominator)
        : 0;

    private void RegisterSoflanPoint(U.Note source)
    {
        if (source is U.SoflanArea or U.SoflanAreaJoint ||
            source is U.AirCrashJoint { Parent: U.AirCrash { Color: Color.NON } parent, Density.Original: 0 or int.MaxValue }
                && source.Tick != parent.Tick)
        {
            return;
        }

        AddSoflanPoint(source, Math.Clamp(source.Width, 1, 16));
    }

    private void AddSoflanPoint(U.Note source, int width)
    {
        var owner = source.Parent is U.Hold or U.Slide or U.AirHold or U.AirSlide or U.AirCrash
            ? source.Parent : source;
        var end = new Time(checked(owner.GetLastTick() + SoflanTickOffset)).Round;
        _soflanPoints.Add(new SoflanPoint(source, new U.Tap
        {
            Tick = new Time(checked(source.Tick.Original + SoflanTickOffset)).Round,
            Lane = source.Lane,
            Width = width,
            Timeline = source.Timeline
        }, end));
    }

    private void AssignSoflanTimelines()
    {
        foreach (var rung in AirCrashSoflanPoints.Create(Mgxc, SoflanTickOffset))
        {
            AddSoflanPoint(rung, rung.Width);
        }

        var points = _soflanPoints
            .OrderByDescending(p => p.EndTick)
            .DistinctBy(p => (p.Geometry.Tick, p.Geometry.Lane, p.Geometry.Width, p.Source.Timeline))
            .ToArray();
        var speeds = Events.OfType<C.SpeedEventBase>()
            .Where(e => e is C.Slp or C.Sfl or C.Stop)
            .GroupBy(e => e is C.Slp slp ? slp.Timeline : 0)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var allocation = new SoflanAllocation(points, speeds);
        var areas = allocation.Place();
        var geometry = points.Select(p => p.Geometry).ToArray();
        var optimized = SlaPlacementOptimizer.Optimize(geometry, areas);
        foreach (var area in optimized)
        {
            Notes.Add(new C.Sla
            {
                Tick = area.Tick,
                Timeline = area.Timeline,
                Lane = area.Lane,
                Width = area.Width,
                Length = area.Length
            });
        }

        allocation.CopySpeeds(Events);
        allocation.ReportLosses(Diagnostic);
        C2s.Extras.RebuiltSla = true;
        if (allocation.ChangedTimelines)
        {
            // The older COPYRIGHT snapshot must not replace the generated aliases.
            C2s.Extras.UnchangedEventKinds.Remove("TIL");
        }
    }

    private sealed record SoflanPoint(U.Note Source, U.Note Geometry, int EndTick);

    private sealed class SoflanAllocation
    {
        private readonly SoflanPoint[] _points;
        private readonly Dictionary<int, C.SpeedEventBase[]> _speeds;
        private readonly Dictionary<int, (int Tick, decimal Delta)[]> _profiles;
        private readonly Dictionary<int, int> _outputs;
        private readonly Dictionary<int, int> _sources;
        private readonly Dictionary<int, int> _aliases = [];
        private readonly Dictionary<SoflanPoint, (int Timeline, SoflanPoint? Owner)> _effective = [];
        private int _lastId;

        public SoflanAllocation(SoflanPoint[] points, Dictionary<int, C.SpeedEventBase[]> speeds)
        {
            _points = points;
            _speeds = speeds;
            _profiles = speeds.ToDictionary(g => g.Key, g => SpeedProfile(g.Value));
            var ids = speeds.Keys.Concat(points.Select(p => p.Source.Timeline)).Where(id => id > 0).Distinct().Order().ToArray();
            // Leave enough ids for a worst-case alias at every point, even for sparse Int32 ids.
            var compact = ids.Length > 0 && (long)ids[^1] + points.Length > int.MaxValue;
            _outputs = ids.Select((id, index) => (id, output: compact ? index + 1 : id))
                .ToDictionary(p => p.id, p => p.output);
            _outputs[0] = 0;
            _sources = _outputs.ToDictionary(p => p.Value, p => p.Key);
            _lastId = _sources.Keys.Max();
        }

        public bool ChangedTimelines => _aliases.Count > 0 || _outputs.Any(p => p.Key != p.Value);

        public List<SlaPlacement> Place()
        {
            var areas = new List<SlaPlacement>();
            foreach (var tick in _points.GroupBy(p => p.Geometry.Tick.Original).OrderBy(g => g.Key))
            {
                var placed = new List<(SlaPlacement Area, SoflanPoint Owner)>();
                foreach (var span in tick.GroupBy(p => (p.Geometry.Lane, p.Geometry.Width))
                             .OrderByDescending(g => g.Key.Width).ThenBy(g => g.Key.Lane))
                {
                    PlaceSpan(span.ToArray(), placed, areas);
                }
            }
            return areas;
        }

        private void PlaceSpan(SoflanPoint[] span, List<(SlaPlacement Area, SoflanPoint Owner)> placed, List<SlaPlacement> areas)
        {
            // A shared curve must remain valid until the longest-lived object ends.
            var owner = span.MaxBy(p => (p.EndTick, p.Source.Timeline))!;
            var geometry = owner.Geometry;
            var inherited = placed.Where(p => Contains(p.Area, geometry)).DefaultIfEmpty()
                .MaxBy(p => p.Area.Timeline);
            var timeline = inherited.Area.Timeline;
            if (geometry.Width <= 16)
            {
                var sourceId = owner.Source.Timeline;
                var outputId = _outputs[sourceId];
                if (outputId <= timeline && !Equivalent(sourceId, _sources[timeline]))
                {
                    outputId = Alias(sourceId, timeline);
                }
                if (outputId > timeline)
                {
                    timeline = outputId;
                    var area = new SlaPlacement(geometry.Tick.Original, timeline, geometry.Lane, geometry.Width, ChartResolution.SingleTick);
                    placed.Add((area, owner));
                    areas.Add(area);
                    inherited = (area, owner);
                }
            }

            foreach (var point in span)
            {
                _effective.Add(point, (timeline, inherited.Owner));
            }
        }

        private int Alias(int source, int above)
        {
            var existing = _aliases.Where(p => p.Value == source && p.Key > above).Select(p => p.Key).DefaultIfEmpty(0).Min();
            if (existing != 0)
            {
                return existing;
            }
            var id = checked(++_lastId);
            _aliases.Add(id, source);
            _sources.Add(id, source);
            return id;
        }

        private bool Equivalent(int left, int right, int endTick = int.MaxValue) =>
            left == right || (_profiles.GetValueOrDefault(left) ?? []).Where(p => p.Tick < endTick)
                .SequenceEqual((_profiles.GetValueOrDefault(right) ?? []).Where(p => p.Tick < endTick));

        // Compare the whole serialized scroll function, including additive SLP/SFL/STP,
        // implicit speed 1 and zero-length intervals. Equal instantaneous speeds are insufficient.
        private static (int Tick, decimal Delta)[] SpeedProfile(IEnumerable<C.SpeedEventBase> events)
        {
            var changes = new SortedDictionary<int, decimal>();
            foreach (var e in events)
            {
                var start = e.Tick.Round;
                var end = new Time(checked(e.Tick.Original + e.Length.Original)).Round;
                if (end <= start)
                {
                    continue;
                }
                var speed = e is C.Stop ? 0m : decimal.Parse(e.Speed.ToString("F6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                changes[start] = changes.GetValueOrDefault(start) + speed - 1m;
                changes[end] = changes.GetValueOrDefault(end) - speed + 1m;
            }
            return changes.Where(p => p.Value != 0).Select(p => (p.Key, p.Value)).ToArray();
        }

        public void CopySpeeds(List<C.Event> events)
        {
            foreach (var (source, output) in _outputs.Where(p => p.Key != p.Value))
            {
                if (_speeds.TryGetValue(source, out var originals))
                {
                    foreach (var slp in originals.OfType<C.Slp>())
                    {
                        slp.Timeline = output;
                    }
                }
            }
            foreach (var (id, source) in _aliases)
            {
                foreach (var e in _speeds.GetValueOrDefault(source) ?? [])
                {
                    events.Add(new C.Slp
                    {
                        Timeline = id,
                        Tick = e.Tick,
                        Length = e.Length,
                        Speed = e is C.Stop ? 0m : e.Speed
                    });
                }
            }
        }

        public void ReportLosses(DiagnosticCollector diagnostics)
        {
            foreach (var (point, (timeline, owner)) in _effective)
            {
                var actual = _sources[timeline];
                if (Equivalent(point.Source.Timeline, actual, point.EndTick))
                {
                    continue;
                }
                if (owner is null)
                {
                    diagnostics.Report(new TimedDiagnostic(Severity.Warning,
                        Msg.Create(MsgKeys.Warn_Editor_approximation, "C2S", "ALD timeline",
                            point.Source.Timeline, actual), point.Source.Tick.Original)
                    {
                        Target = NoteDiagnosticTarget.From(point.Source)
                    });
                }
                else
                {
                    diagnostics.Report(new TimedDiagnostic(Severity.Warning,
                        Msg.Create(MsgKeys.Mg_Note_overlapped_in_different_TIL, point.Source.Timeline, actual),
                        point.Source.Tick.Original)
                    {
                        Target = NotePairDiagnosticTarget.From(point.Source, owner.Source, diagnostics.TimeCalculator)
                    });
                }
            }
        }

        private static bool Contains(SlaPlacement area, U.Note point) =>
            point.Lane >= area.Lane && point.Lane + point.Width <= area.Lane + area.Width;
    }
}
