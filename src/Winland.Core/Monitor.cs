namespace Winland;

/// <summary>
/// A display Clients can be placed on. Its identity is <see cref="Name"/>, which a later Workspace
/// can attach to without owning the geometry.
/// </summary>
/// <remarks>
/// <para>
/// Windows reports <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, <see cref="Height"/> and
/// <see cref="WorkArea"/> in physical pixels on the virtual screen. <see cref="WorkArea"/> is the part
/// Clients may use; the taskbar and a rule's reserved area are already removed. Placements use those
/// same physical pixels, so the adapter can apply them without converting.
/// </para>
/// <para>
/// <see cref="Scale"/> is the monitor DPI divided by 96. A rule's position, and the width used to pack
/// <c>auto</c> positions, are logical pixels: <see cref="MonitorPixels"/> divides a physical length by
/// <see cref="Scale"/>. A scale-2 monitor that is 3840 physical pixels wide is 1920 wide in that layout
/// space. The logical position is multiplied by <see cref="Scale"/> again before it is stored here.
/// Gaps and borders in the config are logical pixels and are multiplied by <see cref="Scale"/> on that monitor.
/// </para>
/// <para>
/// <see cref="Name"/> is the Windows display name, for example <c>\\.\DISPLAY1</c>.
/// <see cref="Description"/> is the EDID friendly name. A <c>monitor</c> rule selects a monitor by that
/// display name, or by a <c>desc:</c> prefix of the friendly name.
/// <c>reserved_area</c> and <c>addreserved</c> take top, bottom, left and right in logical pixels and
/// remove them from <see cref="WorkArea"/>. <c>disable</c> drops the monitor from the layout and does
/// not power the display off. <see cref="Orientation"/> is set when the rule asks for a rotation of
/// 0, 90, 180 or 270 degrees. The rectangle is not swapped; the adapter rotates the display.
/// <c>mirror</c> makes the display show another one's picture; see <see cref="MirrorOf"/>.
/// <see cref="Make"/>, <see cref="Model"/>, <see cref="Serial"/>, <see cref="RefreshRate"/>,
/// <see cref="PhysicalWidth"/> and <see cref="PhysicalHeight"/> stay empty unless the adapter has them.
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
    /// <summary>The <c>cm</c> preset its monitor rule names, or empty when none does.</summary>
    public string ColorPreset { get; init; } = "";

    /// <summary>
    /// The name of the display this one shows the picture of, or empty. A mirror is not in the layout: it is not
    /// <see cref="Enabled"/>, and it has its source's position.
    /// </summary>
    public string MirrorOf { get; init; } = "";

    /// <summary>Bits per color channel Windows reported, or 0 when it has not. A rule may raise the reported depth.</summary>
    public int BitsPerChannel { get; init; }

    /// <summary>The display can show HDR, as Windows reported it. A rule's <c>supports_hdr</c> does not change this.</summary>
    public bool HdrCapable { get; init; }

    /// <summary>The display can show wide color, as Windows reported it. A rule's <c>supports_wide_color</c> does not change this.</summary>
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

    /// <summary>Mastering minimum luminance in nits, or -1 when it is not known. A rule at or above 0 replaces it.</summary>
    public float MinLuminance { get; init; } = -1;

    /// <summary>Mastering maximum luminance in nits, or -1 when it is not known. A rule at or above 0 replaces it.</summary>
    public float MaxLuminance { get; init; } = -1;

    /// <summary>Maximum average luminance in nits, or -1 when it is not known. A rule at or above 0 replaces it.</summary>
    public float MaxAvgLuminance { get; init; } = -1;

    /// <summary>The rule's SDR brightness multiplier, or 1 when the rule leaves it out. It is reported and not applied.</summary>
    public double SdrBrightness { get; init; } = 1;

    /// <summary>The rule's SDR saturation multiplier, or 1 when the rule leaves it out. It is reported and not applied.</summary>
    public double SdrSaturation { get; init; } = 1;

    /// <summary>The rule's SDR minimum luminance, or 0.2 when the rule leaves it out. It is reported and not applied.</summary>
    public double SdrMinLuminance { get; init; } = 0.2;

    /// <summary>The rule's SDR maximum luminance, or 80 when the rule leaves it out. It is reported and not applied.</summary>
    public int SdrMaxLuminance { get; init; } = 80;
}
