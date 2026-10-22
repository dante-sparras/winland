namespace Winland.Infrastructure;

/// <summary>Whether the user holds a key, named as a config names it or by its XKB keycode, as the keyboard hook saw.</summary>
public static class KeyState
{
    /// <summary>
    /// True or false for a key the foreground window's layout has, or null when <paramref name="key"/> names no key,
    /// or, with an empty name, <paramref name="code"/> is no keycode.
    /// </summary>
    public static bool? IsDown(string key, int code)
    {
        ArgumentNullException.ThrowIfNull(key);
        var layout = KeyboardLayout.Read(KeyboardLayout.ForegroundHandle());
        if (!KeyStrokes.TryResolve(key, code, layout, out var stroke) || stroke.IsMouse)
            return null;

        return KeyboardHook.IsDown(stroke.VirtualKey);
    }
}
