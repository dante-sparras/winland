namespace Winland.Core.Shell;

/// <summary>Which picture a window chip should show.</summary>
public enum WindowMark
{
    /// <summary>The icon the window itself published.</summary>
    Window,

    /// <summary>The logo of the app id the window carries.</summary>
    App,

    /// <summary>The icon of the executable file.</summary>
    File,
}

/// <summary>Picks a window chip's picture.</summary>
public static class WindowMarkChoice
{
    /// <summary>
    /// A window that set its own icon keeps that.
    /// A hosted app such as Settings publishes no window icon; its app id carries the gear,
    /// and the host executable is only the generic program mark.
    /// A browser web app does publish a window icon, often blank or the browser's own mark.
    /// Its installed logo is the picture.
    /// </summary>
    /// <param name="hasWindowIcon">True when the window or its class returned a visible icon.</param>
    /// <param name="hasAppLogo">True when the window's app id resolved to a logo.</param>
    /// <param name="preferAppLogo">True when the app logo is the picture even though the window has an icon.</param>
    /// <returns>The picture to draw.</returns>
    public static WindowMark Choose(bool hasWindowIcon, bool hasAppLogo, bool preferAppLogo = false)
    {
        if (preferAppLogo && hasAppLogo)
        {
            return WindowMark.App;
        }

        if (hasWindowIcon)
        {
            return WindowMark.Window;
        }

        return hasAppLogo ? WindowMark.App : WindowMark.File;
    }
}
