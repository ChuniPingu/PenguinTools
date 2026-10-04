using PenguinTools.Core.Metadata;

namespace PenguinTools.Workflow;

public sealed record ChartDiagnosticTarget(
    int? SongId,
    string MgxcId,
    string Title,
    Difficulty Difficulty,
    string Designer,
    string FilePath,
    bool IsMain)
{
    public static ChartDiagnosticTarget FromMeta(Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        return new ChartDiagnosticTarget(
            meta.Id,
            meta.MgxcId,
            meta.Title,
            meta.Difficulty,
            meta.Designer,
            meta.FilePath,
            meta.IsMain);
    }
}
