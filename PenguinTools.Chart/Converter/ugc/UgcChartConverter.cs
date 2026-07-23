using PenguinTools.Chart.Models;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Converter.ugc;

using c2s = Models.c2s;
using umgr = Models.umgr;

/// <summary>Converts the legacy C2S interchange model into the current editor model.</summary>
public sealed class UgcChartConverter
{
    private readonly c2s.Chart _source;
    private readonly umgr.Chart _target = new();
    private readonly Dictionary<c2s.Note, umgr.PositiveNote> _positiveNotes = [];
    private readonly List<OpenSlide> _openSlides = [];

    private readonly bool _debugTil;

    public UgcChartConverter(UgcConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.C2s);
        _source = request.C2s;
        _debugTil = request.DebugTil;
    }

    public OperationResult<umgr.Chart> Convert()
    {
        _target.Meta = _source.Meta;
        ConvertEvents();
        var notes = _source.Notes.Where(x => x is not c2s.Sla).ToArray();
        foreach (var note in notes.Where(x => x is not c2s.Air and not c2s.AirSlide and not c2s.AirHold))
            ConvertNote(note);
        foreach (var note in notes.OfType<c2s.AirSlide>().Where(x => x.Parent is not c2s.AirSlide))
            ConvertAirSlideChain(note, notes.OfType<c2s.AirSlide>().ToArray());
        foreach (var note in notes.OfType<c2s.AirHold>().Where(x => x.Parent is not c2s.AirHold))
            ConvertAirHoldChain(note, notes.OfType<c2s.AirHold>().ToArray());
        foreach (var note in notes.OfType<c2s.Air>()) ConvertNote(note);
        ApplySlaTimelines();
        if (_debugTil) EmitDebugTilMarkers();
        _target.Notes.Sort();
        return OperationResult<umgr.Chart>.Success(_target);
    }

    private void ConvertEvents()
    {
        foreach (var bpm in _source.Events.OfType<c2s.Bpm>())
            _target.Events.AppendChild(new umgr.BpmEvent { Tick = bpm.Tick, Bpm = bpm.Value });

        var meters = _source.Events.OfType<c2s.Met>().OrderBy(x => x.Tick).ToArray();
        var bar = 0;
        var previousTick = 0;
        var previousNumerator = 4;
        var previousDenominator = 4;
        foreach (var meter in meters)
        {
            // Game charts occasionally emit a trailing MET with a zero numerator/denominator
            // (e.g. music2918 Master). Skip them — zero-length bars break UGC bar formatting.
            if (meter.Numerator <= 0 || meter.Denominator <= 0) continue;

            var previousLength = ChartResolution.UmiguriTick * previousNumerator / previousDenominator;
            if (previousLength <= 0) previousLength = ChartResolution.UmiguriTick;
            bar += (meter.Tick.Original - previousTick) / previousLength;
            _target.Events.AppendChild(new umgr.BeatEvent
            {
                Tick = meter.Tick, Bar = bar,
                Numerator = meter.Numerator, Denominator = meter.Denominator
            });
            previousTick = meter.Tick.Original;
            previousNumerator = meter.Numerator;
            previousDenominator = meter.Denominator;
        }

        AddDurationEvents(_source.Events.OfType<c2s.Dcm>(),
            (tick, speed) => new umgr.NoteSpeedEvent { Tick = tick, Speed = speed });

#pragma warning disable CS0612
        var scrolls = _source.Events.OfType<c2s.SpeedEventBase>()
            .Where(x => x is c2s.Slp or c2s.Sfl)
            .GroupBy(x => x is c2s.Slp slp ? Math.Max(0, slp.Timeline) : 0);
#pragma warning restore CS0612
        foreach (var group in scrolls)
            AddDurationEvents(group, (tick, speed) => new umgr.ScrollSpeedEvent
                { Tick = tick, Speed = speed, Timeline = group.Key });
    }

    private void AddDurationEvents<T>(IEnumerable<T> source, Func<int, decimal, umgr.Event> factory)
        where T : c2s.SpeedEventBase
    {
        var events = source.OrderBy(x => x.Tick).ThenBy(x => x.Length).ToArray();
        foreach (var item in events)
        {
            _target.Events.AppendChild(factory(item.Tick.Original, item.Speed));
            var end = item.Tick.Original + item.Length.Original;
            var restored = events.Where(x => !ReferenceEquals(x, item) && x.Tick.Original <= end &&
                                              x.Tick.Original + x.Length.Original > end)
                .OrderByDescending(x => x.Tick).Select(x => x.Speed).FirstOrDefault(1m);
            _target.Events.AppendChild(factory(end, restored));
        }
    }

    private void ConvertNote(c2s.Note source)
    {
        switch (source)
        {
            case c2s.Tap x: AddPositive(x, new umgr.Tap()); break;
            case c2s.Damage x: AddPositive(x, new umgr.Damage()); break;
            case c2s.Flick x: AddPositive(x, new umgr.Flick()); break;
            case c2s.ExTap x: AddPositive(x, new umgr.ExTap { Effect = x.Effect ?? ExEffect.UP }); break;
            case c2s.Hold x: ConvertHold(x); break;
            case c2s.Slide x: ConvertSlide(x); break;
            case c2s.Air x: ConvertAir(x); break;
            case c2s.AirCrash x: ConvertAirCrash(x); break;
        }
    }

    private void AddPositive(c2s.Note source, umgr.PositiveNote target)
    {
        Copy(source, target);
        _target.Notes.AppendChild(target);
        _positiveNotes[source] = target;
    }

    private void ConvertHold(c2s.Hold source)
    {
        var hold = new umgr.Hold { Effect = source.Effect };
        Copy(source, hold);
        _target.Notes.AppendChild(hold);
        var tail = new umgr.HoldJoint { Tick = source.EndTick, Timeline = Timeline(source) };
        hold.AppendChild(tail);
        _positiveNotes[source] = tail;
    }

    private void ConvertSlide(c2s.Slide source)
    {
        var matchIndex = _openSlides.FindIndex(open =>
            open.LastJoint.Tick.Original == source.Tick.Original &&
            open.LastJoint.Lane == source.Lane &&
            open.LastJoint.Width == source.Width);

        if (matchIndex >= 0)
        {
            var open = _openSlides[matchIndex];
            open.LastJoint.Joint = IntermediateJoint(open.LastSegment);
            var joint = CreateSlideJoint(source);
            open.Slide.AppendChild(joint);
            _positiveNotes[source] = joint;
            _openSlides[matchIndex] = new OpenSlide(open.Slide, joint, source);
            return;
        }

        var slide = new umgr.Slide { Effect = source.Effect };
        Copy(source, slide);
        _target.Notes.AppendChild(slide);
        var firstJoint = CreateSlideJoint(source);
        slide.AppendChild(firstJoint);
        _positiveNotes[source] = firstJoint;
        _openSlides.Add(new OpenSlide(slide, firstJoint, source));
    }

    private static umgr.SlideJoint CreateSlideJoint(c2s.Slide source) => new()
    {
        Tick = source.EndTick, Lane = source.EndLane, Width = source.EndWidth,
        Timeline = Timeline(source), Joint = Joint.D
    };

    private static Joint IntermediateJoint(c2s.Slide segment) => segment.Joint;

    private void ConvertAir(c2s.Air source)
    {
        if (source.Parent is null || !_positiveNotes.TryGetValue(source.Parent, out var parent))
            return;

        var air = new umgr.Air { Direction = source.Direction, Color = source.Color };
        _target.Notes.AppendChild(air);
        parent.MakePair(air);
    }

    private void ConvertAirSlideChain(c2s.AirSlide source, IReadOnlyList<c2s.AirSlide> allSegments)
    {
        var air = new umgr.AirSlide { Height = source.Height.Original, Color = source.Color };
        Copy(source, air);
        var segment = source;
        while (true)
        {
            var next = allSegments.FirstOrDefault(x => ReferenceEquals(x.Parent, segment));
            air.AppendChild(new umgr.AirSlideJoint
            {
                Tick = segment.EndTick, Lane = segment.EndLane, Width = segment.EndWidth,
                Timeline = Timeline(segment), Height = segment.EndHeight.Original,
                Joint = segment.Joint
            });
            if (next is null) break;
            segment = next;
        }
        _target.Notes.AppendChild(air);
        if (source.Parent is not null && _positiveNotes.TryGetValue(source.Parent, out var parent)) parent.MakePair(air);
    }

    private void ConvertAirHoldChain(c2s.AirHold source, IReadOnlyList<c2s.AirHold> allSegments)
    {
        var air = new umgr.AirHold { Color = source.Color };
        Copy(source, air);
        var segment = source;
        while (true)
        {
            var next = allSegments.FirstOrDefault(x => ReferenceEquals(x.Parent, segment));
            air.AppendChild(new umgr.AirHoldJoint
            {
                Tick = segment.EndTick,
                Timeline = Timeline(segment),
                Joint = segment.Joint
            });
            if (next is null) break;
            segment = next;
        }
        _target.Notes.AppendChild(air);
        if (source.Parent is not null && _positiveNotes.TryGetValue(source.Parent, out var parent)) parent.MakePair(air);
    }

    private readonly record struct OpenSlide(umgr.Slide Slide, umgr.SlideJoint LastJoint, c2s.Slide LastSegment);

    private void ConvertAirCrash(c2s.AirCrash source)
    {
        var crash = new umgr.AirCrash
        {
            Height = source.Height.Original, Color = source.Color, Density = source.Density
        };
        Copy(source, crash);
        crash.AppendChild(new umgr.AirCrashJoint
        {
            Tick = source.EndTick, Lane = source.EndLane, Width = source.EndWidth,
            Timeline = Timeline(source), Height = source.EndHeight.Original
        });
        _target.Notes.AppendChild(crash);
    }

    private static void Copy(c2s.Note source, umgr.Note target)
    {
        target.Tick = source.Tick;
        target.Lane = source.Lane;
        target.Width = source.Width;
        target.Timeline = Timeline(source);
    }

    private static int Timeline(c2s.Note note) => Math.Max(0, note.Timeline);

    private void ApplySlaTimelines()
    {
        var regions = _source.Notes.OfType<c2s.Sla>().ToArray();
        foreach (var note in Flatten(_target.Notes.Children))
        {
            var timeline = regions.Where(x => Contains(x, note)).Select(x => x.Timeline).DefaultIfEmpty(note.Timeline).Max();
            note.Timeline = Math.Max(0, timeline);
        }
    }

    private static IEnumerable<umgr.Note> Flatten(IEnumerable<umgr.Note> notes)
    {
        foreach (var note in notes)
        {
            yield return note;
            foreach (var child in Flatten(note.Children)) yield return child;
        }
    }

    private static bool Contains(c2s.Sla sla, umgr.Note note)
    {
        var end = sla.Tick.Original + sla.Length.Original;
        return note.Tick.Original >= sla.Tick.Original && note.Tick.Original < end &&
               note.Lane >= sla.Lane && note.Lane + note.Width <= sla.Lane + sla.Width;
    }

    private void EmitDebugTilMarkers()
    {
        foreach (var sla in _source.Notes.OfType<c2s.Sla>())
        {
            var crash = new umgr.AirCrash
            {
                Tick = sla.Tick,
                Lane = sla.Lane,
                Width = sla.Width,
                Timeline = 0,
                Height = 0,
                Color = Color.NON,
                Density = 0
            };
            crash.AppendChild(new umgr.AirCrashJoint
            {
                Tick = sla.Tick.Original + sla.Length.Original,
                Lane = sla.Lane,
                Width = sla.Width,
                Timeline = 0,
                Height = 0
            });
            _target.Notes.AppendChild(crash);
        }
    }
}
