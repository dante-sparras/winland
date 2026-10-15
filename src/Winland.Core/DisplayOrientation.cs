namespace Winland;

/// <summary>A rotation a monitor rule can ask the display to use. Flipped values are not represented.</summary>
public enum DisplayOrientation
{
    Identity = 0,
    Rotate90 = 1,
    Rotate180 = 2,
    Rotate270 = 3,
}

/// <summary>A request to rotate one display. The adapter applies it; the layout does not call the display.</summary>
public readonly record struct DisplayOrientationRequest(string Device, DisplayOrientation Orientation);
