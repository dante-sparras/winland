using System.Collections.Concurrent;

namespace Winland.Infrastructure;

/// <summary>
/// The alpha a window shows while a motion fades it. The border of the window is drawn with it, so
/// it fades in and out with the window. A window with no running motion has none, and its border is opaque.
/// Any thread may read or change it.
/// </summary>
internal static class MotionAlpha
{
    private static readonly ConcurrentDictionary<WindowId, byte> Levels = new();

    internal static byte Of(WindowId window) => Levels.TryGetValue(window, out var alpha) ? alpha : (byte)255;

    /// <summary>Sets the alpha, or takes it away when the motion is over. The decorations draw again when it changed.</summary>
    internal static void Set(WindowId window, byte? alpha)
    {
        var changed = alpha is byte level
            ? !Levels.TryGetValue(window, out var before) || before != level
            : Levels.ContainsKey(window);
        if (alpha is byte now)
            Levels[window] = now;
        else
            Levels.TryRemove(window, out _);

        if (changed)
            StandFrames.Follow(window);
    }
}
