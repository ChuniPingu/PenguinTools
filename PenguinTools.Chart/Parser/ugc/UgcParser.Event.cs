using System.Globalization;
using PenguinTools.Chart.Models;

namespace PenguinTools.Chart.Parser.ugc;

using umgr = Models.umgr;

public partial class UgcParser
{
    private const int DefaultBeatNumerator = 4;
    private const int DefaultBeatDenominator = 4;
    private readonly List<(int Bar, int Tick, decimal Bpm)> _pendingBpms = [];
    private readonly List<(int Bar, int Tick, decimal Speed)> _pendingSpdMods = [];
    private readonly List<(int Timeline, int Bar, int Tick, decimal Speed)> _pendingTils = [];

    private void HandleBpm(string[] args)
    {
        if (args.Length < 2) return;
        if (!TryParseBarTick(args[0], out var bar, out var tick)) return;
        if (!decimal.TryParse(args[1], CultureInfo.InvariantCulture, out var bpm)) return;
        _pendingBpms.Add((bar, tick, bpm));
    }

    private void HandleBeat(string[] args)
    {
        if (args.Length < 3) return;
        if (!int.TryParse(args[0], out var bar)) return;
        if (!int.TryParse(args[1], out var num)) return;
        if (!int.TryParse(args[2], out var den)) return;
        if (num < 0 || den <= 0)
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, string.Join(",", args), "nonnegative numerator and positive denominator"));
        Ugc.Events.AppendChild(new umgr.BeatEvent { Bar = bar, Numerator = num, Denominator = den });
    }

    private void HandleSpdMod(string[] args)
    {
        if (args.Length < 2) return;
        if (!TryParseBarTick(args[0], out var bar, out var tick)) return;
        if (!decimal.TryParse(args[1], CultureInfo.InvariantCulture, out var speed)) return;
        _pendingSpdMods.Add((bar, tick, speed));
    }

    private static bool TryParseBarTick(string s, out int bar, out int tick)
    {
        bar = 0;
        tick = 0;
        var idx = s.IndexOf('\'');
        if (idx <= 0) return false;
        return int.TryParse(s.AsSpan(0, idx), out bar)
               && int.TryParse(s.AsSpan(idx + 1), out tick);
    }

    // UGC body/event positions use a fixed four-quarter axis. BEAT only
    // controls visual bars, whose cumulative positions are built separately.
    internal int BarTickToAbsTick(int bar, int tick) =>
        checked(bar * ChartResolution.UmiguriTick + ScaleTick(tick));
}
