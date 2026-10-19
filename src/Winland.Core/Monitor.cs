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
/// Mode and scale in the rule are not applied.
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
}
