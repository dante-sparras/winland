namespace Winland.Domain;

/// <summary>A rotation Windows may report for a display. Winland does not change it.</summary>
public enum DisplayOrientation
{
    Identity = 0,
    Rotate90 = 1,
    Rotate180 = 2,
    Rotate270 = 3,
}
