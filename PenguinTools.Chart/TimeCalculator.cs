/*
   This code is modified from https://github.com/paralleltree/Ched
   Original Author: paralleltree
*/

using PenguinTools.Chart.Models.umgr;
using PenguinTools.Chart.Parser;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart;

public class TimeCalculator : ITickFormatter
{
    private readonly int _barTick;
    private readonly TimeSignature[] _timeSignatures;

    public TimeCalculator(int resolution, IEnumerable<BeatEvent> beatEvents)
    {
        _barTick = resolution;

        _timeSignatures = BuildTimeSignatures(beatEvents);
    }

    private TimeSignature[] BuildTimeSignatures(IEnumerable<BeatEvent> beatEvents)
    {
        var beatEventArray = beatEvents.Where(e => e.Bar >= 0).OrderBy(e => e.Bar).ToArray();
        var timeSignatures = new List<TimeSignature>(beatEventArray.Length + 1);
        var firstEventIndex = 0;
        if (beatEventArray.FirstOrDefault() is { Bar: 0 } firstBeatEvent)
        {
            timeSignatures.Add(new TimeSignature(0, 0, firstBeatEvent.Numerator, firstBeatEvent.Denominator));
            firstEventIndex = 1;
        }
        else
        {
            timeSignatures.Add(new TimeSignature(0, 0, UmiguriParserCommon.DefaultBeatNumerator,
                UmiguriParserCommon.DefaultBeatDenominator));
        }

        foreach (var beatEvent in beatEventArray[firstEventIndex..])
        {
            var prev = timeSignatures[^1];
            var tick = prev.Tick + GetMeasureLength(prev) * (beatEvent.Bar - prev.Bar);
            timeSignatures.Add(new TimeSignature(beatEvent.Bar, tick, beatEvent.Numerator, beatEvent.Denominator));
        }

        return [.. timeSignatures];
    }

    public TickPosition GetPosition(int tick)
    {
        var idx = FindTimeSignatureIndex(tick);
        var ts = _timeSignatures[idx];
        var measureLength = GetMeasureLength(ts);

        var delta = tick - ts.Tick;
        var barsSince = delta / measureLength;
        var remainder = delta % measureLength;

        var beatTick = (double)_barTick / ts.Denominator;
        var beatIndex = (int)(remainder / beatTick);
        var tickOffset = (int)(remainder % beatTick);

        return new TickPosition(ts.Bar + barsSince + 1, beatIndex + 1, tickOffset);
    }

    public string FormatTick(int tick)
    {
        return GetPosition(tick).ToString();
    }

    private int FindTimeSignatureIndex(int tick)
    {
        int low = 0, high = _timeSignatures.Length - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_timeSignatures[mid].Tick <= tick)
            {
                if (mid == _timeSignatures.Length - 1 || _timeSignatures[mid + 1].Tick > tick)
                    return mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        throw new InvalidOperationException($"Tick {tick} is before all time signatures.");
    }

    private int GetMeasureLength(TimeSignature ts)
    {
        return (int)(_barTick / (double)ts.Denominator * ts.Numerator);
    }

    private readonly record struct TimeSignature(int Bar, int Tick, int Numerator, int Denominator);
}
