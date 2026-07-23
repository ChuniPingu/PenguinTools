/*
   This model is based on the original implementation from:
   https://github.com/inonote/MargreteOnline
*/

namespace PenguinTools.Chart.Models.umgr;

public class Tap : PositiveNote;

public class ExTap : PositiveNote
{
    public ExEffect Effect { get; set; } = ExEffect.UP;
}

public class Flick : PositiveNote;

public class Damage : PositiveNote;

public class Slide : ExTapableNote
{
    public override ExEffect? Effect { get; set; }

    public SlideJoint AsChild()
    {
        var child = new SlideJoint
        {
            Timeline = Timeline,
            Tick = Tick,
            Lane = Lane,
            Width = Width,
            Joint = Joint.C
        };
        child.MakeVirtual(this);
        return child;
    }
}

public class SlideJoint : PositiveNote
{
    public Joint Joint { get; set; } = Joint.D;
}

public class Hold : ExTapableNote
{
    public override ExEffect? Effect { get; set; }
}

public class HoldJoint : PositiveNote
{
    public override int Lane
    {
        get => (Parent as Hold)?.Lane ?? 0;
        set
        {
            /* Do nothing */
        }
    }

    public override int Width
    {
        get => (Parent as Hold)?.Width ?? 1;
        set
        {
            // do nothing
        }
    }
}

public class Air : NegativeNote
{
    public AirDirection Direction { get; set; } = AirDirection.IR;
    public Color Color { get; set; }

    public override int Lane
    {
        get => PairNote?.Lane ?? 0;
        set
        {
            // do nothing
        }
    }

    public override int Width
    {
        get => PairNote?.Width ?? 1;
        set
        {
            // do nothing
        }
    }

    public override Time Tick
    {
        get => PairNote?.Tick ?? 0;
        set
        {
            // do nothing
        }
    }
}

public class SoflanArea : Note;

public class SoflanAreaJoint : Note
{
    public override int Lane
    {
        get => (Parent as SoflanArea)?.Lane ?? 0;
        set
        {
            /* Do nothing */
        }
    }

    public override int Width
    {
        get => (Parent as SoflanArea)?.Width ?? 1;
        set
        {
            // do nothing
        }
    }
}

public class AirSlide : NegativeNote
{
    public Color Color { get; set; }
    public decimal Height { get; set; }

    public override int Lane
    {
        get => PairNote?.Lane ?? 0;
        set
        {
            // do nothing
        }
    }

    public override int Width
    {
        get => PairNote?.Width ?? 1;
        set
        {
            // do nothing
        }
    }

    public override Time Tick
    {
        get => PairNote?.Tick ?? 0;
        set
        {
            // do nothing
        }
    }

    public AirSlideJoint AsChild()
    {
        var child = new AirSlideJoint
        {
            Timeline = Timeline,
            Tick = Tick,
            Lane = Lane,
            Width = Width,
            Joint = Joint.C,
            Height = Height
        };
        child.MakeVirtual(this);
        return child;
    }
}

public class AirSlideJoint : Note
{
    public Joint Joint { get; set; } = Joint.D;
    public decimal Height { get; set; }
}

public class AirHold : NegativeNote
{
    public Color Color { get; set; }

    public override int Lane
    {
        get => PairNote?.Lane ?? 0;
        set
        {
            // do nothing
        }
    }

    public override int Width
    {
        get => PairNote?.Width ?? 1;
        set
        {
            // do nothing
        }
    }

    public override Time Tick
    {
        get => PairNote?.Tick ?? 0;
        set
        {
            // do nothing
        }
    }

    public AirHoldJoint AsChild()
    {
        var child = new AirHoldJoint
        {
            Timeline = Timeline,
            Tick = Tick,
            Joint = Joint.C
        };
        child.MakeVirtual(this);
        return child;
    }
}

public class AirHoldJoint : Note
{
    public Joint Joint { get; set; } = Joint.D;

    public override int Lane
    {
        get => (Parent as AirHold)?.Lane ?? 0;
        set
        {
            /* Do nothing */
        }
    }

    public override int Width
    {
        get => (Parent as AirHold)?.Width ?? 1;
        set
        {
            // do nothing
        }
    }
}

public class AirCrash : Note
{
    public decimal Height { get; set; }
    public Color Color { get; set; }
    public Time Density { get; set; }

    public AirCrashJoint AsChild()
    {
        var child = new AirCrashJoint
        {
            Timeline = Timeline,
            Tick = Tick,
            Lane = Lane,
            Width = Width,
            Height = Height
        };
        child.MakeVirtual(this);
        return child;
    }
}

public class AirCrashJoint : Note
{
    public Time Density => (Parent as AirCrash)?.Density ?? 0;
    public decimal Height { get; set; }
}