using System.Globalization;

namespace PenguinTools.Chart;

internal static class C2sNumber
{
    internal static string Bpm(decimal value) => value.ToString("0.000#########################", CultureInfo.InvariantCulture);
    internal static string Speed(decimal value) => value.ToString("0.000000######################", CultureInfo.InvariantCulture);
}
