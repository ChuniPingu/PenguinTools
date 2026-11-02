using System.Globalization;

namespace PenguinTools.Chart;

internal enum NativeNumberKind { Value, Bpm, Speed }

internal static class NativeNumber
{
    internal const decimal SmallestNonzero = 0.0000000000000000000000000001m;
    internal const decimal FallbackMagnitude = 100000m;

    internal static decimal Read(double value, NativeNumberKind kind, out bool adjusted,
        string? original = null, string? shortest = null)
    {
        try
        {
            var result = ToDecimal(value, shortest);
            if (result != 0 || original is null || !HasNonzeroCoefficient(original))
            {
                adjusted = false;
                return result;
            }
        }
        catch (Exception ex) when (ex is OverflowException or FormatException)
        {
            // Keep the precise path for normal inputs; only an unrepresentable
            // value reaches the finite fallback below.
        }

        adjusted = true;
        if (kind == NativeNumberKind.Value)
        {
            return 0;
        }
        if (kind == NativeNumberKind.Bpm)
        {
            return value > 1 ? FallbackMagnitude : 150m;
        }
        if (double.IsNaN(value))
        {
            return 1;
        }
        var negative = value < 0 || value == 0 && original?.TrimStart().StartsWith('-') == true;
        var sign = negative ? -1m : 1m;
        if (Math.Abs(value) < (double)SmallestNonzero)
        {
            return sign * SmallestNonzero;
        }
        if (decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var nearest) && nearest != 0)
        {
            return Math.Clamp(nearest, -FallbackMagnitude, FallbackMagnitude);
        }
        return sign * FallbackMagnitude;
    }

    internal static decimal Project(decimal value, NativeNumberKind kind, out bool adjusted)
    {
        var native = ToDouble(value, preserveSingle: true);
        try
        {
            adjusted = false;
            return ToDecimal(native);
        }
        catch (OverflowException)
        {
            // A decimal endpoint rounds to the double just outside its range.
            // The adjacent double fits and can retain exactly the same float32.
            var adjacent = native < 0 ? Math.BitIncrement(native) : Math.BitDecrement(native);
            if ((float)adjacent == SingleValue(value))
            {
                try
                {
                    var result = ToDecimal(adjacent);
                    adjusted = true;
                    return result;
                }
                catch (OverflowException) { }
            }
            return Read(native, kind, out adjusted);
        }
    }

    internal static bool HasNonzeroCoefficient(string value)
    {
        var exponent = value.IndexOfAny(['e', 'E']);
        var coefficient = exponent < 0 ? value.AsSpan() : value.AsSpan(0, exponent);
        return coefficient.ContainsAnyInRange('1', '9');
    }

    internal static decimal ToDecimal(double value, string? shortest = null)
    {
        if (!double.IsFinite(value))
        {
            throw new OverflowException();
        }
        var result = decimal.Parse(shortest ?? value.ToString("R", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
        if (result == 0 && value != 0)
        {
            throw new OverflowException();
        }
        var native = (float)value;
        if (SingleValue(result) == native)
        {
            return result;
        }

        // A shortest double representation can lie on the opposite side of an
        // exact float midpoint. Keep enough digits to preserve ties-to-even.
        result = decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture);
        if (SingleValue(result) != native)
        {
            // Very small midpoints can exceed decimal's scale. Choose the
            // adjacent decimal only if it still represents the native float.
            var scale = (byte)((decimal.GetBits(result)[3] >> 16) & 0xff);
            var unit = new decimal(1, 0, 0, false, scale);
            result += SingleValue(result) < native ? unit : -unit;
            if (SingleValue(result) != native)
            {
                throw new OverflowException();
            }
        }
        return result;
    }

    internal static double ToDouble(decimal value, bool preserveSingle)
    {
        // Decimal's direct double cast can round twice near float midpoints.
        var result = double.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (preserveSingle)
        {
            var native = SingleValue(value);
            if ((float)result != native)
            {
                result = (float)result < native ? Math.BitIncrement(result) : Math.BitDecrement(result);
            }
        }
        return result;
    }

    private static float SingleValue(decimal value) =>
        float.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
