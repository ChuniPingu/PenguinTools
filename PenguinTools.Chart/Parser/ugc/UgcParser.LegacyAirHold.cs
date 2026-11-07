using System.Globalization;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser.ugc;

public partial class UgcParser
{
    private void NormalizeLegacyAirHoldBlocks(SourceLine[] lines)
    {
        if (!_highPrecision)
        {
            return;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].Text;
            var colon = text.IndexOf(':');
            if (!text.StartsWith('#') || colon < 0 || !text.AsSpan(0, colon).Contains('\''))
            {
                continue;
            }
            var head = text[(colon + 1)..];
            if (head.Length < 5 || head[0] != 'H' || head.AsSpan(3, 2) is not "28")
            {
                continue;
            }

            // The old straight-height-eight export option changed only S to H,
            // leaving its height and child geometry behind. Recover only a
            // complete, constant-geometry block, never an arbitrary color suffix.
            SetCurrentLine(lines[i]);
            if (head.Length != 6 || head[5] is not ('N' or 'I'))
            {
                ThrowAtCurrentLine(Msg.Key(MsgKeys.MgCrit_Ugc_legacy_air_hold));
            }
            var lane = Lane(head[1]);
            var sameEncoding = true;
            var legacyEncoding = _version >= 9;
            var contextChanged = false;
            var previousTick = 0;
            var children = new List<(int Index, string Text)>();
            for (var j = i + 1; j < lines.Length; j++)
            {
                var child = lines[j].Text;
                if (string.IsNullOrWhiteSpace(child) || child.StartsWith('\''))
                {
                    continue;
                }
                if (child.StartsWith('#') && child.Contains('\''))
                {
                    break;
                }
                if (child.StartsWith('@'))
                {
                    contextChanged = true;
                    continue;
                }

                SetCurrentLine(lines[j]);
                var separator = child.IndexOf('>');
                var tick = 0;
                if (contextChanged || !child.StartsWith('#') || separator <= 1 ||
                    !int.TryParse(child.AsSpan(1, separator - 1), NumberStyles.None,
                        CultureInfo.InvariantCulture, out tick) || tick <= previousTick ||
                    child.Length - separator - 1 != 5)
                {
                    ThrowAtCurrentLine(Msg.Key(MsgKeys.MgCrit_Ugc_legacy_air_hold));
                }
                previousTick = tick;
                var payload = child[(separator + 1)..];
                if (payload[0] is not ('s' or 'c') || payload[2] != head[2] ||
                    payload.AsSpan(3) is not "28")
                {
                    ThrowAtCurrentLine(Msg.Key(MsgKeys.MgCrit_Ugc_legacy_air_hold));
                }
                sameEncoding &= payload[1] == head[1];
                // Earlier extended v9 exports re-encoded the H head but left
                // the inherited S child lanes in the legacy alphabet.
                legacyEncoding &= (MargreteInput.LegacyLane(payload[1]) ?? UgcPayload.Base36(payload[1])) == lane;
                children.Add((j, child[..(separator + 1)] + (payload[0] == 's' ? 's' : 'c')));
            }
            SetCurrentLine(lines[i]);
            // Intermediate S controls are nonjudging, while native H readers
            // can turn them into AIR-ACTIONs. Do not normalize across that loss.
            if (children.Count == 0 || !(sameEncoding || legacyEncoding) ||
                children.SkipLast(1).Any(child => child.Text.EndsWith('c')))
            {
                ThrowAtCurrentLine(Msg.Key(MsgKeys.MgCrit_Ugc_legacy_air_hold));
            }
            ReportAtCurrentLine(Severity.Warning, Msg.Key(MsgKeys.Warn_Ugc_legacy_air_hold_normalized));
            lines[i] = lines[i] with { Text = text[..(colon + 1)] + head[..3] + head[5] };
            foreach (var (index, normalized) in children)
            {
                lines[index] = lines[index] with { Text = normalized };
            }
        }
        ClearCurrentLine();
    }
}
