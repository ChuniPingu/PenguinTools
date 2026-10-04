using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PenguinTools.Chart;

// Compact source metadata for fields the editor cannot represent directly.
// It is carried by MGXC/UGC only, never emitted as a private C2S command.
public static partial class C2SRoundTrip
{
    [GeneratedRegex(@"(?:^|;)MGR_C2S_(V1|LZ1)=([A-Za-z0-9+/=]+);", RegexOptions.None, 1000)]
    private static partial Regex MetadataRegex();
    public const string Records = "BPM MET SLP SFL DCM STP CLK TAP CHR FLK MNE HLD HXD SLC SLD SXC SXD SLA AIR AUL AUR ADW ADL ADR ASC ASD AHD AHX ASX ALD";
    public static string[] Parts(string s)
    {
        return s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }
    public static bool IsRecord(string s)
    {
        return (" " + Records + " ").Contains(" " + s + " ");
    }
    public static string Normalize(string line, int resolution)
    {
        var p = new List<string>(Parts(line));
        if (p.Count == 0 || !IsRecord(p[0]))
        {
            return "";
        }

        string tag = p[0];
        if (tag == "SLA")
        {
            return "";
        }

        AddRecordDefaults(p);
        tag = p[0];
        decimal tick = decimal.Parse(p[1], CultureInfo.InvariantCulture) * 1920 + decimal.Parse(p[2], CultureInfo.InvariantCulture) * 1920 / resolution;
        p[1] = tick.ToString("0.############################", CultureInfo.InvariantCulture);
        p[2] = "0";
        int duration = DurationIndex(tag);
        if (duration >= 0 && duration < p.Count)
        {
            p[duration] = (decimal.Parse(p[duration], CultureInfo.InvariantCulture) * 1920 / resolution).ToString(CultureInfo.InvariantCulture);
        }

        if (tag == "ALD")
        {
            p[5] = (decimal.Parse(p[5], CultureInfo.InvariantCulture) * 1920 / resolution).ToString(CultureInfo.InvariantCulture);
        }

        for (int i = 1; i < p.Count; i++)
        {
            if (decimal.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                p[i] = n.ToString("0.############################", CultureInfo.InvariantCulture);
            }
        }
        return string.Join("\t", p);
    }

    private static int DurationIndex(string tag) => tag switch
    {
        "HLD" or "HXD" or "SLC" or "SLD" or "SXC" or "SXD" => 5,
        "ALD" or "ASC" or "ASD" => 7,
        "AHD" or "AHX" or "ASX" => 6,
        "SLP" or "SFL" or "DCM" or "STP" => 3,
        _ => -1
    };

    private static void AddRecordDefaults(List<string> p)
    {
        var tag = p[0];
        if ((" AHD AHX ASX ").Contains(" " + tag + " ") && p.Count == 7)
        {
            p.Add("DEF");
        }

        if (tag == "SFL")
        {
            p[0] = tag = "SLP";
            p.Add("0");
        }
        if (tag == "FLK" && p.Count == 5)
        {
            p.Add("L");
        }

        if (tag == "ALD")
        {
            AddCrashDefaults(p);
        }

        if ((tag == "ASC" || tag == "ASD") && p.Count == 11)
        {
            p.Add("DEF");
        }

        if ((" AIR AUL AUR ADW ADL ADR ").Contains(" " + tag + " ") && p.Count == 6)
        {
            p.Add("DEF");
        }

        if ((" SLC SLD SXC SXD ").Contains(" " + tag + " "))
        {
            AddSlideDefaults(p);
        }
    }

    private static void AddSlideDefaults(List<string> p)
    {
        if (p.Count == 7)
        {
            p.Add(p[4]);
        }

        if (p.Count == 8)
        {
            p.Add("SLD");
        }
    }

