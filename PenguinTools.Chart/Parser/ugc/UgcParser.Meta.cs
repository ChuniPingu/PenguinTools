using System.Globalization;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Parser.ugc;

public partial class UgcParser
{
    private void DispatchHeaderLine(string line)
    {
        var tokens = line.Split('\t');
        if (tokens.Length == 0) return;
        var name = tokens[0].TrimStart('@').ToUpperInvariant();
        var args = tokens.Skip(1).ToArray();

        switch (name)
        {
            case "VER": HandleVer(args); break;
            case "EXVER": HandleExVer(args); break;
            case "TICKS": HandleTicks(args); break;
            case "ENDHEAD": break;

            case "TITLE": Ugc.Meta.Title = Str(args); break;
            case "SORT": Ugc.Meta.SortName = Str(args); break;
            case "ARTIST": Ugc.Meta.Artist = Str(args); break;
            case "GENRE": HandleGenre(args); break;
            case "DESIGN": Ugc.Meta.Designer = Str(args); break;
            case "DIFF": HandleDiff(args); break;
            case "LEVEL": HandleLevel(args); break;
            case "CONST": HandleConst(args); break;
            case "SONGID": HandleSongId(args); break;
            case "RLDATE": HandleReleaseDate(args); break;

            case "BGM": HandleBgm(args); break;
            case "BGMOFS":
                if (args.Length >= 1 && decimal.TryParse(args[0], CultureInfo.InvariantCulture, out var ofs))
                    Ugc.Meta.BgmManualOffset = ofs;
                break;
            case "BGMPRV": HandleBgmPreview(args); break;
            case "JACKET": HandleJacket(args); break;
            case "BGIMG": HandleBgImg(args); break;
            case "BGMODE": break;
            case "FLDCOL": HandleFldCol(args); break;
            case "FLDIMG": break;
            case "FLDSCENE": break;
            case "MAINBPM":
                if (args.Length >= 1 && decimal.TryParse(args[0], CultureInfo.InvariantCulture, out var mbpm))
                    Ugc.Meta.MainBpm = mbpm;
                break;

            case "FLAG": HandleFlag(args); break;
            case "CLKCNT": break;
            case "CMT": Ugc.Meta.Comment = Str(args); break;

            case "ATINFO":
            case "DLURL":
            case "COPYRIGHT":
            case "LICENSE": break;

            case "BPM": HandleBpm(args); break;
            case "BEAT": HandleBeat(args); break;
            case "SPDMOD": HandleSpdMod(args); break;
            case "SPDDEF": break;
            case "SPDFLD": break;
            case "BGSCENE": break;
            case "WEATTR":
                if (args.Length <= 0)
                {
                    Log("WEATTR", args);
                    return;
                }

                var attr = Assets.WeTagNames.FirstOrDefault(x => x.Str == args[0]);
                if (attr != null) Ugc.Meta.WeTag = attr;
                break;
            case "TIL": HandleTil(args); break;
            case "MAINTIL": HandleMainTil(args); break;
            case "USETIL": break;

            default:
                ReportAtCurrentLine(Severity.Warning,
                    Msg.Create(MsgKeys.Mg_Unrecognized_meta, name, Str(args)));
                break;
        }
    }

    private static string Str(string[] args)
    {
        return args.Length >= 1 ? args[0] : string.Empty;
    }

    private void Log(string name, string[] args)
    {
        ReportAtCurrentLine(Severity.Information,
            Msg.Create(MsgKeys.Mg_Unrecognized_meta, name, string.Join(' ', args)));
    }

