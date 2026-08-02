namespace PenguinTools.Chart.Writer.mgxc;

using umgr = Models.umgr;

public sealed record MgxcWriteRequest(string Path, umgr.Chart Chart);
