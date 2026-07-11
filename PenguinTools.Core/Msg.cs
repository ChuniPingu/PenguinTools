namespace PenguinTools.Core;

public static class Msg
{
    public static MessageDescriptor Key(string key, IReadOnlyDictionary<string, object?>? args = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new MessageDescriptor(key, args);
    }

    public static MessageDescriptor Create(string key, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (args.Length == 0) return Key(key);

        var dictionary = new Dictionary<string, object?>(args.Length, StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
            dictionary[$"arg{index}"] = args[index];

        return Key(key, dictionary);
    }

    public static MessageDescriptor Create(string key, params (string Name, object? Value)[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (args.Length == 0) return Key(key);

        return Key(key, args.ToDictionary(arg => arg.Name, arg => arg.Value, StringComparer.Ordinal));
    }

    public static MessageDescriptor Unhandled(string detail)
    {
        return Create(MsgKeys.Error_Unhandled, ("detail", detail));
    }
}