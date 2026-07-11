using System.Xml.Linq;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.ugc;
using PenguinTools.Core;
using PenguinTools.Core.Metadata;
using PenguinTools.Media;

namespace PenguinTools.Application;

public sealed partial class PenguinToolsApplication
{
    public async Task<OperationResult<MusicExtractResult>> ExtractMusicAsync(
        MusicExtractRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await GuardAsync(async () =>
        {
            var xmlPath = FullPath(request.MusicXmlPath);
            var output = FullPath(request.OutputDirectory);
            if (!File.Exists(xmlPath))
                return ApplicationDiagnostics.Failure<MusicExtractResult>(Msg.Key(MsgKeys.Error_File_not_found), xmlPath);

            var document = await LoadXmlAsync(xmlPath, cancellationToken);
            var root = document.Root ?? throw new InvalidDataException("Music.xml has no root element.");
            var songId = EntryId(root.Element("name"));
            var title = EntryString(root.Element("name"));
            var artist = EntryString(root.Element("artistName"));
            var cueId = EntryId(root.Element("cueFileName"));
            var cueName = EntryString(root.Element("cueFileName"));
            var starDifficulty = Int(root.Element("starDifType"));
            var xmlDirectory = Path.GetDirectoryName(xmlPath)!;
            var outputParent = Path.GetDirectoryName(output) ?? output;
            Directory.CreateDirectory(outputParent);
            var stage = Path.Combine(outputParent, $".PenguinTools-reverse-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stage);
            var artifacts = new List<ApplicationArtifact>();
            var chartSummaries = new List<MusicExtractChartSummary>();
            try
            {
                var fumenRows = root.Element("fumens")?.Elements("MusicFumenData")
                    .Where(x => Bool(x.Element("enable"))).ToArray() ?? [];
                if (fumenRows.Length == 0) throw new InvalidDataException("Music.xml contains no enabled fumens.");

                CriExtractResult? audio = null;
                string? musicWav = null;
                if (!request.NoAudio)
                {
                    var (acb, awb) = DiscoverCri(request, xmlPath, cueId, cueName);
                    var source = acb ?? awb ?? throw new FileNotFoundException("Required ACB/AWB media was not found.");
                    audio = await _dependencies.MediaTool.ExtractCriAudioAsync(
                        new CriExtractOptions(source, Path.Combine(stage, "audio"), acb is not null ? awb : acb,
                            request.HcaKey), cancellationToken);
                    var selected = SelectCue(audio.Cues, cueName);
                    musicWav = Path.Combine(stage, "music.wav");
                    File.Copy(selected.WavPath, musicWav, true);
                }

                string? jacketPng = null;
                if (!request.NoJacket)
                {
                    var jacket = OptionalFullPath(request.JacketPath) ?? ResolveRelative(xmlDirectory,
                        root.Element("jaketFile")?.Element("path")?.Value);
                    if (jacket is null || !File.Exists(jacket))
                        throw new FileNotFoundException("Required jacket media was not found.", jacket);
                    jacketPng = Path.Combine(stage, "jacket.png");
                    if (Path.GetExtension(jacket).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                        await _dependencies.MediaTool.DecodeDdsAsync(jacket, jacketPng, cancellationToken);
                    else File.Copy(jacket, jacketPng, true);
                }

                foreach (var row in fumenRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var difficultyId = EntryId(row.Element("type"));
                    var chartPath = ResolveRelative(xmlDirectory, row.Element("file")?.Element("path")?.Value)
                                    ?? throw new InvalidDataException("Enabled fumen has no file path.");
                    var parsed = await new C2SParser(new C2SParseRequest(chartPath)).ParseAsync(cancellationToken);
                    if (!parsed.Succeeded || parsed.Value is null)
                        return OperationResult<MusicExtractResult>.Failure().WithDiagnostics(parsed.Diagnostics);
                    var chart = parsed.Value;
                    chart.Meta.Id = songId;
                    chart.Meta.MgxcId = songId.ToString();
                    chart.Meta.Title = title;
                    chart.Meta.Artist = artist;
                    chart.Meta.Difficulty = DifficultyFromMusicId(difficultyId);
                    if (chart.Meta.Difficulty == Difficulty.WorldsEnd &&
                        Enum.IsDefined((StarDifficulty)starDifficulty))
                        chart.Meta.WeDifficulty = (StarDifficulty)starDifficulty;
                    chart.Meta.Level = Int(row.Element("level")) + Int(row.Element("levelDecimal")) / 100m;
                    chart.Meta.Designer = Value(row.Element("notesDesigner"), chart.Meta.Designer);
                    var xmlBpm = Decimal(row.Element("defaultBpm"));
                    if (xmlBpm > 0) chart.Meta.MainBpm = xmlBpm;
                    chart.Meta.BgmFilePath = musicWav is null ? string.Empty : "music.wav";
                    chart.Meta.JacketFilePath = jacketPng is null ? string.Empty : "jacket.png";
                    if (audio is not null)
                    {
                        var cue = SelectCue(audio.Cues, cueName);
                        chart.Meta.BgmPreviewStart = (cue.PreviewStartMs ?? 0) / 1000m;
                        chart.Meta.BgmPreviewStop = (cue.PreviewStopMs ?? 0) / 1000m;
                    }
                    var converted = new UgcChartConverter(new UgcConvertRequest(chart)).Convert();
                    if (!converted.Succeeded || converted.Value is null)
                        return OperationResult<MusicExtractResult>.Failure().WithDiagnostics(converted.Diagnostics);
                    var filename = $"{songId}_{difficultyId}.ugc";
                    var staged = Path.Combine(stage, filename);
                    await new UgcChartWriter(new UgcWriteRequest(staged, converted.Value)).WriteAsync(cancellationToken);
                    chartSummaries.Add(new MusicExtractChartSummary(songId, difficultyId,
                        chart.Meta.Difficulty.ToString(), chart.Meta.Level, chart.Meta.Designer, chart.Meta.MainBpm,
                        Path.Combine(output, filename)));
                }

                Directory.CreateDirectory(output);
                foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.TopDirectoryOnly))
                {
                    var destination = Path.Combine(output, Path.GetFileName(file));
                    File.Move(file, destination, true);
                    var kind = Path.GetExtension(file).ToLowerInvariant() switch
                    {
                        ".ugc" => "chart.ugc", ".wav" => "audio.wav", ".png" => "jacket.png", _ => "music.file"
                    };
                    artifacts.Add(new ApplicationArtifact(kind, destination));
                }
                progress?.Report(new ProgressReport(Msg.Key(MsgKeys.Progress_Phase_writing), Item: title,
                    Completed: chartSummaries.Count, Total: chartSummaries.Count));
                return OperationResult<MusicExtractResult>.Success(new MusicExtractResult(
                    xmlPath, output, songId, title, artist, chartSummaries, artifacts));
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
            }
        });
    }

