namespace PenguinTools.Chart.Writer.ugc;

using umgr = Models.umgr;

public sealed record UgcWriteRequest(string Path, umgr.Chart Chart);