    private void HandleVer(string[] args)
    {
        if (args.Length < 1) return;
        if (!int.TryParse(args[0], out var v) || v != 8)
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, args[0], "UGC v8"));
    }

    private void HandleExVer(string[] args)
    {
        if (args.Length >= 1 && int.TryParse(args[0], out var v) && v is 0 or 1) return;
        Log("EXVER", args);
    }

    private void HandleTicks(string[] args)
    {
        if (args.Length < 1) return;
        if (!int.TryParse(args[0], out var t) || t != 480)
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, args[0], "TICKS=480"));
    }

    private void HandleGenre(string[] args)
    {
        if (args.Length < 1) return;
        var entry = Assets.GenreNames.FirstOrDefault(x => x.Str.Equals(args[0], StringComparison.Ordinal));
        if (entry != null) Ugc.Meta.Genre = entry;
    }

    private void HandleDiff(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var d)) return;
        Ugc.Meta.Difficulty = UmiguriParserCommon.DifficultyFromValue(d);
        if (Ugc.Meta.Difficulty == Difficulty.WorldsEnd)
            Ugc.Meta.Stage = UmiguriParserCommon.CreateWorldsEndStage();
    }

    private void HandleLevel(string[] args)
    {
        if (Ugc.Meta.Difficulty != Difficulty.WorldsEnd || args.Length < 1) return;
        var trimmed = args[0].Trim('+');
        if (!int.TryParse(trimmed, out var num)) return;
        Ugc.Meta.WeDifficulty = num switch
        {
            1 => StarDifficulty.S1,
            2 => StarDifficulty.S2,
            3 => StarDifficulty.S3,
            4 => StarDifficulty.S4,
            5 => StarDifficulty.S5,
            _ => StarDifficulty.Na
        };
    }

    private void HandleConst(string[] args)
    {
        if (Ugc.Meta.Difficulty == Difficulty.WorldsEnd || args.Length < 1) return;
        if (decimal.TryParse(args[0], CultureInfo.InvariantCulture, out var c))
            Ugc.Meta.Level = Math.Round(c, 2);
    }

    private void HandleSongId(string[] args)
    {
        if (args.Length < 1) return;
        Ugc.Meta.MgxcId = args[0];
        if (int.TryParse(args[0], out var id)) Ugc.Meta.Id = id;
    }

    private void HandleReleaseDate(string[] args)
    {
        if (args.Length < 1) return;
        if (DateTime.TryParseExact(args[0], "yyyyMMdd", null, DateTimeStyles.None, out var date))
            Ugc.Meta.ReleaseDate = date;
    }

    private void HandleBgm(string[] args)
    {
        if (args.Length < 1) return;
        Ugc.Meta.BgmFilePath = args[0];
        if (!string.IsNullOrWhiteSpace(Ugc.Meta.BgmFilePath))
            QueueValidation(
                MediaTool.CheckAudioValidAsync(Ugc.Meta.FullBgmFilePath),
                Ugc.Meta.FullBgmFilePath,
                MsgKeys.Error_Invalid_audio,
                () => Ugc.Meta.BgmFilePath = string.Empty);
    }

    private void HandleBgmPreview(string[] args)
    {
        if (args.Length >= 1 && decimal.TryParse(args[0], CultureInfo.InvariantCulture, out var start))
            Ugc.Meta.BgmPreviewStart = start;
        if (args.Length >= 2 && decimal.TryParse(args[1], CultureInfo.InvariantCulture, out var stop))
            Ugc.Meta.BgmPreviewStop = stop;
    }

    private void HandleJacket(string[] args)
    {
        if (args.Length < 1) return;
        Ugc.Meta.JacketFilePath = args[0];
        if (!string.IsNullOrWhiteSpace(Ugc.Meta.JacketFilePath))
            QueueValidation(
                MediaTool.CheckImageValidAsync(Ugc.Meta.FullJacketFilePath),
                Ugc.Meta.FullJacketFilePath,
                MsgKeys.Error_Invalid_jk_image,
                () => Ugc.Meta.JacketFilePath = string.Empty);
    }

    private void HandleBgImg(string[] args)
    {
        if (args.Length < 1) return;
        var path = args[0];
        Ugc.Meta.BgiFilePath = path;
        if (!string.IsNullOrWhiteSpace(path)) Ugc.Meta.IsCustomStage = true;
    }

    private void HandleFldCol(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var idx) || idx < 0) return;
        var col = UmiguriParserCommon.FieldLineNameFromIndex(idx);
        if (col != null)
            Ugc.Meta.NotesFieldLine = Assets.FieldLines.FirstOrDefault(x => x.Str == col) ?? Ugc.Meta.NotesFieldLine;
    }

    private void HandleFlag(string[] args)
    {
        if (args.Length < 2)
        {
            Log("FLAG", args);
            return;
        }

        var key = args[0].ToUpperInvariant();
        var boolVal = ParseBool(args[1]);
        switch (key)
        {
            case "SOFFSET": Ugc.Meta.BgmEnableBarOffset = boolVal; break;
            case "CLICK":
            case "EXLONG":
            case "BGMWCMP":
            case "HIPRECISION":
            case "DIFFTTL":
                break;
            default:
                Log("FLAG:" + key, args);
                break;
        }
    }

    private static bool ParseBool(string s)
    {
        var v = s.ToLowerInvariant();
        return v is "true" or "1" or "yes";
    }
}