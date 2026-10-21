namespace Winland.Domain;

/// <summary>
/// A display Clients can be placed on. Its identity is its make, model and serial, which a later
/// Workspace can attach to without owning the geometry. With no model and no serial, the identity
/// is <see cref="Name"/>.
/// </summary>
/// <remarks>
/// <para>
/// Windows reports <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, <see cref="Height"/> and
/// <see cref="WorkArea"/> in physical pixels on the virtual screen. <see cref="WorkArea"/> is the part
/// Clients may use; the taskbar and a rule's reserved area are already removed. Placements use those
/// same physical pixels, so the adapter can apply them without converting.
/// </para>
/// <para>
/// <see cref="Scale"/> is the monitor DPI divided by 96. A reserved area is logical pixels:
/// <see cref="MonitorPixels"/> divides a physical length by <see cref="Scale"/>. A scale-2 monitor that is
/// 3840 physical pixels wide is 1920 wide in that layout space. Gaps and borders in the config are logical
/// pixels and are multiplied by <see cref="Scale"/> on that monitor.
/// </para>
/// <para>
/// <see cref="Name"/> is the Windows display name, for example <c>\\.\DISPLAY1</c>.
/// <see cref="Description"/> is the Monitor's identity: <see cref="Make"/>, <see cref="Model"/> and
/// <see cref="Serial"/> from its EDID, joined by spaces. Identical monitors, the same three, get
/// <c> #2</c>, <c> #3</c> and so on in the order they were first seen. A <c>desc:</c> prefix matches
/// that identity. With no model and no serial, <see cref="Description"/> stays the friendly name and
/// <c>desc:</c> matches that. A <c>monitor</c> rule selects a monitor by its display name or by that
/// prefix. <c>reserved_area</c> and <c>addreserved</c> take top, bottom, left and right in logical
/// pixels and remove them from <see cref="WorkArea"/>. Any other monitor setting is skipped.
/// The adapter fills <see cref="Make"/>, <see cref="Model"/> and <see cref="Serial"/> from the EDID.
/// <see cref="RefreshRate"/>, <see cref="PhysicalWidth"/> and <see cref="PhysicalHeight"/> stay empty
/// unless the adapter has them.
/// </para>
/// </remarks>
public sealed record Monitor(
    string Name,
    string Description,
    int X,
    int Y,
    int Width,
    int Height,
    double Scale,
    bool Enabled,
    Rectangle WorkArea,
    string Make = "",
    string Model = "",
    string Serial = "",
    double RefreshRate = 0,
    int PhysicalWidth = 0,
    int PhysicalHeight = 0,
    DisplayOrientation? Orientation = null)
{
    /// <summary>Empty. Winland does not name a color preset.</summary>
    public string ColorPreset { get; init; } = "";

    /// <summary>Empty. Winland does not mirror a display.</summary>
    public string MirrorOf { get; init; } = "";

    /// <summary>Bits per color channel Windows reported, or 0 when it has not.</summary>
    public int BitsPerChannel { get; init; }

    /// <summary>The display can show HDR, as Windows reported it.</summary>
    public bool HdrCapable { get; init; }

    /// <summary>The display can show wide color, as Windows reported it.</summary>
    public bool WideCapable { get; init; }

    /// <summary>Advanced color is supported. Before Windows 11 24H2 that is the HDR switch.</summary>
    public bool AdvancedCapable { get; init; }

    /// <summary>True when Windows reports HDR and wide color as two switches.</summary>
    public bool SeparateColor { get; init; }

    /// <summary>Windows reported color primaries for the display.</summary>
    public bool Chroma { get; init; }

    /// <summary>HDR is on now.</summary>
    public bool HdrActive { get; init; }

    /// <summary>Wide color is on now.</summary>
    public bool WideActive { get; init; }

    /// <summary>Mastering minimum luminance in nits, or -1 when it is not known.</summary>
    public float MinLuminance { get; init; } = -1;

    /// <summary>Mastering maximum luminance in nits, or -1 when it is not known.</summary>
    public float MaxLuminance { get; init; } = -1;

    /// <summary>Maximum average luminance in nits, or -1 when it is not known.</summary>
    public float MaxAvgLuminance { get; init; } = -1;

    /// <summary>Reported SDR brightness. Winland does not set it.</summary>
    public double SdrBrightness { get; init; } = 1;

    /// <summary>Reported SDR saturation. Winland does not set it.</summary>
    public double SdrSaturation { get; init; } = 1;

    /// <summary>Reported SDR minimum luminance. Winland does not set it.</summary>
    public double SdrMinLuminance { get; init; } = 0.2;

    /// <summary>Reported SDR maximum luminance. Winland does not set it.</summary>
    public int SdrMaxLuminance { get; init; } = 80;
}

public readonly record struct MonitorId(int Value)
{
    public static MonitorId Primary { get; } = new(0);
}

/// <summary>A rotation Windows may report for a display. Winland does not change it.</summary>
public enum DisplayOrientation
{
    Identity = 0,
    Rotate90 = 1,
    Rotate180 = 2,
    Rotate270 = 3,
}
