using System.Security.Cryptography;
using System.Text;
using PenguinTools.Chart.Parser.c2s;
using C = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart;

internal static class C2sNoteRelations
{
    internal static IEnumerable<string> Keys(string[] lines, int resolution)
    {
        var records = C2SParser.ParseNoteRecords(lines, resolution);
        var keys = new Dictionary<C.Note, string>();
        foreach (var path in C2sSlidePaths.Build(records.Keys.OfType<C.Slide>()))
        {
            var key = Hash(string.Join('\n', path.Select(n => records[n])));
            foreach (var segment in path)
            {
                keys[segment] = key;
            }
        }
        foreach (var note in records.Keys)
        {
            AddParentKeys(note, records, keys);
        }
        var crashes = C2sAirCrashPaths.Build(records.Keys.OfType<C.AirCrash>())
            .Select(path => Hash(string.Join('\n', path.Select(n => records[n]))));
        return records.Keys.OfType<C.IPairable>().Select(n => keys[(C.Note)n]).Concat(crashes).Order(StringComparer.Ordinal);
    }

    private static void AddParentKeys(C.Note note, Dictionary<C.Note, string> records, Dictionary<C.Note, string> keys)
    {
        // Iterative traversal also bounds malformed or cyclic parent graphs.
        var pending = new Stack<C.Note>();
        var visited = new HashSet<C.Note>();
        var parent = note;
        while (!keys.ContainsKey(parent) && visited.Add(parent))
        {
            pending.Push(parent);
            if (parent is not C.IPairable { Parent: { } next } || !records.ContainsKey(next))
            {
                break;
            }
            parent = next;
        }
        while (pending.TryPop(out var item))
        {
            var parentKey = item is C.IPairable { Parent: { } pair } ? keys.GetValueOrDefault(pair, "unresolved") : "";
            keys[item] = Hash(records[item] + "\n" + parentKey);
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
