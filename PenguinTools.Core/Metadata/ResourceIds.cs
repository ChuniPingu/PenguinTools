using System.Globalization;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Core.Metadata;

public static class ResourceIds
{
    public const int MaxSongId = 2_000_000_000;

    public static bool TryParse(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static bool TryParseNonNegative(string text, out int value) =>
        TryParse(text, out value) && value >= 0;

    public static void Require(int? value, string messageKey)
    {
        if (value < 0)
        {
            throw new DiagnosticException(messageKey, value);
        }
    }

    public static void ValidateSong(int? value)
    {
        if (value is < 0 or > MaxSongId)
        {
            throw new DiagnosticException(MsgKeys.Error_Song_id_invalid, value);
        }
    }

    public static int RequireSong(Meta meta)
    {
        ValidateSong(meta.Id);
        return meta.Id ?? throw new DiagnosticException(string.IsNullOrWhiteSpace(meta.MgxcId)
            ? MsgKeys.Error_Song_id_is_not_set : MsgKeys.Error_Song_id_invalid, meta.MgxcId);
    }

    public static void ValidateFieldLine(Entry entry, AssetManager assets, IDiagnosticSink diagnostics)
    {
        // -1 is the unspecified-reference sentinel. The catalog is not the game's complete table.
        if (entry.Id != -1 && !assets.IsKnownFieldLine(entry.Id))
        {
            diagnostics.Report(new Diagnostic.Diagnostic(Severity.Warning,
                Msg.Create(MsgKeys.Warn_Field_line_unknown, entry.Id)));
        }
    }

    public static void ValidateMetadata(Meta meta)
    {
        ValidateSong(meta.Id);
        Require(meta.UnlockEventId, MsgKeys.Error_Event_id_invalid);
        Require(meta.WallId, MsgKeys.Error_Wall_id_invalid);
    }
}
