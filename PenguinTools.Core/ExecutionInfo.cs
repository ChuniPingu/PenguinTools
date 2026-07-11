namespace PenguinTools.Core;

public sealed record ExecutionInfo(
    string ApplicationName,
    string Version,
    DateTime? BuildDateUtc,
    string BaseDirectory,
    string TempWorkPath,
    string UserDataPath,
    string InfrastructureAssetsPath,
    string PlusAssetsPath);