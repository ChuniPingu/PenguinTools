namespace PenguinTools.Image;

/// <summary>A failed texconv invocation, including the original command and diagnostics.</summary>
public sealed class TexconvException(string command, int exitCode, string standardOutput, string standardError)
    : Exception($"texconv failed ({exitCode}): {standardError.Trim()}")
{
    public string Command { get; } = command;
    public int ExitCode { get; } = exitCode;
    public string StandardOutput { get; } = standardOutput.Trim();
    public string StandardError { get; } = standardError.Trim();
}
