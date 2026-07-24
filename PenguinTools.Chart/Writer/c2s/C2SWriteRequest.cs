using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Writer.c2s;

using c2s = Models.c2s;

public sealed record C2SWriteRequest(string OutPath, c2s.Chart Chart, ITickFormatter? TimeCalculator = null);
