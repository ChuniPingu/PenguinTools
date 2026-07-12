namespace PenguinTools.Core;

/// <summary>
///     Application-managed filesystem locations (scratch temp).
/// </summary>
public interface IApplicationPaths
{
    /// <summary>Directory for short-lived working files.</summary>
    string TempWorkPath { get; }
}
