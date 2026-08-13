namespace PenguinTools.Chart.Parser.mgxc;

/// <summary>
/// MGXC ExTap height is unused by the game and is reserved as a round-trip role marker.
/// </summary>
internal static class MgxcExTapMarkers
{
    public const int DefaultHeight = 80;
    public const int ExplicitChr = 81;
    public const int HoldOnlyCarrier = 82;
    public const int AirActionCarrierTap = 83;
    public const int AirActionCarrierExTap = 84;
    public const int AirActionCarrierFlick = 85;
    public const int AirActionCarrierDamage = 86;
    public const int AirActionCarrierHold = 87;
    public const int AirActionCarrierSlideD = 88;
    public const int AirActionCarrierSlideC = 89;
    public const int AirActionCarrierExHold = 90;
    public const int AirActionCarrierExSlideD = 91;
    public const int AirActionCarrierExSlideC = 92;
}
