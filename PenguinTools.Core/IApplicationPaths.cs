namespace PenguinTools.Core;

/// <summary>
///     Application-managed filesystem locations (scratch temp and durable user data).
/// </summary>
public interface IApplicationPaths
{
    /// <summary>Directory for short-lived working files.</summary>
    string TempWorkPath { get; }

    /// <summary>Directory for per-user durable files (e.g. collected asset JSON).</summary>
    string UserDataPath { get; }
}