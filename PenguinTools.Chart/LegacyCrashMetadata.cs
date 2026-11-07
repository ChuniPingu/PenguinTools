using System.Globalization;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class LegacyCrashMetadata
{
    internal const string Prefix = "'MGR_LEGACY_CRUSH_V1";

    internal static void Validate(U.AirCrash root)
    {
        if (root.HasExplicitJudgements && (root.Attr != AirLadderAttr.DEF || root.Density.Original != 0))
        {
            throw new TimedDiagnosticException(Msg.Create(MsgKeys.MgCrit_Legacy_crash_attributes,
                root.Attr, root.Density.Original), root.Tick.Original, root);
        }
    }

    internal static void Read(U.Chart chart, IEnumerable<string> lines)
    {
        var roots = chart.Notes.Children.OfType<U.AirCrash>().ToArray();
        var used = new HashSet<int>();
        foreach (var line in lines.Where(l => l.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 7 || parts[0] != Prefix)
            {
                throw MargreteInput.Invalid(line, "complete legacy AIR-CRUSH metadata");
            }

            var values = new int[5];
            for (var i = 0; i < values.Length; i++)
            {
                if (!int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                {
                    throw MargreteInput.Invalid(line, "integer legacy AIR-CRUSH identity");
                }
            }

            var index = values[0];
            if (index < 0 || index >= roots.Length || !used.Add(index))
            {
                throw MargreteInput.Invalid(line, "unique AIR-CRUSH ordinal");
            }

            var root = roots[index];
            if (root.Tick.Original != values[1] || root.Lane != values[2] || root.Width != values[3] ||
                root.Height != values[4] || root.Density.Original != 0 || parts[6].Length != root.Children.Count ||
                parts[6].Any(c => c is not ('0' or '1')))
            {
                throw MargreteInput.Invalid(line, "matching AIR-CRUSH identity and child flags");
            }

            root.HasExplicitJudgements = true;
            Validate(root);
            for (var i = 0; i < root.Children.Count; i++)
            {
                ((U.AirCrashJoint)root.Children[i]).IsJudgement = parts[6][i] == '1';
            }
        }
    }

    internal static string Write(U.AirCrash root, int ordinal) => string.Create(CultureInfo.InvariantCulture,
        $"{Prefix}\t{ordinal}\t{root.Tick.Original}\t{root.Lane}\t{root.Width}\t{root.Height}\t{new string(root.Children.OfType<U.AirCrashJoint>().Select(n => n.IsJudgement ? '1' : '0').ToArray())}");
}
