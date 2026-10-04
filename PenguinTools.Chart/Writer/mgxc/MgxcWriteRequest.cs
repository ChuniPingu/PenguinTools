
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Writer.mgxc;

public sealed record MgxcWriteRequest(string Path, UmgrModel.Chart Chart);
