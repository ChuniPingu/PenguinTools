using PenguinTools.Chart.Models;

using C2sModel = PenguinTools.Chart.Models.c2s;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Converter.c2s;

public partial class C2SChartConverter
{
    private void ConvertEvent(UmgrModel.Chart mgxc)
    {
        Time lastTick = mgxc.GetLastTick();

        var events = mgxc.Events.Children;
        foreach (var e in events.OfType<UmgrModel.BpmEvent>().OrderBy(e => e.Tick))
        {
            Events.Add(new C2sModel.Bpm
            {
                Tick = e.Tick,
                Value = e.Bpm
            });
        }

        foreach (var e in events.OfType<UmgrModel.BeatEvent>().OrderBy(e => e.Tick))
        {
            Events.Add(new C2sModel.Met
            {
                Tick = e.Tick,
                Numerator = e.Numerator,
                Denominator = e.Denominator
            });
        }

        ConvertDcm([.. events.OfType<UmgrModel.NoteSpeedEvent>().OrderBy(e => e.Tick)], lastTick);
        ConvertSlp(mgxc, [.. events.OfType<UmgrModel.ScrollSpeedEvent>().OrderBy(e => e.Tick)]);
    }

    private void ConvertDcm(List<UmgrModel.NoteSpeedEvent> events, Time lastTick)
    {
        if (events.Count <= 0)
        {
            return;
        }

        for (var i = 0; i < events.Count - 1; i++)
        {
            var curr = events[i];
            if (curr.Speed == 1m)
            {
                continue;
            }

            var next = events[i + 1];
            var note = new C2sModel.Dcm
            {
                Tick = curr.Tick,
                Length = next.Tick.Round - curr.Tick.Round,
                Speed = curr.Speed
            };
            Events.Add(note);
        }

        var lastEvent = events[^1];
        if (lastEvent.Speed == 1m)
        {
            return;
        }

        var e = new C2sModel.Dcm
        {
            Tick = lastEvent.Tick,
            Length = Math.Max(lastTick.Round - lastEvent.Tick.Round, ChartResolution.SingleTick),
            Speed = lastEvent.Speed
        };
        Events.Add(e);
    }

    private void ConvertSlp(UmgrModel.Chart mgxc, List<UmgrModel.ScrollSpeedEvent> tilEvents)
    {
        if (tilEvents.Count <= 0)
        {
            return;
        }

        var tilGroups = tilEvents.GroupBy(til => til.Timeline).ToDictionary(g => g.Key, g => g.ToArray());
        var convertSlp = new List<C2sModel.Slp>();

        foreach (var (id, grouped) in tilGroups)
        {
            Time lastTilTick = mgxc.GetLastTick(p => p.Timeline == id);

            if (grouped.Length <= 0)
            {
                continue;
            }

            for (var i = 0; i < grouped.Length - 1; i++)
            {
                var curr = grouped[i];
                var next = grouped[i + 1];
                convertSlp.Add(new C2sModel.Slp
                {
                    Timeline = id,
                    Tick = curr.Tick,
                    Length = next.Tick.Round - curr.Tick.Round,
                    Speed = curr.Speed
                });
            }

            var lastEvent = grouped[^1];
            convertSlp.Add(new C2sModel.Slp
            {
                Timeline = id,
                Tick = lastEvent.Tick,
                Length = Math.Max(lastTilTick.Round - lastEvent.Tick.Round, ChartResolution.SingleTick),
                Speed = lastEvent.Speed
            });
        }

        convertSlp.RemoveAll(e => e.Speed == 1m);
        Events.AddRange((IEnumerable<C2sModel.Event>)convertSlp);
    }
}
