using System.Collections.Concurrent;

namespace Winland.Windows;

/// <summary>
/// The wobble and motion blur each window's motions are drawn with, as its last decoration gave them. A window with none has no entry.
/// Any thread may read or change it.
/// </summary>
internal static class MotionFx
{
    private static readonly ConcurrentDictionary<WindowId, MotionEffects> Settings = new();

    internal static MotionEffects? Of(WindowId window) => Settings.TryGetValue(window, out var effects) ? effects : null;

    /// <summary>Records the effects a decoration gave the window, or takes them away.</summary>
    internal static void Set(WindowId window, MotionEffects? effects)
    {
        if (effects is { Any: true })
            Settings[window] = effects;
        else
            Settings.TryRemove(window, out _);
    }

    internal static void Forget(WindowId window) => Settings.TryRemove(window, out _);

    internal static void ForgetAll() => Settings.Clear();
}
