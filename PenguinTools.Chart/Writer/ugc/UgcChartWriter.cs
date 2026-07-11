using System.Globalization;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core;
using PenguinTools.Core.IO;

namespace PenguinTools.Chart.Writer.ugc;

using umgr = Models.umgr;

/// <summary>Writes canonical, UTF-8 UGC v8 text.</summary>
public sealed class UgcChartWriter(UgcWriteRequest request)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly umgr.Chart _chart = request.Chart ?? throw new ArgumentNullException(nameof(request));

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        await AtomicFile.WriteAsync(request.Path, async (stream, token) =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            foreach (var line in Lines()) await writer.WriteLineAsync(line.AsMemory(), token);
            await writer.FlushAsync(token);
        }, ct);
        return OperationResult.Success();
    }

    private IEnumerable<string> Lines()
    {
        var m = _chart.Meta;
        yield return "@VER\t8";
        yield return "@EXVER\t1";
        yield return "@TICKS\t480";
        yield return $"@TITLE\t{Clean(m.Title)}";
        yield return $"@SORT\t{Clean(m.SortName)}";
        yield return $"@ARTIST\t{Clean(m.Artist)}";
        yield return $"@DESIGN\t{Clean(m.Designer)}";
        yield return $"@DIFF\t{DifficultyValue(m.Difficulty)}";
        if (m.Difficulty == PenguinTools.Core.Metadata.Difficulty.WorldsEnd)
            yield return $"@LEVEL\t{(m.WeDifficulty == PenguinTools.Core.Metadata.StarDifficulty.Na ? Math.Max(1, (int)m.Level) : ((int)m.WeDifficulty + 1) / 2)}";
        else yield return $"@CONST\t{F(m.Level)}";
        yield return $"@SONGID\t{Clean(string.IsNullOrWhiteSpace(m.MgxcId) ? m.Id?.ToString(Invariant) ?? "" : m.MgxcId)}";
        yield return $"@MAINBPM\t{F(m.MainBpm)}";
        yield return $"@BGM\t{Clean(m.BgmFilePath)}";
        yield return $"@BGMOFS\t{F(m.BgmManualOffset)}";
        yield return $"@BGMPRV\t{F(m.BgmPreviewStart)}\t{F(m.BgmPreviewStop)}";
        yield return $"@JACKET\t{Clean(m.JacketFilePath)}";
        yield return $"@CMT\t{Clean(m.Comment)}";

        var beats = _chart.Events.Children.OfType<umgr.BeatEvent>().OrderBy(x => x.Bar).ToArray();
        if (beats.Length == 0) beats = [new umgr.BeatEvent { Bar = 0, Tick = 0, Numerator = 4, Denominator = 4 }];
        foreach (var beat in beats) yield return $"@BEAT\t{beat.Bar}\t{beat.Numerator}\t{beat.Denominator}";
        var axis = new BarAxis(beats);
        foreach (var bpm in _chart.Events.Children.OfType<umgr.BpmEvent>().OrderBy(x => x.Tick))
            yield return $"@BPM\t{axis.Format(bpm.Tick.Original)}\t{F(bpm.Bpm)}";
        foreach (var speed in Canonical(_chart.Events.Children.OfType<umgr.NoteSpeedEvent>()))
            yield return $"@SPDMOD\t{axis.Format(speed.Tick.Original)}\t{F(speed.Speed)}";
        foreach (var speed in Canonical(_chart.Events.Children.OfType<umgr.ScrollSpeedEvent>())
                     .OrderBy(x => x.Timeline).ThenBy(x => x.Tick))
            yield return $"@TIL\t{speed.Timeline}\t{axis.Format(speed.Tick.Original)}\t{F(speed.Speed)}";
        yield return $"@MAINTIL\t{m.MainTil}";
        yield return "@ENDHEAD";

        var timeline = int.MinValue;
        // Chart.Sort places negative notes immediately after their paired positive root.
        // Preserve that order: the UGC grammar resolves AIR pairing contextually.
        foreach (var note in _chart.Notes.Children)
        {
            if (note.Timeline != timeline)
            {
                timeline = note.Timeline;
                yield return $"@USETIL\t{timeline}";
            }
            foreach (var line in NoteLines(note, axis, HasEffectCarrier(note))) yield return line;
        }
    }

    private bool HasEffectCarrier(umgr.Note note)
    {
        if (note is not umgr.ExTapableNote { Effect: { } effect }) return false;
        return _chart.Notes.Children.OfType<umgr.ExTap>().Any(x =>
            x.Tick == note.Tick && x.Effect == effect && x.Lane <= note.Lane &&
            x.Lane + x.Width >= note.Lane + note.Width);
    }

    private static IEnumerable<T> Canonical<T>(IEnumerable<T> events) where T : umgr.SpeedEventBase
    {
        T? previous = null;
        foreach (var item in events.OrderBy(x => x.Tick))
        {
            if (previous is not null && previous.Tick == item.Tick && previous.Speed == item.Speed) continue;
            previous = item;
            yield return item;
        }
    }

    private static IEnumerable<string> NoteLines(umgr.Note note, BarAxis axis, bool hasEffectCarrier)
    {
        var prefix = $"#{axis.Format(note.Tick.Original)}:";
        var pos = $"{B36(note.Lane)}{B36(note.Width)}";
        switch (note)
        {
            case umgr.Tap: yield return prefix + "t" + pos; break;
            case umgr.ExTap x: yield return prefix + "x" + pos + Effect(x.Effect); break;
            case umgr.Flick: yield return prefix + "f" + pos; break;
            case umgr.Damage: yield return prefix + "d" + pos; break;
            case umgr.Hold x:
                if (!hasEffectCarrier && x.Effect is { } holdEffect) yield return prefix + "x" + pos + Effect(holdEffect);
                yield return prefix + "h" + pos;
                foreach (var c in x.Children)
                {
                    if (c.Timeline != x.Timeline) yield return $"@USETIL\t{c.Timeline}";
                    yield return $"#{c.Tick.Original - x.Tick.Original}:s";
                }
                if (x.Children.Any(c => c.Timeline != x.Timeline)) yield return $"@USETIL\t{x.Timeline}";
                break;
            case umgr.Slide x:
                if (!hasEffectCarrier && x.Effect is { } slideEffect) yield return prefix + "x" + pos + Effect(slideEffect);
                yield return prefix + "s" + pos;
                foreach (var c in x.Children.OfType<umgr.SlideJoint>())
                {
                    if (c.Timeline != x.Timeline) yield return $"@USETIL\t{c.Timeline}";
                    yield return $"#{c.Tick.Original - x.Tick.Original}:{(c.Joint == Joint.C ? 'c' : 's')}{B36(c.Lane)}{B36(c.Width)}";
                }
                if (x.Children.Any(c => c.Timeline != x.Timeline)) yield return $"@USETIL\t{x.Timeline}";
                break;
            case umgr.Air x:
                yield return prefix + "a" + pos + Direction(x.Direction) + AirColor(x.Color);
                break;
            case umgr.AirSlide x:
                yield return prefix + "S" + pos + B36((int)Math.Round(x.Height), 2) + AirColor(x.Color);
                foreach (var c in x.Children.OfType<umgr.AirSlideJoint>())
                {
                    if (c.Timeline != x.Timeline) yield return $"@USETIL\t{c.Timeline}";
                    yield return $"#{c.Tick.Original - x.Tick.Original}:{(c.Joint == Joint.C ? 'c' : 's')}{B36(c.Lane)}{B36(c.Width)}{B36((int)Math.Round(c.Height), 2)}";
                }
                if (x.Children.Any(c => c.Timeline != x.Timeline)) yield return $"@USETIL\t{x.Timeline}";
                break;
            case umgr.AirCrash x:
                yield return prefix + "C" + pos + B36((int)Math.Round(x.Height), 2) + CrashColor(x.Color) + "," + (x.Density.Original >= int.MaxValue ? "$" : x.Density.Original.ToString(Invariant));
                foreach (var c in x.Children.OfType<umgr.AirCrashJoint>())
                {
                    if (c.Timeline != x.Timeline) yield return $"@USETIL\t{c.Timeline}";
                    yield return $"#{c.Tick.Original - x.Tick.Original}:c{B36(c.Lane)}{B36(c.Width)}{B36((int)Math.Round(c.Height), 2)}";
                }
                if (x.Children.Any(c => c.Timeline != x.Timeline)) yield return $"@USETIL\t{x.Timeline}";
                break;
        }
    }

    private static int DifficultyValue(PenguinTools.Core.Metadata.Difficulty d) => d switch
    {
        PenguinTools.Core.Metadata.Difficulty.WorldsEnd => 4,
        PenguinTools.Core.Metadata.Difficulty.Ultima => 5,
        _ => (int)d
    };
    private static string F(decimal value) => value.ToString("0.############################", Invariant);
    private static string Clean(string value) => (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    private static char B36(int value) => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[Math.Clamp(value, 0, 35)];
    private static string B36(int value, int width)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        value = Math.Clamp(value, 0, 1295);
        Span<char> buffer = stackalloc char[Math.Max(1, width)];
        buffer.Fill('0');
        for (var index = buffer.Length - 1; index >= 0 && value > 0; index--)
        {
            buffer[index] = digits[value % 36];
            value /= 36;
        }
        return buffer.ToString();
    }
    private static char Effect(ExEffect value) => value switch { ExEffect.UP => 'U', ExEffect.DW => 'D', ExEffect.CE => 'C', ExEffect.RS => 'L', ExEffect.LS => 'R', ExEffect.RC => 'A', ExEffect.LC => 'W', ExEffect.BS => 'I', _ => 'U' };
    private static string Direction(AirDirection value) => value switch { AirDirection.UL => "UL", AirDirection.UR => "UR", AirDirection.DW => "DC", AirDirection.DL => "DL", AirDirection.DR => "DR", _ => "UC" };
    private static char AirColor(Color value) => value switch { Color.PNK => 'I', Color.NON => 'Z', Color.GRN => 'G', Color.LIM => 'M', Color.RED => 'R', Color.BLK => 'K', Color.VLT => 'V', Color.BLU => 'B', Color.DGR => 'D', Color.AQA => 'A', Color.CYN => 'C', Color.YEL => 'Y', Color.ORN => 'O', Color.GRY => 'H', Color.PPL => 'P', _ => 'N' };
    private static char CrashColor(Color value) => value switch { Color.RED => '1', Color.ORN => '2', Color.YEL => '3', Color.LIM => '4', Color.GRN => '5', Color.AQA => '6', Color.CYN => '7', Color.DGR => '8', Color.BLU => '9', Color.VLT => 'A', Color.PPL => 'Y', Color.PNK => 'B', Color.GRY => 'C', Color.BLK => 'D', Color.NON => 'Z', _ => '0' };

    private sealed class BarAxis(IEnumerable<umgr.BeatEvent> source)
    {
        private readonly umgr.BeatEvent[] _beats = source.OrderBy(x => x.Bar).ToArray();
        public string Format(int tick)
        {
            var active = _beats.Where(x => x.Tick.Original <= tick).LastOrDefault() ?? _beats[0];
            var length = ChartResolution.UmiguriTick * active.Numerator / active.Denominator;
            var delta = tick - active.Tick.Original;
            return $"{active.Bar + delta / length}'{delta % length}";
        }
    }
}
