using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// A key or mouse button in the form the input API takes it. A key has a virtual key, a scan code,
/// or both. <see cref="MouseButton"/> is the evdev button code (272 left to 276 extra) for a button.
/// </summary>
public readonly record struct KeyStroke(int VirtualKey, int Scan, bool Extended, int MouseButton = 0)
{
    public bool IsMouse => MouseButton != 0;
}
