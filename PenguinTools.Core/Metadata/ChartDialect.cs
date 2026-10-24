namespace PenguinTools.Core.Metadata;

public enum ChartDialect
{
    Auto = 0,
    Original = 1,
    Extended = 2,
    // Retain the original API/CLI names and serialized values for existing callers.
    Margrete180 = Original,
    Margrete232 = Extended
}
