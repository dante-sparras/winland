using System.Collections.Concurrent;
using Windows.Win32.Foundation;

namespace Winland.Infrastructure;

/// <summary>
/// The rectangle a window's stand-in shows while a motion plays. The border of a window with a stand-in
/// follows this rectangle instead of the window's own frame, which already has its final size, and is stacked against the
/// stand-in. Any thread may read or change it.
/// </summary>
internal static class StandFrames
{
    private static readonly ConcurrentDictionary<WindowId, (Rectangle Frame, HWND Overlay)> Shown = new();

    /// <summary>The stand-in's current rectangle and window. An empty rectangle hides the decorations.</summary>
    internal static bool TryGet(WindowId window, out Rectangle frame, out HWND overlay)
    {
        if (Shown.TryGetValue(window, out var shown))
        {
            (frame, overlay) = shown;
            return true;
        }

        frame = default;
        overlay = HWND.Null;
        return false;
    }

    internal static void Set(WindowId window, Rectangle frame, HWND overlay) => Shown[window] = (frame, overlay);

    internal static void Remove(WindowId window) => Shown.TryRemove(window, out _);

    /// <summary>Makes the decorations of the window draw again where the stand-in is now, or at its own frame once it has none.</summary>
    internal static void Follow(WindowId window)
    {
        BorderHost.Follow(window);
    }
}
