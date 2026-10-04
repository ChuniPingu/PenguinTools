using PenguinTools.Core.Diagnostic;

using C2sModel = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart.Writer.c2s;

public sealed record C2SWriteRequest(string OutPath, C2sModel.Chart Chart, ITickFormatter? TimeCalculator = null);
