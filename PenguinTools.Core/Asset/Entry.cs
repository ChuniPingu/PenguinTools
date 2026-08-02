using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace PenguinTools.Core.Asset;

public class Entry : IComparable<Entry>, IEquatable<Entry>
{
    public static readonly Entry Default = new();

    // for xml
    private Entry()
    {
    }

    public Entry(int id, string str, string data = "")
    {
        Id = id;
        Str = str;
        Data = data;
    }

    [JsonPropertyName("id")]
    [XmlElement("id")]
    public int Id { get; init; } = -1;

    [JsonPropertyName("str")]
    [XmlElement("str")]
    public string Str { get; init; } = "Invalid";

    [JsonPropertyName("data")]
    [XmlElement("data")]
    public string Data { get; init; } = string.Empty;

    public override string ToString()
    {
        return $"{Id} {Str} {Data}";
    }

    #region IComparable

    public bool Equals(Entry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is Entry other && Equals(other));
    }

    public override int GetHashCode()
    {
        return Id;
    }

    public static bool operator ==(Entry? left, Entry? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Entry? left, Entry? right)
    {
        return !Equals(left, right);
    }

    public int CompareTo(Entry? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (other is null) return 1;

        return Id.CompareTo(other.Id);
    }

    #endregion
}