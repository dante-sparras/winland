using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Winland.Windows;

/// <summary>The current physical mode of one display, in pixels, and the rotation it is using.</summary>
public readonly record struct DisplayMode(int Width, int Height, DisplayOrientation Orientation);

/// <summary>
/// Reads and commits a display mode. The session does not call this; tests pass a stand-in so
/// nothing changes a real display.
/// </summary>
public interface IDisplayModeSource
{
    bool TryRead(string device, out DisplayMode mode);

    bool TryCommit(string device, DisplayMode mode);
}

/// <summary>Turns a rotation request into a display-mode change. A matching rotation does not commit.</summary>
public static class DisplayOrientationAdapter
{
    public static bool TryApply(DisplayOrientationRequest request, IDisplayModeSource source)
    {
        ArgumentNullException.ThrowIfNull(request.Device);
        ArgumentNullException.ThrowIfNull(source);
        if (request.Device.Length == 0 || !source.TryRead(request.Device, out var current))
            return false;

        if (current.Orientation == request.Orientation)
            return true;

        var width = current.Width;
        var height = current.Height;
        if (SwapsAxes(current.Orientation, request.Orientation))
            (width, height) = (height, width);

        return source.TryCommit(request.Device, new DisplayMode(width, height, request.Orientation));
    }

    /// <summary>Applies <paramref name="request"/> with the system display APIs. Tests do not call this.</summary>
    public static bool TryApplyToSystem(DisplayOrientationRequest request) =>
        TryApply(request, Win32DisplayModes.Shared);

    private static bool SwapsAxes(DisplayOrientation current, DisplayOrientation next) =>
        ((int)current & 1) != ((int)next & 1);

    private sealed class Win32DisplayModes : IDisplayModeSource
    {
        public static readonly Win32DisplayModes Shared = new();

        public bool TryRead(string device, out DisplayMode mode)
        {
            mode = default;
            if (!TryCurrent(device, out var devmode))
                return false;

            var orientation = (int)devmode.Anonymous1.Anonymous2.dmDisplayOrientation;
            if (orientation is < 0 or > 3)
                return false;

            mode = new DisplayMode((int)devmode.dmPelsWidth, (int)devmode.dmPelsHeight, (DisplayOrientation)orientation);
            return true;
        }

        public unsafe bool TryCommit(string device, DisplayMode mode)
        {
            if (!TryCurrent(device, out var devmode))
                return false;

            // Already at this orientation, so the display settings stay as they are.
            if ((int)devmode.Anonymous1.Anonymous2.dmDisplayOrientation == (int)mode.Orientation)
                return true;

            devmode.dmFields = DEVMODE_FIELD_FLAGS.DM_DISPLAYORIENTATION
                | DEVMODE_FIELD_FLAGS.DM_PELSWIDTH
                | DEVMODE_FIELD_FLAGS.DM_PELSHEIGHT;
            devmode.dmPelsWidth = (uint)mode.Width;
            devmode.dmPelsHeight = (uint)mode.Height;
            devmode.Anonymous1.Anonymous2.dmDisplayOrientation = (DEVMODE_DISPLAY_ORIENTATION)(int)mode.Orientation;
            fixed (char* name = device)
            {
                var result = PInvoke.ChangeDisplaySettingsEx(new PCWSTR(name), &devmode, default, CDS_TYPE.CDS_UPDATEREGISTRY, null);
                return result == DISP_CHANGE.DISP_CHANGE_SUCCESSFUL;
            }
        }

        private static unsafe bool TryCurrent(string device, out DEVMODEW devmode)
        {
            devmode = default;
            if (string.IsNullOrEmpty(device))
                return false;

            devmode.dmSize = (ushort)sizeof(DEVMODEW);
            fixed (char* name = device)
            fixed (DEVMODEW* pointer = &devmode)
            {
                return PInvoke.EnumDisplaySettingsEx(name, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, pointer, default);
            }
        }
    }
}
