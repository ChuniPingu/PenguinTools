using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PenguinTools.Chart;

// Compact source metadata for fields the editor cannot represent directly.
// It is carried by MGXC/UGC only, never emitted as a private C2S command.
public static class C2SRoundTrip
{
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
            return "";
        string tag = p[0];
        if (tag == "SLA")
            return "";
        if ((" AHD AHX ASX ").Contains(" " + tag + " ") && p.Count == 7)
            p.Add("DEF");
        if (tag == "SFL")
        {
            p[0] = tag = "SLP";
            p.Add("0");
        }
        if (tag == "FLK" && p.Count == 5)
            p.Add("L");
        if (tag == "ALD")
        while (p.Count < 13)
            p.Add("DEF");
        if (tag == "ALD" && p[12] == "Trace")
            p[12] = "DEF";
        if ((tag == "ASC" || tag == "ASD") && p.Count == 11)
            p.Add("DEF");
        if ((" AIR AUL AUR ADW ADL ADR ").Contains(" " + tag + " ") && p.Count == 6)
            p.Add("DEF");
        if ((" SLC SLD SXC SXD ").Contains(" " + tag + " "))
        {
            if (p.Count == 7)
                p.Add(p[4]);
            if (p.Count == 8)
                p.Add("SLD");
        }
        decimal tick = decimal.Parse(p[1], CultureInfo.InvariantCulture) * 1920 + decimal.Parse(p[2], CultureInfo.InvariantCulture) * 1920 / resolution;
        p[1] = tick.ToString("0.############################", CultureInfo.InvariantCulture);
        p[2] = "0";
        int duration = (" HLD HXD SLC SLD SXC SXD ALD ").Contains(" " + tag + " ") ? (tag == "ALD" ? 7 : 5) : (" ASC ASD ").Contains(" " + tag + " ") ? 7 : (" AHD AHX ASX ").Contains(" " + tag + " ") ? 6 : (" SLP SFL DCM STP ").Contains(" " + tag + " ") ? 3 : -1;
        if (duration >= 0 && duration < p.Count)
            p[duration] = (decimal.Parse(p[duration], CultureInfo.InvariantCulture) * 1920 / resolution).ToString(CultureInfo.InvariantCulture);
        if (tag == "ALD")
            p[5] = (decimal.Parse(p[5], CultureInfo.InvariantCulture) * 1920 / resolution).ToString(CultureInfo.InvariantCulture);
        for (int i = 1; i < p.Count; i++)
        {
            decimal n;
            if (decimal.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n))
                p[i] = n.ToString("0.############################", CultureInfo.InvariantCulture);
        }
        return string.Join("\t", p.ToArray());
    }
    public static string Fingerprint(string text)
    {
        int resolution = 384;
        var lines = text.Replace("\r", "").Split('\n');
        foreach (string l in lines)
        {
            var p = Parts(l);
            if (p.Length > 1 && p[0] == "RESOLUTION")
                resolution = int.Parse(p[1], CultureInfo.InvariantCulture);
        }
        var rows = new List<string>();
        foreach (string l in lines)
        {
            string s = Normalize(l, resolution);
            if (s.Length > 0)
                rows.Add(s);
        }
        rows.Sort(StringComparer.Ordinal);
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", rows.ToArray())))).Replace("-", "");
    }
    public static string Encode(string text, string view = "", string exceptions = "")
    {
        var saved = new List<string>();
        saved.Add("HASH\t" + Fingerprint(text));
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            var p = Parts(line);
            if (p.Length == 0 || p[0].StartsWith("//"))
                continue;
            if (!IsRecord(p[0]) || (" SLA BPM MET SLP SFL DCM STP CLK ").Contains(" " + p[0] + " "))
                saved.Add(string.Join("\t", p));
        }
        foreach (string kind in new[] { "BEAT", "BPM", "TIL", "SPDMOD" })
        {
            var rows = new List<string>();
            foreach (string line in view.Replace("\r", "").Split('\n'))
            if (line.StartsWith(kind + "\t"))
                rows.Add(line);
            saved.Add("VIEWHASH\t" + kind + "\t" + ViewHash(rows));
        }
        foreach (string line in exceptions.Split('\n'))
        if (line.StartsWith("EXCEPT\t"))
            saved.Add(line);
        // Equal-position long-note rows are ordered edges, not an unordered set.
        // Save only ambiguous groups; ordinary rows need no extra metadata.
        var groups = new Dictionary<string, List<string>>();
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            var p = Parts(line);
            if (p.Length < 5)
                continue;
            string family = (" SLC SLD SXC SXD ").Contains(" " + p[0] + " ") ? "S" :
                (" ASC ASD ").Contains(" " + p[0] + " ") ? "A" :
                (" AHD AHX ASX ").Contains(" " + p[0] + " ") ? "H" : p[0] == "ALD" ? "C" : "";
            if (family.Length == 0)
                continue;
            string key = family + "|" + p[1] + "|" + p[2] + "|" + p[3] + "|" + p[4];
            if (family == "A" || family == "C")
            {
                key += "|" + p[5] + "|" + p[6];
                for (int i = 11; i < p.Length; i++)
                    key += "|" + p[i];
            }
            else if (family == "H")
                key += "|" + p[5];
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<string>();
            list.Add(string.Join("\t", p));
        }
        foreach (var group in groups.Values)
        {
            if (group.Count < 2 || new HashSet<string>(group).Count < 2)
                continue;
            foreach (string line in group)
                saved.Add("ORDER\t" + line);
        }
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            var p = Parts(line);
            if (p.Length > 0 && (" AIR AUL AUR ADW ADL ADR ASC ASD ").Contains(" " + p[0] + " ") && p[p.Length - 1] == "GRN")
                saved.Add("COLOR\t" + string.Join("\t", p));
        }
        return Pack(string.Join("\n", saved.ToArray()));
    }
    // Byte-oriented LZ encoding keeps lossless snapshots within MGXC string limits.
    public static string Pack(string text, bool forceCompression = false)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        if (!forceCompression && data.Length < 12000)
            return "MGR_C2S_V1=" + Convert.ToBase64String(data) + ";";
        var packed = new List<byte>();
        var last = new Dictionary<int, List<int>>();
        for (int i = 0; i < data.Length;)
        {
            int length = 0, prior = 0, key = i + 2 < data.Length ? (data[i] << 16) | (data[i + 1] << 8) | data[i + 2] : -1;
            List<int>? candidates;
            if (key >= 0 && last.TryGetValue(key, out candidates))
            for (int k = candidates.Count - 1; k >= 0; k--)
            {
                int pos = candidates[k], matched = 0;
                if (i - pos > 65535)
                    break;
                while (matched < 258 && i + matched < data.Length && data[pos + matched] == data[i + matched])
                    matched++;
                if (matched > length)
                {
                    length = matched;
                    prior = pos;
                }
                if (length == 258)
                    break;
            }
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
                packed.Add(data[i]);
            for (int j = i; j < i + advance && j + 2 < data.Length; j++)
            {
                int code = (data[j] << 16) | (data[j + 1] << 8) | data[j + 2];
                List<int>? list;
                if (!last.TryGetValue(code, out list))
                    last[code] = list = new List<int>();
                if (list.Count == 256)
                    list.RemoveAt(0);
                list.Add(j);
            }
            i += advance;
        }
        return "MGR_C2S_LZ1=" + Convert.ToBase64String(packed.ToArray()) + ";";
    }
    public static string Decode(string copyright)
    {
        var m = System.Text.RegularExpressions.Regex.Match(copyright ?? "", @"(?:^|;)MGR_C2S_(V1|LZ1)=([A-Za-z0-9+/=]+);");
        if (!m.Success)
            return "";
        byte[] data = Convert.FromBase64String(m.Groups[2].Value);
        if (m.Groups[1].Value == "V1")
            return Encoding.UTF8.GetString(data);
        var output = new List<byte>();
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 255)
                output.Add(data[i]);
            else
            {
                if (i + 3 >= data.Length)
                    throw new FormatException("Truncated chart metadata");
                int distance = (data[++i] << 8) | data[++i], length = data[++i] + 3;
                if (distance == 0 || distance > output.Count)
                    throw new FormatException("Invalid chart metadata reference");
                for (int j = 0; j < length; j++)
                    output.Add(output[output.Count - distance]);
            }
            if (output.Count > 4000000)
                throw new FormatException("Chart metadata exceeds limit");
        }
        return Encoding.UTF8.GetString(output.ToArray());
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
                    p[i] = n.ToString("0.############################", CultureInfo.InvariantCulture);
            }
            rows.Add(string.Join("\t", p));
        }
        rows.Sort(StringComparer.Ordinal);
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", rows.ToArray())))).Replace("-", "");
    }
}
