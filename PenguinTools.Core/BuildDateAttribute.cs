using System.Globalization;
using System.Reflection;

namespace PenguinTools.Core;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class BuildDateAttribute(string value) : Attribute
{
    public DateTime DateTime { get; } =
        DateTime.ParseExact(value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public static DateTime? GetAssemblyBuildDate(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly();
        if (assembly is null)
        {
            return null;
        }

        var attribute = assembly.GetCustomAttribute<BuildDateAttribute>();
        return attribute?.DateTime;
    }
}
