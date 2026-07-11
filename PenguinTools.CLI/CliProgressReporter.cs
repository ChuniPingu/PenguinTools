namespace PenguinTools.CLI;

internal sealed class CliProgressReporter(string operation) : IProgress<ProgressReport>
{
    public void Report(ProgressReport value)
    {
        CliOutput.WriteProgress(operation, value);
    }
}