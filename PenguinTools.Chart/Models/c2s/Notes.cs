/*
   This model is based on the original implementation from:
   https://margrithm.girlsband.party/
*/

namespace PenguinTools.Chart.Models.c2s;

public class Tap : Note
{
    public override string Id => "TAP";
}

public class Damage : Note
{
    public override string Id => "MNE";
}

public class Flick : Note
{
    public override string Id => "FLK";
}

public class ExTap : Note
{
    public ExEffect? Effect { get; set; }
    public override string Id => "CHR";
}

public class Hold : ExTapableLongNote
{
    public override string Id => $"H{Effect.GetMark()}D";
}

public class Sla : Note
{
    public Time Length { get; set; }
    public override string Id => "SLA";
}

public class Slide : ExTapableLongNote
{
    public Joint Joint { get; set; }
    public override string Id => $"S{Effect.GetMark()}{Joint}";
}

public interface IPairable
{
    public Note? Parent { get; set; }
}

public class Air : Note, IPairable
{
    public AirDirection Direction { get; set; }
    public Color Color { get; set; } = Color.DEF;

    public override string Id => $"A{Direction}";
    public Note? Parent { get; set; }
}

public class AirSlide : LongHeightNote, IPairable
{
    public Joint Joint { get; set; }
    public Color Color { get; set; } = Color.DEF;

    public override string Id => $"AS{Joint}";
    public Note? Parent { get; set; }
}

public class AirHold : LongNote, IPairable
{
    public Joint Joint { get; set; } = Joint.D;
    public Color Color { get; set; } = Color.DEF;

    public override string Id => Joint == Joint.C ? "AHX" : "AHD";
    public Note? Parent { get; set; }
}

public class AirCrash : LongHeightNote
{
    public Time Density { get; set; }
    public Color Color { get; set; } = Color.DEF;

    public override string Id => "ALD";
}