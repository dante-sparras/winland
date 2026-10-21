using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winland.Windows;

/// <summary>The Windows accent, read outside the core. Tests replace <see cref="Read"/>.</summary>
internal static class SystemAccent
{
    internal static Func<BorderColor?> Read = ReadWindows;

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

    private static unsafe BorderColor? Colorization()
    {
        uint packed;
        BOOL opaque;
        if (PInvoke.DwmGetColorizationColor(&packed, &opaque).Failed)
            return null;

        return new BorderColor(0xFF, (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }
}