    private static void AddCrashDefaults(List<string> p)
    {
        while (p.Count < 13)
        {
            p.Add("DEF");
        }

        if (p[12] == "Trace")
        {
            p[12] = "DEF";
        }
    }
    public static string Fingerprint(string text)
    {
        int resolution = 384;
        var lines = text.Replace("\r", "").Split('\n');
        foreach (string l in lines)
        {
            var p = Parts(l);
            if (p.Length > 1 && p[0] == "RESOLUTION")
            {
                resolution = int.Parse(p[1], CultureInfo.InvariantCulture);
            }
        }
        var rows = new List<string>();
        foreach (string l in lines)
        {
            string s = Normalize(l, resolution);
            if (s.Length > 0)
            {
                rows.Add(s);
            }
        }
        rows.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
    }
    public static string Encode(string text, string view = "", string exceptions = "")
    {
        var saved = new List<string>();
        saved.Add("HASH\t" + Fingerprint(text));
        var sourceLines = text.Replace("\r", "").Split('\n');
        foreach (string line in sourceLines)
        {
            var p = Parts(line);
            if (p.Length == 0 || p[0].StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsRecord(p[0]) || (" SLA BPM MET SLP SFL DCM STP CLK ").Contains(" " + p[0] + " "))
            {
                saved.Add(string.Join("\t", p));
            }
        }
        foreach (string kind in new[] { "BEAT", "BPM", "TIL", "SPDMOD" })
        {
            var rows = view.Replace("\r", "").Split('\n').Where(line => line.StartsWith(kind + "\t", StringComparison.Ordinal));
            saved.Add("VIEWHASH\t" + kind + "\t" + ViewHash(rows));
        }
        saved.AddRange(exceptions.Split('\n').Where(line => line.StartsWith("EXCEPT\t", StringComparison.Ordinal)));
        SaveAmbiguousOrder(sourceLines, saved);
        foreach (string line in sourceLines)
        {
            var p = Parts(line);
            if (p.Length > 0 && (" AIR AUL AUR ADW ADL ADR ASC ASD ").Contains(" " + p[0] + " ") && p[p.Length - 1] == "GRN")
            {
                saved.Add("COLOR\t" + string.Join("\t", p));
            }
        }
        return Pack(string.Join("\n", saved));
    }

    private static string LongNoteFamily(string tag) => tag switch
    {
        "SLC" or "SLD" or "SXC" or "SXD" => "S",
        "ASC" or "ASD" => "A",
        "AHD" or "AHX" or "ASX" => "H",
        "ALD" => "C",
        _ => ""
    };

    private static string LongNoteKey(string[] p, string family)
    {
        var key = new StringBuilder(family).Append('|').AppendJoin('|', p.Skip(1).Take(4));
        if (family == "A" || family == "C")
        {
            key.Append('|').Append(p[5]).Append('|').Append(p[6]);
            foreach (var part in p.Skip(11))
            {
                key.Append('|').Append(part);
            }
        }
        else if (family == "H")
        {
            key.Append('|').Append(p[5]);
        }

        return key.ToString();
    }

