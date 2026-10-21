using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// A key or mouse button in the form the input API takes it. A key has a virtual key, a scan code,
/// or both. <see cref="MouseButton"/> is the evdev button code (272 left to 276 extra) for a button.
/// </summary>
public readonly record struct KeyStroke(int VirtualKey, int Scan, bool Extended, int MouseButton = 0)
{
    public bool IsMouse => MouseButton != 0;
}

public static class KeyStrokes
{
    internal const int FirstMouseButton = 272;
    internal const int LastMouseButton = 276;

    /// <summary>
    /// Resolves a relayed key: a key name in the given layout, <c>code:N</c>, or <c>mouse:N</c>.
    /// A name is tried first. <paramref name="code"/> is the XKB keycode used when the name does not resolve.
    /// </summary>
    public static bool TryResolve(string key, int code, KeyLayout layout, out KeyStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(layout);
        stroke = default;
        if (TryMouse(key, out var button))
        {
            if (button is < FirstMouseButton or > LastMouseButton)
                return false;

            stroke = new KeyStroke(0, 0, false, button);
            return true;
        }

        if (key.Length > 0)
        {
            // The layout comes first: a symbol such as slash sits on a different key outside the US layout.
            var resolved = KeyNames.Resolve(key, layout);
            if (layout.TryVirtualKey(resolved, out var virtualKey))
            {
                stroke = new KeyStroke(virtualKey, 0, false);
                return true;
            }

            if (KeyNames.TryVirtualKey(resolved, out virtualKey, out var extended))
            {
                stroke = new KeyStroke(virtualKey, 0, extended || IsExtended(virtualKey));
                return true;
            }

            if (TryCode(resolved, out var named))
                code = named;
        }

        if (code > 0 && KeyNames.TryScan(code, out var scan, out var scanExtended, out var scanKey))
        {
            stroke = new KeyStroke(scanKey, scan, scanExtended);
            return true;
        }

        return false;
    }

    // Keys that only exist on the extended part of the keyboard: navigation, arrows, the right
    // Ctrl and Alt, the Windows and menu keys, keypad divide, Num Lock and Print Screen.
    private static bool IsExtended(int virtualKey) =>
        virtualKey is >= 0x21 and <= 0x28 or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;

    internal static bool TryMouse(string key, out int button)
    {
        button = 0;
        return key.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out button);
    }

    internal static bool TryCode(string key, out int code)
    {
        code = 0;
        return key.StartsWith("code:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out code)
            && code > 0;
    }
}
