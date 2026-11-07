using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.IO;
using PenguinTools.Core.Metadata;

using C2sModel = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart.Writer.c2s;

public partial class C2SChartWriter
{
    public C2SChartWriter(C2SWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutPath);
        ArgumentNullException.ThrowIfNull(request.Chart);

        OutPath = request.OutPath;
        Chart = request.Chart;
        Diagnostic.TimeCalculator = request.TimeCalculator;
    }

    private DiagnosticCollector Diagnostic { get; } = new();
    private string OutPath { get; }
    private C2sModel.Chart Chart { get; }
    private int Resolution { get; set; } = 384;

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            NoteWidthRules.Validate(Chart);
            Resolution = Chart.Extras.SerializationResolution(Chart);
            var sb = new StringBuilder();
            AppendHeaders(sb);

            AppendFormattedEvents(sb);
            sb.AppendLine();
            if (!AppendFormattedNotes(sb))
            {
                return OperationResult.Failure().WithDiagnostics(Diagnostic);
            }

            var text = FinalizeChartText(sb.ToString());
            text = FormatForGame(text);
            await AtomicFile.WriteAsync(OutPath, async (stream, token) =>
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false, true), leaveOpen: true);
                await writer.WriteAsync(text.AsMemory(), token);
                await writer.FlushAsync(token);
            }, ct);
            return OperationResult.Success().WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.Report(ex.ToDiagnostic().WithPathFallback(OutPath));
            return OperationResult.Failure().WithDiagnostics(Diagnostic);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private void AppendHeaders(StringBuilder sb)
    {
        var version = "1.15.00";
        var bpms = Chart.Events.OfType<C2sModel.Bpm>().Select(x => x.Value).ToArray();
        var fallbackBpm = Chart.Meta.BgmInitialBpm > 0 ? Chart.Meta.BgmInitialBpm : bpms.FirstOrDefault(120m);
        var mainBpm = Chart.Meta.MainBpm > 0 ? Chart.Meta.MainBpm : fallbackBpm;
        var maxBpm = bpms.Length > 0 ? bpms.Max() : mainBpm;
        var minBpm = bpms.Length > 0 ? bpms.Min() : mainBpm;

        sb.AppendLine(CultureInfo.InvariantCulture, $"VERSION\t{version}\t{version}");
        // Song selection and difficulty are supplied by the game's XML.
        sb.AppendLine("MUSIC\t0");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine("DIFFICULT\t0");
        sb.AppendLine("LEVEL\t0.0");
        sb.AppendLine(CultureInfo.InvariantCulture, $"CREATOR\t{Chart.Meta.Designer}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"BPM_DEF\t{C2sNumber.Bpm(bpms.FirstOrDefault(mainBpm))}\t{C2sNumber.Bpm(mainBpm)}\t{C2sNumber.Bpm(maxBpm)}\t{C2sNumber.Bpm(minBpm)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"MET_DEF\t{Chart.Meta.BgmInitialDenominator}\t{Chart.Meta.BgmInitialNumerator}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RESOLUTION\t{Resolution}");
        var clock = Chart.Extras.ClockTicks(Chart) * Resolution / 1920;
        sb.AppendLine(CultureInfo.InvariantCulture, $"CLK_DEF\t{clock}");
        sb.AppendLine(Chart.Extras.Headers.GetValueOrDefault("PROGJUDGE_BPM", "PROGJUDGE_BPM\t240.000"));
        sb.AppendLine(Chart.Extras.Headers.GetValueOrDefault("PROGJUDGE_AER", "PROGJUDGE_AER\t  0.999"));
        sb.AppendLine(CultureInfo.InvariantCulture, $"TUTORIAL\t{(Chart.Extras.Tutorial ? 1 : 0)}");
        if (Chart.Extras.PrerollBar1Applied)
        {
            sb.AppendLine("//" + ChartExtras.PrerollBar1Marker);
        }
        foreach (var (tag, value) in Chart.Extras.Headers)
        {
            if (tag is not ("CLK_DEF" or "SEQUENCEID" or "PROGJUDGE_BPM" or "PROGJUDGE_AER" or "TUTORIAL" or "VERSION" or "MUSIC" or "DIFFICULT" or "LEVEL" or "CREATOR" or "BPM_DEF" or "MET_DEF"))
            {
                sb.AppendLine(value);
            }
        }

        sb.AppendLine();

    }

    private string RestoreSourceHeaders(string text)
    {
        if (Chart.Extras.SourceMetaKey == ChartExtras.MetaKey(Chart))
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var tag = lines[i].Split('\t')[0];
                if (tag is ("CREATOR" or "BPM_DEF" or "MET_DEF") &&
                    Chart.Extras.Headers.TryGetValue(tag, out var original))
                {
                    lines[i] = original;
                }
            }
            text = string.Join('\n', lines);
        }
        return text;
    }

    private string FinalizeChartText(string text)
    {
        text = RestoreSourceHeaders(text);
        text = Chart.Extras.RestoreSourceOrder(text, Resolution);
        var unchanged = Chart.Extras.SourceKey == GetContentKey(Chart);
        if (unchanged && Chart.Extras.SourceStatistics.Count > 0)
        {
            text += "\n" + string.Join("\n", Chart.Extras.SourceStatistics) + "\n";
        }

        if (Chart.Extras.SourceSnapshot.Length > 0 &&
            (Chart.Extras.SourceKey is null || !Chart.Extras.BinarySnapshotValid || !Chart.Extras.HasSpeedSnapshot))
        {
            Chart.Extras.Resolution = Resolution;
            text = Chart.Extras.FinalizeText(Chart, text);
        }
        return text;
    }

    internal static string GetContentKey(C2sModel.Chart chart)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var writer = new C2SChartWriter(new C2SWriteRequest("snapshot", chart))
            {
                Resolution = 1920
            };
            var sb = new StringBuilder();
            writer.AppendFormattedEvents(sb);
            if (!writer.AppendFormattedNotes(sb))
            {
                return "invalid";
            }

            sb.AppendLine(chart.Extras.Headers.GetValueOrDefault("PROGJUDGE_BPM", "PROGJUDGE_BPM\t240.000"));
            sb.AppendLine(CultureInfo.InvariantCulture, $"TUTORIAL\t{(chart.Extras.Tutorial ? 1 : 0)}");
            var records = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')).Order(StringComparer.Ordinal);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', records))));
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static readonly string[] EventOrder = ["BPM", "MET", "SLP", "SFL", "DCM", "STP"];

    private void AppendFormattedEvents(StringBuilder sb)
    {
        foreach (var e in Chart.Events.OrderBy(e => Array.IndexOf(EventOrder, e.Id)).ThenBy(e => e.Tick.Original))
        {
            sb.AppendLine(Format(e));
        }

        foreach (var tick in Chart.Extras.ClickTicks.Order())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"CLK\t{tick / 1920}\t{Scale(tick % 1920)}");
        }
    }

    private List<C2sModel.Note> OrderedNotesForWrite()
    {
        // Ground SLIDE statement order defines the paths. The scheduler inserts
        // AIR after its attachment point and before any ground continuation.
        var notes = Chart.Notes.OfType<C2sModel.Slide>().Cast<C2sModel.Note>().Concat(Chart.Notes
            .Where(note => note is not C2sModel.Slide))
            .ToList();

        return C2sAirScheduler.Schedule(notes);
    }

    private bool AppendFormattedNotes(StringBuilder sb)
    {
        var hasError = false;
        foreach (var n in OrderedNotesForWrite())
        {
            if (TryFormat(n, out var line, out var error) && error is null)
            {
                sb.AppendLine(line);
                continue;
            }

            Diagnostic.Report(new TimedDiagnostic(Severity.Error, error!, n.Tick.Original)
            {
                Target = n
            });
            hasError = true;
        }

        return !hasError;
    }

}
