using System.Globalization;
using PenguinTools.Chart.Models;

namespace PenguinTools.Chart;

internal static class C2sHeight
{
    // AIR attachment compares native integer tenths, after parsing the displayed
    // height as a single-precision float. Preserve the editable decimal separately.
    internal static int Key(Height height) => Key(height.Result);

    internal static int Key(decimal displayedHeight)
    {
        var displayed = float.Parse(displayedHeight.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var value = displayed * 10f + 0.5f;
        return value switch
        {
            >= int.MaxValue => int.MaxValue,
            <= int.MinValue => int.MinValue,
            _ => (int)value
        };
    }
}
