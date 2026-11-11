using System.Globalization;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;

namespace PenguinTools.Chart.Parser.ugc;

public partial class UgcParser
{
    private readonly List<string> _commentLines = [];
    private void DispatchHeaderLine(string line)
    {
        var tokens = line.Split('\t');
        if (tokens.Length == 0)
        {
            return;
        }

        var name = tokens[0].TrimStart('@').ToUpperInvariant();
        var args = tokens.Skip(1).ToArray();

        switch (name)
        {
            case "VER":
                HandleVer(args);
                break;
            case "EXVER":
                HandleExVer(args);
                break;
            case "TICKS":
                HandleTicks(args);
                break;
            case "ENDHEAD":
                break;

            case "TITLE":
                Ugc.Meta.Title = Str(args);
                break;
            case "SORT":
                Ugc.Meta.SortName = Str(args);
                break;
            case "ARTIST":
                Ugc.Meta.Artist = Str(args);
                break;
            case "GENRE":
                HandleGenre(args);
                break;
            case "DESIGN":
                Ugc.Meta.Designer = Str(args);
                break;
            case "DIFF":
                HandleDiff(args);
                break;
            case "LEVEL":
                HandleLevel(args);
                break;
            case "CONST":
                HandleConst(args);
                break;
            case "SONGID":
                HandleSongId(args);
                break;
            case "RLDATE":
                HandleReleaseDate(args);
                break;

            case "BGM":
                HandleBgm(args);
                break;
            case "BGMOFS":
                HandleBgmOffset(args);
                break;
            case "BGMPRV":
                HandleBgmPreview(args);
                break;
            case "JACKET":
                HandleJacket(args);
                break;
            case "BGIMG":
                HandleBgImg(args);
                break;
            case "BGMODE":
                HandleBackgroundMode(args);
                break;
            case "FLDCOL":
                HandleFldCol(args);
                break;
            case "FLDIMG":
                Ugc.Meta.FieldImage = Str(args);
                break;
            case "FLDSCENE":
                Ugc.Meta.FieldScene = Str(args);
                break;
            case "MAINBPM":
                HandleMainBpm(args);
                break;
            case "FLAG":
                HandleFlag(args);
                break;
            case "CLKCNT":
                HandleClickCount(args);
                break;
            case "CMT":
                HandleComment(args);
                break;

            case "ATINFO":
                HandleAuthorInfo(args);
                break;
            case "DLURL":
                Ugc.Meta.DownloadUrl = Str(args);
                break;
            case "COPYRIGHT":
                HandleCopyright(args);
                break;
            case "LICENSE":
                HandleLicense(args);
                break;

            case "BPM":
                HandleBpm(args);
                break;
            case "BEAT":
                HandleBeat(args);
                break;
            case "SPDMOD":
                HandleSpdMod(args);
                break;
            case "SPDDEF":
                Ugc.Extras.EditorHeaders.Add(line);
                break;
            case "SPDFLD":
                Ugc.Extras.EditorHeaders.Add(line);
                break;
            case "BGSCENE":
                Ugc.Meta.BackgroundScene = Str(args);
                break;
            case "WEATTR":
                HandleWeAttribute(args);
                break;
            case "TIL":
                HandleTil(args);
                break;
            case "MAINTIL":
                HandleMainTil(args);
                break;
            case "USETIL":
                break;

            default:
                PreserveUnknownHeader(name, args, line);
                break;
        }
    }

    private void HandleComment(string[] args)
    {
        _commentLines.Add(Str(args));
        Ugc.Meta.Comment = string.Join('\n', _commentLines);
    }

    private void HandleBackgroundMode(string[] args)
    {
        Ugc.Extras.EditorBackgroundMode = Str(args);
        Ugc.Meta.BackgroundSync = args.Length < 2 || !ParseBool(args[1]);
    }

    private void HandleAuthorInfo(string[] args)
    {
        if (args.Length > 1 && args[0].Equals("AUTHORS", StringComparison.OrdinalIgnoreCase))
        {
            Ugc.Meta.Authors = string.Join('\t', args.Skip(1));
        }
        else if (args.Length > 1 && args[0].Equals("SITES", StringComparison.OrdinalIgnoreCase))
        {
            Ugc.Meta.AuthorSites = string.Join('\t', args.Skip(1));
        }
    }

    private void HandleLicense(string[] args)
    {
        Ugc.Meta.License = Str(args);
        Ugc.Meta.LicenseUrl = args.Length > 1 ? args[1] : string.Empty;
    }

