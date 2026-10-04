using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Models.umgr;

public class Chart
{
    public ChartExtras Extras { get; set; } = new();
    public Meta Meta { get; set; } = new();
    public Note Notes { get; set; } = new();
    public Event Events { get; set; } = new();

    public int GetLastTick(Func<Note, bool>? noteFilter = null)
    {
        var p = noteFilter != null ? Notes.Children.Where(noteFilter) : Notes.Children;
        return p.Select(note => note.GetLastTick()).Prepend(0).Max();
    }

    public TimeCalculator GetCalculator()
    {
        var beatEvents = Events.Children.OfType<BeatEvent>().OrderBy(e => e.Bar).ToList();
        return new TimeCalculator(ChartResolution.UmiguriTick, beatEvents);
    }

    public static void CalculateBeatEventTicks(IReadOnlyList<BeatEvent> beatEvents)
    {
        if (beatEvents.Count <= 0)
        {
            return;
        }

        var ticks = 0;
        beatEvents[0].Tick = ticks;
        for (var i = 0; i < beatEvents.Count - 1; i++)
        {
            var curr = beatEvents[i];
            var next = beatEvents[i + 1];
            ticks += ChartResolution.UmiguriTick * curr.Numerator / curr.Denominator * (next.Bar - curr.Bar);
            next.Tick = ticks;
        }
    }
}
