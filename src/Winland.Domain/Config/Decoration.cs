namespace Winland.Domain;

/// <summary>
/// How one Client is decorated. <see cref="Region"/> is set when DWM has no corner of that radius,
/// and the corners are then cut from the window instead. <see cref="Blur"/> asks for DWM's acrylic backdrop.
/// DWM's own shadow is left as Windows draws it.
/// </summary>
public readonly record struct Decoration(
    ClientId Client,
    BorderColor Border,
    CornerPreference Corners,
    byte Alpha = 255,
    bool Borderless = false)
{
    public CornerRegion? Region { get; init; }

    /// <summary>The window's opacity, from 0 to 255. <c>fadeSwitch</c> animates it. It is <see cref="Alpha"/>.</summary>
    public byte Opacity { get; init; } = Alpha;

    public bool Blur { get; init; }

    /// <summary>The border Winland draws outside the window, or null for none.</summary>
    public BorderRing? Ring { get; init; }
}
