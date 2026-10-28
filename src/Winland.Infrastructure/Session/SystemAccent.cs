using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

/// <summary>
/// The Windows accent, the High Contrast colors and the text size, read outside the core. Tests replace
/// <see cref="Read"/>, <see cref="Contrast"/> and <see cref="TextScale"/>.
/// </summary>
internal static class SystemAccent
{
    private const int HighlightColor = 13;
    private const int WindowTextColor = 8;
    private const int GrayTextColor = 17;

    internal static Func<BorderColor?> Read = ReadWindows;

    internal static Func<HighContrast?> Contrast = ReadContrast;

    internal static Func<double> TextScale = ReadTextScale;

    internal static BorderColor? ReadWindows()
    {
        if (TryUiAccent(out var accent))
            return accent;

        return Colorization();
    }

    private static bool TryUiAccent(out BorderColor color)
    {
        color = default;
        try
        {
            var value = new global::Windows.UI.ViewManagement.UISettings()
                .GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent);
            color = new BorderColor(0xFF, value.R, value.G, value.B);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    internal static unsafe HighContrast? ReadContrast()
    {
        var contrast = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        if (!PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETHIGHCONTRAST, contrast.cbSize, &contrast, 0)
            || (contrast.dwFlags & HIGHCONTRASTW_FLAGS.HCF_HIGHCONTRASTON) == 0)
            return null;

        return new HighContrast(System(HighlightColor), System(WindowTextColor), System(GrayTextColor));
    }

    internal static double ReadTextScale()
    {
        try
        {
            return new global::Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return 1;
        }
    }

    // GetSysColor packs a color as 0x00BBGGRR.
    private static BorderColor System(int index)
    {
        var packed = PInvoke.GetSysColor((SYS_COLOR_INDEX)index);
        return new BorderColor(0xFF, (byte)packed, (byte)(packed >> 8), (byte)(packed >> 16));
    }

    private static unsafe BorderColor? Colorization()
    {
        uint packed;
        BOOL opaque;
        if (PInvoke.DwmGetColorizationColor(&packed, &opaque).Failed)
            return null;

        return new BorderColor(0xFF, (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }
}