    private void PreserveUnknownHeader(string name, string[] args, string line)
    {
        // C2S statistics are recomputed from notes, not carried as editor headers.
        if (!name.StartsWith("T_JUDGE_", StringComparison.Ordinal))
        {
            Ugc.Extras.EditorHeaders.Add(line);
        }
        HandleUnknownHeader(name, args);
    }

    private void HandleBgmOffset(string[] args)
    {
        if (args.Length >= 1 && decimal.TryParse(args[0], CultureInfo.InvariantCulture, out var ofs))
        {
            Ugc.Meta.BgmManualOffset = ofs;
        }
    }

    private void HandleMainBpm(string[] args)
    {
        if (args.Length >= 1 && TryParseNativeNumber(args[0], "MAINBPM", out var mbpm))
        {
            Ugc.Meta.MainBpm = mbpm;
        }
    }

    private void HandleClickCount(string[] args)
    {
        if (!int.TryParse(Str(args), out var count) || count < 0)
        {
            ReportAtCurrentLine(Severity.Warning, Msg.Create(MsgKeys.Warn_Invalid_metadata, "UGC CLKCNT", Str(args)));
            return;
        }

        Ugc.Extras.ClickCount = count;
    }

    private void WarnInvalidRoundtripMetadata() =>
        ReportAtCurrentLine(Severity.Warning, Msg.Key(MsgKeys.Warn_Roundtrip_metadata_ignored));

    private void HandleCopyright(string[] args)
    {
        Ugc.Meta.Copyright = ChartExtras.CopyrightText(Str(args));
        if (Str(args).Contains("PT_C2S_", StringComparison.Ordinal))
        {
            Ugc.Extras = ChartExtras.FromCopyright(Str(args), WarnInvalidRoundtripMetadata);
        }
        else
        {
            Ugc.Extras.ReadCopyright(Str(args), WarnInvalidRoundtripMetadata);
        }

        C2sRoundTripComment.Absorb(Ugc.Meta, Ugc.Extras.RoundTripBookmarks);
        // Explicit UGC header lines are authoritative over their retained copies.
        Ugc.Extras.EditorHeaders.Clear();
    }

    private void HandleWeAttribute(string[] args)
    {
        if (args.Length <= 0)
        {
            Log("WEATTR", args);
            return;
        }

        var attr = Assets.WeTagNames.FirstOrDefault(x => x.Str == args[0]);
        Ugc.Meta.WeTag = attr ?? new Entry(-1, args[0]);
    }

