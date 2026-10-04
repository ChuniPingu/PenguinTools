using System.Globalization;

namespace PenguinTools.Chart.Parser.ugc;

public partial class UgcParser
{
    private void HandleTil(string[] args)
    {
        if (args.Length < 3)
        {
            return;
        }

        if (!int.TryParse(args[0], out var id))
        {
            return;
        }

        if (!TryParseBarTick(args[1], out var bar, out var tick))
        {
            return;
        }

        if (!decimal.TryParse(args[2], CultureInfo.InvariantCulture, out var speed))
        {
            return;
        }

        _pendingTils.Add((id, bar, tick, speed));
    }

    private void HandleMainTil(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var id))
        {
            return;
        }

        Ugc.Meta.MainTil = id;
    }

    private void ApplyUseTil(string line)
    {
        var tokens = line.Split('\t');
        if (tokens.Length < 2)
        {
            return;
        }

        if (int.TryParse(tokens[1], out var id))
        {
            _currentTimeline = id;
        }
    }
}
