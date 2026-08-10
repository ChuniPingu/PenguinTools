using System.Globalization;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Parser.mgxc;

public partial class MgxcParser
{
    private void ParseMeta(BinaryReader br)
    {
        var name = br.ReadUtf8String(4);
        var data = br.ReadField();

        if (name == "titl")
        {
            Mgxc.Meta.Title = (string)data;
        }
        else if (name == "sort")
        {
            Mgxc.Meta.SortName = (string)data;
        }
        else if (name == "arts")
        {
            Mgxc.Meta.Artist = (string)data;
        }
        else if (name == "genr")
        {
            var genre = (string)data;
            var entry = Assets.GenreNames.FirstOrDefault(e => e.Str.Equals(genre, StringComparison.Ordinal));
            if (entry != null) Mgxc.Meta.Genre = entry;
        }
        else if (name == "dsgn")
        {
            Mgxc.Meta.Designer = (string)data;
        }
        else if (name == "diff")
        {
            Mgxc.Meta.Difficulty = UmiguriParserCommon.DifficultyFromValue((int)data);
            if (Mgxc.Meta.Difficulty == Difficulty.WorldsEnd)
                Mgxc.Meta.Stage = UmiguriParserCommon.CreateWorldsEndStage();
        }
        else if (name == "plvl")
        {
            if (Mgxc.Meta.Difficulty != Difficulty.WorldsEnd) return;
            var trimmed = ((string)data).Trim('+');
            if (!int.TryParse(trimmed, out var num)) return;
            Mgxc.Meta.WeDifficulty = num switch
            {
                1 => StarDifficulty.S1,
                2 => StarDifficulty.S2,
                3 => StarDifficulty.S3,
                4 => StarDifficulty.S4,
                5 => StarDifficulty.S5,
                _ => StarDifficulty.Na
            };
        }
        else if (name == "weat")
        {
            var attr = Assets.WeTagNames.FirstOrDefault(x => x.Str == (string)data);
            if (attr != null) Mgxc.Meta.WeTag = attr;
        }
        else if (name == "cnst")
        {
            if (Mgxc.Meta.Difficulty == Difficulty.WorldsEnd) return;
            Mgxc.Meta.Level = data.Round(2);
        }
        else if (name == "sgid")
        {
            Mgxc.Meta.MgxcId = (string)data;
            if (int.TryParse(Mgxc.Meta.MgxcId, out var id)) Mgxc.Meta.Id = id;
        }
        else if (name == "wvfn")
        {
            Mgxc.Meta.BgmFilePath = (string)data;
            if (!string.IsNullOrWhiteSpace(Mgxc.Meta.BgmFilePath))
                QueueValidation(
                    MediaTool.CheckAudioValidAsync(Mgxc.Meta.FullBgmFilePath),
                    Mgxc.Meta.FullBgmFilePath,
                    MsgKeys.Error_Invalid_audio,
                    () => Mgxc.Meta.BgmFilePath = string.Empty);
        }
        else if (name == "wvof")
        {
            Mgxc.Meta.BgmManualOffset = data.Round();
        }
        else if (name == "wvp0")
        {
            Mgxc.Meta.BgmPreviewStart = data.Round();
        }
        else if (name == "wvp1")
        {
            Mgxc.Meta.BgmPreviewStop = data.Round();
        }
        else if (name == "jack")
        {
            Mgxc.Meta.JacketFilePath = (string)data;
            if (!string.IsNullOrWhiteSpace(Mgxc.Meta.JacketFilePath))
                QueueValidation(
                    MediaTool.CheckImageValidAsync(Mgxc.Meta.FullJacketFilePath),
                    Mgxc.Meta.FullJacketFilePath,
                    MsgKeys.Error_Invalid_jk_image,
                    () => Mgxc.Meta.JacketFilePath = string.Empty);
        }
        else if (name == "bgfn")
        {
            var path = (string)data;
            Mgxc.Meta.BgiFilePath = path;
            if (!string.IsNullOrWhiteSpace(path)) Mgxc.Meta.IsCustomStage = true;
        }
        else if (name == "bgsc")
        {
            // BGSCENE
        }
        else if (name == "bgsy")
        {
            // BGSYNC
        }
        else if (name == "flcl")
        {
            // FIELDCOL
        }
        else if (name == "flcx")
        {
            var col = UmiguriParserCommon.FieldLineNameFromIndex((int)data);
            if (col != null)
                Mgxc.Meta.NotesFieldLine =
                    Assets.FieldLines.FirstOrDefault(x => x.Str == col) ?? Mgxc.Meta.NotesFieldLine;
        }
        else if (name == "flbg")
        {
            // FIELDBG
        }
        else if (name == "flsc")
        {
            // FIELDSCENE
        }
        else if (name == "mtil")
        {
            Mgxc.Meta.MainTil = (int)data;
        }
        else if (name == "mbpm")
        {
            Mgxc.Meta.MainBpm = data.Round();
        }
        else if (name == "ttrl")
        {
            // TUTORIAL
        }
        else if (name == "sofs")
        {
            Mgxc.Meta.BgmEnableBarOffset = Convert.ToBoolean((int)data);
        }
        else if (name == "uclk")
        {
            // USECLICK
        }
        else if (name == "xlng")
        {
            // EXLONG
        }
        else if (name == "bgmw")
        {
            // BGMWAITEND
        }
        else if (name == "atls")
        {
            // AUTHOR LIST
        }
        else if (name == "atst")
        {
            // AUTHOR SITES
        }
        else if (name == "durl")
        {
            // DLURL
        }
        else if (name == "lcpy")
        {
            if (data is string value)
                ParseCopyrightField(value);
        }
        else if (name == "ltyp")
        {
            // LICENSE
        }
        else if (name == "lurl")
        {
            // LICENSE URL
        }
        else if (name == "xver")
        {
            // XVER
        }
        else if (name == "cmmt")
        {
            Mgxc.Meta.Comment = (string)data;
        }
        else if (name == "CTCK")
        {
            // last cursor position?
        }
        else if (name == "LXFN")
        {
            // .ugc location?
        }
        else if (name == "HSCL")
        {
            // idk
        }
        else if (name == "\0\0\0\0")
        {
            // why
        }
        else
        {
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Unrecognized_meta, name, data);
            ReportAtPosition(Severity.Information, msg, br.BaseStream.Position);
        }
    }

    private void ParseCopyrightField(string value)
    {
        var summaryStart = FindJudgeSummaryStart(value);

        if (summaryStart < 0)
        {
            Mgxc.Meta.Copyright = value;
            return;
        }

        var summary = value[summaryStart..];

        if (summary.StartsWith(
                "GJ2:",
                StringComparison.Ordinal) &&
            !ParseJudgeSummary(summary))
        {
            Mgxc.Meta.Copyright = value;
            return;
        }

        ParseJudgeProxyBaseline(summary);
        ParseMeterDefSnapshot(summary);
        ParseSlpSnapshot(summary);
        ParseSlaSnapshot(summary);

        Mgxc.Meta.Copyright = value[..summaryStart]
            .TrimEnd('\r', '\n');
    }

    private static int FindJudgeSummaryStart(string value)
    {
        static bool IsSummaryStart(ReadOnlySpan<char> candidate) =>
            candidate.StartsWith(
                "GJ2:",
                StringComparison.Ordinal) ||
            candidate.StartsWith(
                ";GJM:",
                StringComparison.Ordinal) ||
            candidate.StartsWith(
                ";GJL:",
                StringComparison.Ordinal) ||
            candidate.StartsWith(
                ";GJS:",
                StringComparison.Ordinal);

        if (IsSummaryStart(value.AsSpan()))
            return 0;

        var searchStart = 0;

        while (searchStart < value.Length)
        {
            var newlineIndex = value.IndexOf(
                '\n',
                searchStart);

            if (newlineIndex < 0)
                break;

            var candidate = newlineIndex + 1;

            if (candidate < value.Length &&
                IsSummaryStart(value.AsSpan(candidate)))
                return candidate;

            searchStart = candidate;
        }

        return -1;
    }

    private bool ParseJudgeSummary(string value)
    {
        const string prefix = "GJ2:";
        const int fieldLength = 8;
        const int fieldCount = 6;

        if (!value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var payloadStart = prefix.Length;
        var payloadLength = fieldLength * fieldCount;

        if (value.Length < payloadStart + payloadLength)
            return false;

        var values = new int[fieldCount];

        for (var i = 0; i < fieldCount; i++)
        {
            var field = value.Substring(
                payloadStart + i * fieldLength,
                fieldLength);

            if (!uint.TryParse(
                    field,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var parsed) ||
                parsed > int.MaxValue)
                return false;

            values[i] = (int)parsed;
        }

        if ((long)values[0] +
            values[1] +
            values[2] +
            values[3] +
            values[4] != values[5])
            return false;

        Mgxc.Meta.C2sJudgeTap = values[0];
        Mgxc.Meta.C2sJudgeHld = values[1];
        Mgxc.Meta.C2sJudgeSld = values[2];
        Mgxc.Meta.C2sJudgeAir = values[3];
        Mgxc.Meta.C2sJudgeFlk = values[4];
        Mgxc.Meta.C2sJudgeAll = values[5];

        return true;
    }

    private void ParseJudgeProxyBaseline(string value)
    {
        const string judgePrefix = "GJ2:";
        const string proxyPrefix = ";GJP:";
        const int fieldLength = 8;
        const int judgeFieldCount = 6;
        const int proxyFieldCount = 3;

        var proxyStart =
            judgePrefix.Length +
            fieldLength * judgeFieldCount;

        if (value.Length < proxyStart + proxyPrefix.Length ||
            !value.AsSpan(proxyStart).StartsWith(
                proxyPrefix,
                StringComparison.Ordinal))
            return;

        var payloadStart = proxyStart + proxyPrefix.Length;
        var payloadLength = fieldLength * proxyFieldCount;

        if (value.Length < payloadStart + payloadLength)
            return;

        var values = new int?[proxyFieldCount];

        for (var i = 0; i < proxyFieldCount; i++)
        {
            var field = value.Substring(
                payloadStart + i * fieldLength,
                fieldLength);

            if (!uint.TryParse(
                    field,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var parsed))
                return;

            if (parsed == uint.MaxValue)
            {
                values[i] = null;
                continue;
            }

            if (parsed > int.MaxValue)
                return;

            values[i] = (int)parsed;
        }

        Mgxc.Meta.C2sJudgeHldProxyBaseline = values[0];
        Mgxc.Meta.C2sJudgeSldProxyBaseline = values[1];
        Mgxc.Meta.C2sJudgeAirProxyBaseline = values[2];
    }

    private void ParseMeterDefSnapshot(string value)
    {
        const string prefix = ";GJM:";
        const int fieldLength = 8;
        const int fieldCount = 2;

        var start = value.IndexOf(
            prefix,
            StringComparison.Ordinal);

        if (start < 0)
            return;

        start += prefix.Length;

        if (value.Length < start + fieldLength * fieldCount)
            return;

        var values = new int[fieldCount];

        for (var i = 0; i < fieldCount; i++)
        {
            var field = value.Substring(
                start + i * fieldLength,
                fieldLength);

            if (!uint.TryParse(
                    field,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var parsed) ||
                parsed > int.MaxValue)
                return;

            values[i] = (int)parsed;
        }

        Mgxc.Meta.C2sMeterDefDenominator = values[0];
        Mgxc.Meta.C2sMeterDefNumerator = values[1];
    }

    private void ParseSlpSnapshot(string value)
    {
        const string prefix = ";GJL:";
        const string slaPrefix = ";GJS:";
        const string suffix = ";PENGUINTOOLS_T_JUDGE_TAP=";

        var start = value.IndexOf(
            prefix,
            StringComparison.Ordinal);

        if (start < 0)
            return;

        start += prefix.Length;

        var end = value.IndexOf(
            slaPrefix,
            start,
            StringComparison.Ordinal);

        if (end < 0)
        {
            end = value.IndexOf(
                suffix,
                start,
                StringComparison.Ordinal);
        }

        if (end < 0)
            end = value.Length;

        Mgxc.Meta.C2sSlpSnapshot =
            value[start..end];
    }

    private void ParseSlaSnapshot(string value)
    {
        const string prefix = ";GJS:";
        const string suffix = ";PENGUINTOOLS_T_JUDGE_TAP=";

        var start = value.IndexOf(
            prefix,
            StringComparison.Ordinal);

        if (start < 0)
            return;

        start += prefix.Length;

        var end = value.IndexOf(
            suffix,
            start,
            StringComparison.Ordinal);

        if (end < 0)
            end = value.Length;

        Mgxc.Meta.C2sSlaSnapshot =
            value[start..end];
    }
}