    private void HandleUnknownHeader(string name, string[] args)
    {
        if (name.StartsWith("T_JUDGE_", StringComparison.Ordinal))
        {
            return;
        }

        ReportAtCurrentLine(Severity.Warning, Msg.Create(MsgKeys.Mg_Unrecognized_meta, name, Str(args)));
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
        if (args.Length < 1)
        {
            return;
        }

        if (int.TryParse(args[0], out var v) && v > 9)
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Chart_version_too_new, "UGC", v, 9));
        }
        if (v is not (8 or 9))
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, args[0], "UGC v8/v9"));
        }
        if (_declaredVersion.HasValue && _declaredVersion.Value != v)
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, args[0], "one UGC format version"));
        }
        _declaredVersion = v;
        _version = v;
    }

    private void HandleExVer(string[] args)
    {
        if (args.Length >= 1 && int.TryParse(args[0], out var v) && v is 0 or 1)
        {
            Ugc.Meta.ExVersion = v;
            return;
        }

        Log("EXVER", args);
    }

    private void HandleTicks(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out _sourceTicks) || _sourceTicks <= 0)
        {
            ThrowAtCurrentLine(Msg.Create(MsgKeys.Error_Invalid_Header, Str(args), "positive TICKS"));
        }
    }

    private void HandleGenre(string[] args)
    {
        if (args.Length < 1)
        {
            return;
        }

        var entry = Assets.GenreNames.FirstOrDefault(x => x.Str.Equals(args[0], StringComparison.Ordinal));
        Ugc.Meta.Genre = entry ?? new Entry(-1, args[0]);
    }

    private void HandleDiff(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var d))
        {
            return;
        }

        Ugc.Meta.Difficulty = UmiguriParserCommon.DifficultyFromValue(d);
        if (Ugc.Meta.Difficulty == Difficulty.WorldsEnd)
        {
            Ugc.Meta.Stage = UmiguriParserCommon.CreateWorldsEndStage();
        }
    }

    private void HandleLevel(string[] args)
    {
        if (Ugc.Meta.Difficulty != Difficulty.WorldsEnd || args.Length < 1)
        {
            return;
        }

        var trimmed = args[0].Trim('+');
        if (!int.TryParse(trimmed, out var num))
        {
            return;
        }

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
        if (Ugc.Meta.Difficulty == Difficulty.WorldsEnd || args.Length < 1)
        {
            return;
        }

        if (decimal.TryParse(args[0], CultureInfo.InvariantCulture, out var c))
        {
            Ugc.Meta.Level = Math.Round(c, 2);
        }
    }

    private void HandleSongId(string[] args)
    {
        if (args.Length < 1)
        {
            return;
        }

        Ugc.Meta.MgxcId = args[0];
        Ugc.Meta.Id = ResourceIds.TryParseNonNegative(args[0], out var id) ? id : null;
    }

    private void HandleReleaseDate(string[] args)
    {
        if (args.Length < 1)
        {
            return;
        }

        if (DateTime.TryParseExact(args[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Ugc.Meta.ReleaseDate = date;
            Ugc.Extras.EditorHeaders.Add("@RLDATE\t" + args[0]);
        }
    }

    private void HandleBgm(string[] args)
    {
        if (args.Length < 1)
        {
            return;
        }

        Ugc.Meta.BgmFilePath = args[0];
        if (!string.IsNullOrWhiteSpace(Ugc.Meta.BgmFilePath))
        {
            QueueValidation(
                MediaValidator.CheckAudioValidAsync(Ugc.Meta.FullBgmFilePath),
                Ugc.Meta.FullBgmFilePath,
                MsgKeys.Error_Invalid_audio,
                () => Ugc.Meta.BgmFilePath = string.Empty);
        }
    }

    private void HandleBgmPreview(string[] args)
    {
        if (args.Length >= 1 && decimal.TryParse(args[0], CultureInfo.InvariantCulture, out var start))
        {
            Ugc.Meta.BgmPreviewStart = start;
        }

        if (args.Length >= 2 && decimal.TryParse(args[1], CultureInfo.InvariantCulture, out var stop))
        {
            Ugc.Meta.BgmPreviewStop = stop;
        }
    }

    private void HandleJacket(string[] args)
    {
        if (args.Length < 1)
        {
            return;
        }

        Ugc.Meta.JacketFilePath = args[0];
        if (!string.IsNullOrWhiteSpace(Ugc.Meta.JacketFilePath))
        {
            QueueValidation(
                MediaValidator.CheckImageValidAsync(Ugc.Meta.FullJacketFilePath),
                Ugc.Meta.FullJacketFilePath,
                MsgKeys.Error_Invalid_jk_image,
                () => Ugc.Meta.JacketFilePath = string.Empty);
        }
    }

    private void HandleBgImg(string[] args)
    {
        if (args.Length < 1)
        {
            return;
        }

        var path = args[0];
        Ugc.Meta.BgiFilePath = path;
        if (!string.IsNullOrWhiteSpace(path))
        {
            Ugc.Meta.IsCustomStage = true;
        }
    }

    private void HandleFldCol(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var idx))
        {
            return;
        }

        Ugc.Meta.FieldColorIndex = idx;
        var col = UmiguriParserCommon.FieldLineNameFromIndex(idx);
        if (col != null)
        {
            Ugc.Meta.NotesFieldLine = Assets.FieldLines.FirstOrDefault(x => x.Str == col) ?? Ugc.Meta.NotesFieldLine;
        }
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
            case "EXFIELD":
                Ugc.Meta.ExtendedField = boolVal;
                break;
            case "SOFFSET":
                Ugc.Meta.BgmEnableBarOffset = boolVal;
                break;
            case "CLICK":
                Ugc.Extras.ClickEnabled = boolVal;
                break;
            case "TUTORIAL":
                Ugc.Extras.Tutorial = boolVal;
                break;
            case "DIFFTTL":
                Ugc.Extras.Tutorial = boolVal;
                break;
            case "EXLONG":
                Ugc.Meta.ExLongEnabled = boolVal;
                break;
            case "BGMWCMP":
                Ugc.Meta.WaitBgmEnd = boolVal;
                break;
            case "HIPRECISION":
                _highPrecision = boolVal;
                break;
            default:
                Ugc.Extras.EditorHeaders.Add("@FLAG\t" + string.Join('\t', args));
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