    private static void SaveAmbiguousOrder(IEnumerable<string> sourceLines, List<string> saved)
    {
        // Equal-position long-note rows are ordered edges, not an unordered set.
        // Save only ambiguous groups; ordinary rows need no extra metadata.
        var groups = new Dictionary<string, List<string>>();
        foreach (string line in sourceLines)
        {
            var p = Parts(line);
            if (p.Length < 5)
            {
                continue;
            }

            string family = LongNoteFamily(p[0]);
            if (family.Length == 0)
            {
                continue;
            }

            string key = LongNoteKey(p, family);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = new List<string>();
            }

            list.Add(string.Join("\t", p));
        }
        foreach (var group in groups.Values)
        {
            if (group.Count < 2 || new HashSet<string>(group).Count < 2)
            {
                continue;
            }

            saved.AddRange(group.Select(line => "ORDER\t" + line));
        }
    }
    // Byte-oriented LZ encoding keeps lossless snapshots within MGXC string limits.
    public static string Pack(string text, bool forceCompression = false)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        if (!forceCompression && data.Length < 12000)
        {
            return "MGR_C2S_V1=" + Convert.ToBase64String(data) + ";";
        }

        var packed = new List<byte>();
        var last = new Dictionary<int, List<int>>();
        for (int i = 0; i < data.Length;)
        {
            var (length, prior) = FindMatch(data, i, last);
            int advance = length >= 4 ? length : 1;
            if (length >= 4)
            {
                int distance = i - prior;
                packed.Add(255);
                packed.Add((byte)(distance >> 8));
                packed.Add((byte)distance);
                packed.Add((byte)(length - 3));
            }
            else
            {
                packed.Add(data[i]);
            }

            RememberPositions(data, i, advance, last);
            i += advance;
        }
        return "MGR_C2S_LZ1=" + Convert.ToBase64String(packed.ToArray()) + ";";
    }

    private static (int Length, int Prior) FindMatch(byte[] data, int index, Dictionary<int, List<int>> last)
    {
        int key = index + 2 < data.Length ? (data[index] << 16) | (data[index + 1] << 8) | data[index + 2] : -1;
        if (key < 0 || !last.TryGetValue(key, out var candidates))
        {
            return (0, 0);
        }

        int length = 0, prior = 0;
        for (int k = candidates.Count - 1; k >= 0; k--)
        {
            int pos = candidates[k], matched = 0;
            if (index - pos > 65535)
            {
                break;
            }

            while (matched < 258 && index + matched < data.Length && data[pos + matched] == data[index + matched])
            {
                matched++;
            }

            if (matched > length)
            {
                length = matched;
                prior = pos;
            }
            if (length == 258)
            {
                break;
            }
        }
        return (length, prior);
    }

    private static void RememberPositions(byte[] data, int index, int advance, Dictionary<int, List<int>> last)
    {
        for (int j = index; j < index + advance && j + 2 < data.Length; j++)
        {
            int code = (data[j] << 16) | (data[j + 1] << 8) | data[j + 2];
            if (!last.TryGetValue(code, out var list))
            {
                last[code] = list = new List<int>();
            }

            if (list.Count == 256)
            {
                list.RemoveAt(0);
            }

            list.Add(j);
        }
    }
    public static string Decode(string copyright)
    {
        var m = MetadataRegex().Match(copyright ?? "");
        if (!m.Success)
        {
            return "";
        }

        byte[] data = Convert.FromBase64String(m.Groups[2].Value);
        if (m.Groups[1].Value == "V1")
        {
            return Encoding.UTF8.GetString(data);
        }

        var output = new List<byte>();
        int i = 0;
        while (i < data.Length)
        {
            if (data[i] != 255)
            {
                output.Add(data[i]);
            }
            else
            {
                CopyReference(data, i, output);
                i += 3;
            }
            if (output.Count > 4000000)
            {
                throw new FormatException("Chart metadata exceeds limit");
            }

            i++;
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void CopyReference(byte[] data, int index, List<byte> output)
    {
        if (index + 3 >= data.Length)
        {
            throw new FormatException("Truncated chart metadata");
        }

        int distance = (data[index + 1] << 8) | data[index + 2], length = data[index + 3] + 3;
        if (distance == 0 || distance > output.Count)
        {
            throw new FormatException("Invalid chart metadata reference");
        }

        for (int j = 0; j < length; j++)
        {
            output.Add(output[output.Count - distance]);
        }
    }
    public static string ViewHash(IEnumerable<string> lines)
    {
        var rows = new List<string>();
        foreach (string line in lines)
        {
            var p = Parts(line);
            for (int i = 1; i < p.Length; i++)
            {
                decimal n;
                if (decimal.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n))
                {
                    p[i] = n.ToString("0.############################", CultureInfo.InvariantCulture);
                }
            }
            rows.Add(string.Join("\t", p));
        }
        rows.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
    }
}
