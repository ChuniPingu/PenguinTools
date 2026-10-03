/*
   This model is based on the original implementation from:
   https://margrithm.girlsband.party/
*/

namespace PenguinTools.Chart.Models.c2s;

public abstract class Event : Node;

public class Bpm : Event
{
    public decimal Value { get; set; }
    public override string Id => "BPM";
}

public class Met : Event
{
    public int Numerator { get; set; }
    public int Denominator { get; set; }
    public override string Id => "MET";
}

public abstract class SpeedEventBase : Event
{
    public decimal Speed { get; set; }
    public Time Length { get; set; }
}

public class Slp : SpeedEventBase
{
    public virtual int Timeline { get; set; } = -1;
    public override string Id => "SLP";
}

// Legacy SFL remains supported for lossless reading and writing of older charts.
// New charts should use SLP; this model is retained for format compatibility.
public class Sfl : SpeedEventBase
{
    public override string Id => "SFL";
}

public class Dcm : SpeedEventBase
{
    public override string Id => "DCM";
}

public class Stop : SpeedEventBase
{
    public override string Id => "STP";
}
