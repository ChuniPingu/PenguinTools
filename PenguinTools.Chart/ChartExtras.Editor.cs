using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

public sealed record EditorBookmark(string Id, int Tick, string Tag, string Rgb);

public sealed partial class ChartExtras
{
    public string? EditorCommentText { get; set; }
    public string? EditorCopyrightText { get; set; }

    internal static string CleanEditorText(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    internal void CaptureUserText(PenguinTools.Core.Metadata.Meta meta)
    {
        var comment = PenguinTools.Core.Metadata.C2sRoundTripComment.Strip(meta.Comment);
        EditorCommentText = comment == CleanEditorText(comment) ? null : comment;
        EditorCopyrightText = meta.Copyright == CleanEditorText(meta.Copyright) ? null : meta.Copyright;
    }

    internal void RestoreUserText(PenguinTools.Core.Metadata.Meta meta)
    {
        if (EditorCommentText is { } comment && meta.Comment == CleanEditorText(comment))
        {
            meta.Comment = comment;
        }
        if (EditorCopyrightText is { } copyright && meta.Copyright == CleanEditorText(copyright))
        {
            meta.Copyright = copyright;
        }
    }

    internal void CaptureEditorEvents(U.Chart chart)
    {
        EditorBookmarks = chart.Events.Children.OfType<U.BookmarkEvent>()
            .Where(b => !IsExtrasBookmark(b.Tag) && !PenguinTools.Core.Metadata.C2sRoundTripComment.IsRoundTripLine(b.Tag))
            .Select(b => new EditorBookmark(b.Id, b.Tick.Original, b.Tag, b.Rgb)).ToList();
        EditorBreakingMarkers = chart.Events.Children.OfType<U.BreakingMarker>().Select(b => b.Tick.Original).ToList();
    }

    internal void RestoreEditorEvents(U.Chart chart)
    {
        foreach (var b in EditorBookmarks)
        {
            chart.Events.AppendChild(new U.BookmarkEvent { Id = b.Id, Tick = b.Tick, Tag = b.Tag, Rgb = b.Rgb });
        }
        foreach (var tick in EditorBreakingMarkers)
        {
            chart.Events.AppendChild(new U.BreakingMarker { Tick = tick });
        }
    }

    internal void RestoreEditorMetadata(PenguinTools.Core.Metadata.Meta meta)
    {
        var release = EditorHeaders.LastOrDefault(h => h.StartsWith("@RLDATE\t", StringComparison.Ordinal));
        if (release is not null && DateTime.TryParseExact(release[8..], "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
        {
            meta.ReleaseDate = date;
        }
    }
}
