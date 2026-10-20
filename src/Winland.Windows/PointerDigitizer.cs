using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.Pointer;

namespace Winland.Windows;

/// <summary>
/// Reads the rectangle Windows maps a pen or a touch screen to, and the tablet's size. The read changes no setting.
/// </summary>
internal static class PointerDigitizer
{
    /// <summary>
    /// The device of <paramref name="pen"/> whose display contains the point, or the first such device. Null when
    /// Windows lists none.
    /// </summary>
    internal static unsafe DigitizerSample? At(bool pen, int x, int y)
    {
        uint count = 0;
        if (!PInvoke.GetPointerDevices(&count, null) || count == 0)
            return null;

        var infos = new POINTER_DEVICE_INFO[count];
        fixed (POINTER_DEVICE_INFO* first = infos)
        {
            if (!PInvoke.GetPointerDevices(&count, first))
                return null;
        }

        DigitizerSample? firstOfType = null;
        var limit = Math.Min(count, (uint)infos.Length);
        for (var i = 0; i < limit; i++)
        {
            if (!Matches(infos[i].pointerDeviceType, pen))
                continue;

            var sample = Read(infos[i].device);
            if (sample is not DigitizerSample found)
                continue;

            firstOfType ??= found;
            if (Contains(found.Display, x, y))
                return found;
        }

        return firstOfType;
    }

    private static bool Matches(POINTER_DEVICE_TYPE type, bool pen) => pen
        ? type is POINTER_DEVICE_TYPE.POINTER_DEVICE_TYPE_INTEGRATED_PEN or POINTER_DEVICE_TYPE.POINTER_DEVICE_TYPE_EXTERNAL_PEN
        : type == POINTER_DEVICE_TYPE.POINTER_DEVICE_TYPE_TOUCH;

    // Himetric is a hundredth of a millimetre. A size Windows does not report is zero, so the active area is skipped.
    private static unsafe DigitizerSample? Read(HANDLE device)
    {
        RECT deviceRect;
        RECT display;
        if (!PInvoke.GetPointerDeviceRects(device, &deviceRect, &display))
            return null;

        var width = display.right - display.left;
        var height = display.bottom - display.top;
        var area = width > 0 && height > 0 ? new Rectangle(display.left, display.top, width, height) : default;
        var mmWidth = Math.Abs(deviceRect.right - deviceRect.left) / 100.0;
        var mmHeight = Math.Abs(deviceRect.bottom - deviceRect.top) / 100.0;
        if (mmWidth <= 0 || mmHeight <= 0)
            mmWidth = mmHeight = 0;
        return new DigitizerSample(area, mmWidth, mmHeight);
    }

    private static bool Contains(Rectangle area, int x, int y) =>
        area.Width > 0 && area.Height > 0 && x >= area.X && y >= area.Y && x < area.X + area.Width && y < area.Y + area.Height;
}
