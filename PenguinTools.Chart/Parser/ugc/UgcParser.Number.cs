using System.Globalization;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser.ugc;

public partial class UgcParser
{
    private bool TryParseNativeNumber(string text, string field, out decimal value)
    {
        value = 0;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var native))
        {
            return false;
        }
        value = NativeNumber.Read(native, field is "BPM" or "MAINBPM" ? NativeNumberKind.Bpm : NativeNumberKind.Speed,
            out var adjusted, original: text);
        if (adjusted)
        {
            ReportAtCurrentLine(Severity.Warning, Msg.Create(MsgKeys.Warn_Number_adjusted, $"UGC {field}", text,
                value.ToString("G29", CultureInfo.InvariantCulture)));
        }
        return true;
    }
}
