using Winland.Core;

namespace Winland.Platform;

/// <summary>Writes <see cref="ShellMark"/> on a window and reads it back. The shell tags its own windows. Winland only reads.</summary>
public static class ShellProperty
{
    /// <summary>Makes <paramref name="hwnd"/> carry the shell mark. A window that already carries it is left as it is.</summary>
    /// <param name="hwnd">A window the shell created.</param>
    public static void Tag(nint hwnd)
    {
        if (hwnd == 0 || Native.GetProp(hwnd, ShellMark.Property) == ShellMark.Value)
        {
            return;
        }

        Native.SetProp(hwnd, ShellMark.Property, ShellMark.Value);
    }

    /// <summary>True when <paramref name="hwnd"/> carries the shell mark.</summary>
    /// <param name="hwnd">A top-level window.</param>
    /// <returns>True when Winland must not tile or hide it.</returns>
    public static bool IsTagged(nint hwnd) => hwnd != 0 && Native.GetProp(hwnd, ShellMark.Property) == ShellMark.Value;
}
