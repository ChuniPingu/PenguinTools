using System.ComponentModel;

namespace PenguinTools.Chart.Models;

// ReSharper disable InconsistentNaming
public enum AirDirection
{
    [Description("Up")] IR,
    [Description("Up Left")] UL,
    [Description("Up Right")] UR,
    [Description("Down")] DW,
    [Description("Down Left")] DL,
    [Description("Down Right")] DR
}

public enum Color
{
    [Description("Default")] DEF,
    [Description("None")] NON,
    [Description("Pink")] PNK,
    [Description("Green")] GRN,
    [Description("Lime")] LIM,
    [Description("Red")] RED,
    [Description("Black")] BLK,
    [Description("Violet")] VLT,
    [Description("Blue")] BLU,
    [Description("Dodger Blue")] DGR,
    [Description("Aqua")] AQA,
    [Description("Cyan")] CYN,
    [Description("Yellow")] YEL,
    [Description("Orange")] ORN,
    [Description("Gray")] GRY,
    [Description("Purple")] PPL
}

public enum ExEffect
{
    [Description("Up")] UP,
    [Description("Down")] DW,
    [Description("Center")] CE,
    [Description("Left")] LC,
    [Description("Right")] RC,
    [Description("Rotate Left")] LS,
    [Description("Rotate Right")] RS,
    [Description("InOut")] BS
}

public enum Joint
{
    [Description("Control")] C,
    [Description("Step")] D
}

public enum AirLadderAttr
{
    [Description("Default")] DEF,
    [Description("Axis Y")] AxisY,
    [Description("Axis Z")] AxisZ,
    [Description("Trace")] Trace
}