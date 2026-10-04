
using C2sModel = PenguinTools.Chart.Models.c2s;

namespace PenguinTools.Chart.Converter.ugc;

public sealed record UgcConvertRequest(C2sModel.Chart C2s, bool DebugTil = false);