    private static async Task<XDocument> LoadXmlAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await XDocument.LoadAsync(stream, LoadOptions.None, ct);
    }

    private static CriCue SelectCue(IReadOnlyList<CriCue> cues, string cueName)
    {
        var named = cues.Where(x => string.Equals(x.Name, cueName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (named.Length == 1) return named[0];
        var zero = cues.Where(x => x.CueId == 0).ToArray();
        if (zero.Length == 1) return zero[0];
        throw new InvalidDataException("Audio cue selection is ambiguous.");
    }

    private static (string? Acb, string? Awb) DiscoverCri(
        MusicExtractRequest request, string xmlPath, int cueId, string cueName)
    {
        var acb = OptionalFullPath(request.AcbPath);
        var awb = OptionalFullPath(request.AwbPath);
        if (acb is not null || awb is not null) return (acb, awb);
        var root = Directory.GetParent(Path.GetDirectoryName(xmlPath)!)?.Parent?.FullName ?? Path.GetDirectoryName(xmlPath)!;
        var expectedDirectory = Path.Combine(root, "cueFile", $"cueFile{cueId:000000}");
        var dirs = Directory.Exists(expectedDirectory)
            ? [expectedDirectory]
            : Directory.EnumerateDirectories(root, $"cueFile{cueId:000000}", SearchOption.AllDirectories).ToArray();
        if (dirs.Length != 1) return (null, null);
        var expectedAcb = Path.Combine(dirs[0], $"{cueName}.acb");
        acb = File.Exists(expectedAcb) ? expectedAcb
            : Directory.EnumerateFiles(dirs[0], "*.acb").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        var expectedAwb = Path.Combine(dirs[0], $"{cueName}.awb");
        awb = File.Exists(expectedAwb) ? expectedAwb
            : acb is null ? Directory.EnumerateFiles(dirs[0], "*.awb").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault()
                : Path.ChangeExtension(acb, ".awb");
        if (awb is not null && !File.Exists(awb)) awb = null;
        return (acb, awb);
    }

    private static string? ResolveRelative(string root, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));
    private static int EntryId(XElement? value) => Int(value?.Element("id"));
    private static string EntryString(XElement? value) => value?.Element("str")?.Value ?? string.Empty;
    private static bool Bool(XElement? value) => bool.TryParse(value?.Value, out var result) && result;
    private static int Int(XElement? value) => int.TryParse(value?.Value, out var result) ? result : 0;
    private static decimal Decimal(XElement? value) => decimal.TryParse(value?.Value, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static string Value(XElement? value, string fallback) => string.IsNullOrWhiteSpace(value?.Value) ? fallback : value.Value;
    private static Difficulty DifficultyFromMusicId(int value) => value switch
    {
        0 => Difficulty.Basic, 1 => Difficulty.Advanced, 2 => Difficulty.Expert,
        3 => Difficulty.Master, 4 => Difficulty.Ultima, 5 => Difficulty.WorldsEnd,
        _ => Difficulty.Master
    };
}
