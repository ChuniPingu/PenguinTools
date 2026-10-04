namespace PenguinTools.Chart.Models.c2s;

/*
   This model is based on the original implementation from:
   https://margrithm.girlsband.party/
*/

public abstract class Node
{
    public Time Tick { get; set; }

    public abstract string Id { get; }
}

public abstract class Note : Node
{
    public int Timeline { get; set; } = -1;

    public int Lane { get; set; }
    public int Width { get; set; }
}

public abstract class LongNote : Note
{
    public Time EndTick { get; set; }
    public Time Length => EndTick.Round - Tick.Round;

    public int EndLane { get; set; }
    public int EndWidth { get; set; }
}

public abstract class LongHeightNote : LongNote
{
    public Height Height { get; set; }
    public Height EndHeight { get; set; }
}

public abstract class ExTapableLongNote : LongNote
{
    public ExEffect? Effect { get; set; }
}

public static class EffectExtension
{
    extension(ExEffect? e)
    {
        public char GetMark()
        {
            return e != null ? 'X' : 'L';
        }

        public string GetKind()
        {
            return e != null ? $"\t{e}" : "";
        }
    }
}
