using System.Globalization;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Parser.mgxc;

public partial class MgxcParser
{
    private string _privateMetadata = "";
    private string _copyright = "";
    private void ParseMeta(BinaryReader br)
    {
        var name = br.ReadUtf8String(4);
        var data = br.ReadField();

        switch (name)
        {
            case "titl":
                Mgxc.Meta.Title = (string)data;
                break;
            case "sort":
                Mgxc.Meta.SortName = (string)data;
                break;
            case "arts":
                Mgxc.Meta.Artist = (string)data;
                break;
            case "genr":
                ApplyGenre((string)data);
                break;
            case "dsgn":
                Mgxc.Meta.Designer = (string)data;
                break;
            case "diff":
                ApplyDifficulty((int)data);
                break;
            case "plvl":
                ApplyWorldsEndLevel(data);
                break;
            case "weat":
                ApplyWorldsEndTag((string)data);
                break;
            case "cnst":
                ApplyLevel(data);
                break;
            case "sgid":
                ApplySongId((string)data);
                break;
            case "wvfn":
                ApplyBgmFile((string)data);
                break;
            case "wvof":
                Mgxc.Meta.BgmManualOffset = data.Round();
                break;
            case "wvp0":
                Mgxc.Meta.BgmPreviewStart = data.Round();
                break;
            case "wvp1":
                Mgxc.Meta.BgmPreviewStop = data.Round();
                break;
            case "jack":
                ApplyJacketFile((string)data);
                break;
            case "bgfn":
                ApplyBackgroundFile((string)data);
                break;
            case "flcx":
                ApplyFieldLine((int)data);
                break;
            case "mtil":
                Mgxc.Meta.MainTil = (int)data;
                break;
            case "mbpm":
                Mgxc.Meta.MainBpm = data.Round();
                break;
            case "ttrl":
                Mgxc.Extras.Tutorial = Convert.ToBoolean(data, CultureInfo.InvariantCulture);
                break;
            case "sofs":
                Mgxc.Meta.BgmEnableBarOffset = Convert.ToBoolean((int)data);
                break;
            case "uclk":
                Mgxc.Extras.ClickEnabled = Convert.ToBoolean(data, CultureInfo.InvariantCulture);
                break;
            case "lcpy":
                _copyright = (string)data;
                Mgxc.Meta.Copyright = ChartExtras.CopyrightText(_copyright);
                Mgxc.Extras.ReadCopyright(_copyright);
                break;
            case "ptmd":
                _privateMetadata += (string)data;
                break;
            case "cmmt":
                Mgxc.Meta.Comment = (string)data;
                break;
            case "ltyp":
                Mgxc.Meta.License = (string)data;
                break;
            case "lurl":
                Mgxc.Meta.LicenseUrl = (string)data;
                break;
            case "xlng":
                Mgxc.Meta.ExLongEnabled = Convert.ToBoolean(data, CultureInfo.InvariantCulture);
                break;
            case "xver":
                Mgxc.Meta.ExVersion = (int)data;
                break;
            case "bgmw":
                Mgxc.Meta.WaitBgmEnd = Convert.ToBoolean(data, CultureInfo.InvariantCulture);
                break;
            case "bgsc": // BGSCENE
                Mgxc.Meta.BackgroundScene = (string)data;
                break;
            case "bgsy": // BGSYNC
                Mgxc.Meta.BackgroundSync = Convert.ToBoolean(data, CultureInfo.InvariantCulture);
                break;
            case "flcl": // FIELDCOL
                Mgxc.Meta.FieldColorName = (string)data;
                break;
            case "flbg": // FIELDBG
                Mgxc.Meta.FieldImage = (string)data;
                break;
            case "flsc": // FIELDSCENE
                Mgxc.Meta.FieldScene = (string)data;
                break;
            case "atls": // AUTHOR LIST
                Mgxc.Meta.Authors = (string)data;
                break;
            case "atst": // AUTHOR SITES
                Mgxc.Meta.AuthorSites = (string)data;
                break;
            case "durl": // DLURL
                Mgxc.Meta.DownloadUrl = (string)data;
                break;
            case "CTCK": // last cursor position?
            case "LXFN": // .ugc location?
            case "HSCL": // idk
            case "\0\0\0\0": // why
                break;
            default:
                MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Unrecognized_meta, name, data);
                ReportAtPosition(Severity.Information, msg, br.BaseStream.Position);
                break;
        }
    }

    private void ApplyGenre(string genre)
    {
        var entry = Assets.GenreNames.FirstOrDefault(e => e.Str.Equals(genre, StringComparison.Ordinal));
        Mgxc.Meta.Genre = entry ?? new PenguinTools.Core.Asset.Entry(-1, genre);
    }

    private void ApplyDifficulty(int difficulty)
    {
        Mgxc.Meta.Difficulty = UmiguriParserCommon.DifficultyFromValue(difficulty);
        if (Mgxc.Meta.Difficulty == Difficulty.WorldsEnd)
        {
            Mgxc.Meta.Stage = UmiguriParserCommon.CreateWorldsEndStage();
        }
    }

    private void ApplyWorldsEndLevel(object value)
    {
        if (Mgxc.Meta.Difficulty != Difficulty.WorldsEnd)
        {
            return;
        }

        var trimmed = ((string)value).Trim('+');
        if (!int.TryParse(trimmed, out var num))
        {
            return;
        }

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

    private void ApplyWorldsEndTag(string tag)
    {
        var attr = Assets.WeTagNames.FirstOrDefault(x => x.Str == tag);
        Mgxc.Meta.WeTag = attr ?? new PenguinTools.Core.Asset.Entry(-1, tag);
    }

    private void ApplyLevel(object value)
    {
        if (Mgxc.Meta.Difficulty == Difficulty.WorldsEnd)
        {
            return;
        }

        Mgxc.Meta.Level = value.Round(2);
    }

    private void ApplySongId(string songId)
    {
        Mgxc.Meta.MgxcId = songId;
        if (int.TryParse(Mgxc.Meta.MgxcId, out var id))
        {
            Mgxc.Meta.Id = id;
        }
    }

    private void ApplyBgmFile(string path)
    {
        Mgxc.Meta.BgmFilePath = path;
        if (!string.IsNullOrWhiteSpace(Mgxc.Meta.BgmFilePath))
        {
            QueueValidation(
                MediaTool.CheckAudioValidAsync(Mgxc.Meta.FullBgmFilePath),
                Mgxc.Meta.FullBgmFilePath,
                MsgKeys.Error_Invalid_audio,
                () => Mgxc.Meta.BgmFilePath = string.Empty);
        }
    }

    private void ApplyJacketFile(string path)
    {
        Mgxc.Meta.JacketFilePath = path;
        if (!string.IsNullOrWhiteSpace(Mgxc.Meta.JacketFilePath))
        {
            QueueValidation(
                MediaTool.CheckImageValidAsync(Mgxc.Meta.FullJacketFilePath),
                Mgxc.Meta.FullJacketFilePath,
                MsgKeys.Error_Invalid_jk_image,
                () => Mgxc.Meta.JacketFilePath = string.Empty);
        }
    }

    private void ApplyBackgroundFile(string path)
    {
        Mgxc.Meta.BgiFilePath = path;
        if (!string.IsNullOrWhiteSpace(path))
        {
            Mgxc.Meta.IsCustomStage = true;
        }
    }

    private void ApplyFieldLine(int index)
    {
        Mgxc.Meta.FieldColorIndex = index;
        var col = UmiguriParserCommon.FieldLineNameFromIndex(index);
        if (col != null)
        {
            Mgxc.Meta.NotesFieldLine =
                Assets.FieldLines.FirstOrDefault(x => x.Str == col) ?? Mgxc.Meta.NotesFieldLine;
        }
    }
}
