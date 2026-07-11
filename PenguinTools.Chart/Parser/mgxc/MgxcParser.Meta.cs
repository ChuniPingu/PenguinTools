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
            // COPYRIGHT
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
}