using System.Globalization;

namespace Winland.Domain;

/// <summary>Modifiers the caller is tracking. Latched lock state is not an input.</summary>
public readonly record struct KeyModifierState(bool Shift, bool Control, bool Alt, bool Super);

/// <summary>
/// Names a key from its virtual key, scan code and the layout's unshifted characters,
/// and resolves a config key to that same name.
/// </summary>
public static class KeyNames
{
    private static readonly string[] Letters =
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
    ];

    private static readonly string[] Digits = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    private static readonly string[] Functions =
    [
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
    ];

    private static readonly string[] Keypad =
    [
        "KP_0", "KP_1", "KP_2", "KP_3", "KP_4", "KP_5", "KP_6", "KP_7", "KP_8", "KP_9",
    ];

    private static readonly Dictionary<char, string> Symbols = new()
    {
        [';'] = "semicolon",
        ['='] = "equal",
        [','] = "comma",
        ['-'] = "minus",
        ['.'] = "period",
        ['/'] = "slash",
        ['`'] = "grave",
        ['['] = "bracketleft",
        ['\\'] = "backslash",
        [']'] = "bracketright",
        ['\''] = "apostrophe",
        ['+'] = "plus",
        ['#'] = "numbersign",
        ['<'] = "less",
        ['>'] = "greater",
        ['?'] = "question",
        [':'] = "colon",
        ['"'] = "quotedbl",
        ['!'] = "exclam",
        ['@'] = "at",
        ['^'] = "asciicircum",
        ['_'] = "underscore",
        ['{'] = "braceleft",
        ['|'] = "bar",
        ['}'] = "braceright",
        ['~'] = "asciitilde",
        ['&'] = "ampersand",
        ['*'] = "asterisk",
        ['('] = "parenleft",
        [')'] = "parenright",
        ['%'] = "percent",
        ['$'] = "dollar",
        ['ä'] = "adiaeresis",
        ['ö'] = "odiaeresis",
        ['ü'] = "udiaeresis",
        ['ß'] = "ssharp",
        ['é'] = "eacute",
        ['è'] = "egrave",
        ['à'] = "agrave",
        ['ç'] = "ccedilla",
        ['ñ'] = "ntilde",
        ['å'] = "aring",
        ['ø'] = "oslash",
    };

    private static readonly Dictionary<string, char> BySymbol = BuildBySymbol();

    private static readonly Dictionary<string, (int VirtualKey, bool Extended)> VirtualKeys = BuildVirtualKeys();

    public static bool PassesThrough(int virtualKey) => virtualKey is 0xE5 or 0xE7;

    public static string Name(int virtualKey, int scanCode, bool extended, KeyLayout layout, KeyModifierState modifiers)
    {
        ArgumentNullException.ThrowIfNull(layout);
        // The unshifted symbol does not follow shift, and this never reads latched lock state.
        _ = modifiers;

        if (PassesThrough(virtualKey) || IsSyntheticAltGrControl(virtualKey, scanCode, extended))
            return "";

        if (virtualKey == 0xA5)
            return layout.HasAltGr ? "ISO_Level3_Shift" : "Alt_R";

        if (IsCharacterKey(virtualKey))
        {
            if (layout.IsDead(virtualKey))
                return VirtualKeyName(virtualKey, extended);

            if (layout.TrySymbol(virtualKey, out var symbol))
                return symbol;
        }

        var named = VirtualKeyName(virtualKey, extended);
        if (named.Length > 0)
            return named;

        var code = XkbCode(virtualKey, scanCode, extended);
        return code > 0 ? $"code:{code}" : $"vk:{virtualKey}";
    }

    public static string Resolve(string configName, KeyLayout layout)
    {
        ArgumentNullException.ThrowIfNull(configName);
        ArgumentNullException.ThrowIfNull(layout);
        if (configName.Length == 0
            || configName.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase)
            || configName.Equals("catchall", StringComparison.Ordinal))
            return configName;

        var split = configName.IndexOf('&');
        if (split > 0 && split < configName.Length - 1)
        {
            var parts = configName.Split('&');
            for (var index = 0; index < parts.Length; index++)
                parts[index] = Resolve(parts[index].Trim(), layout);
            return string.Join('&', parts);
        }

        if (TryCode(configName, out var code))
            return code;

        if (TryKeysym(configName, layout, out var symbol))
            return symbol;

        if (TryVirtualKey(configName, out var virtualKey, out var extended))
            return Name(virtualKey, 0, extended, layout, default);

        if (TryAlias(configName, out var alias))
            return alias;

        return configName;
    }

    /// <summary>XKB keycode for a Windows scan code: the evdev keycode plus 8. Zero when this scan is not in the table.</summary>
    public static int XkbCode(int virtualKey, int scanCode, bool extended)
    {
        if (scanCode <= 0 || IsSyntheticAltGrControl(virtualKey, scanCode, extended))
            return 0;

        var scan = scanCode & 0xFF;
        var evdev = Evdev(virtualKey, scan, extended);
        return evdev == 0 ? 0 : evdev + 8;
    }

    internal static string? Canonical(string character)
    {
        if (character.Length != 1)
            return character.Length == 0 ? null : character;

        var value = character[0];
        if (value is >= 'a' and <= 'z')
            return Letters[value - 'a'];
        if (value is >= 'A' and <= 'Z')
            return Letters[value - 'A'];
        if (value is >= '0' and <= '9')
            return Digits[value - '0'];

        var lower = char.ToLowerInvariant(value);
        if (Symbols.TryGetValue(lower, out var named))
            return named;

        return value > 127 ? character : null;
    }

    public static bool TryVirtualKey(string name, out int virtualKey, out bool extended)
    {
        if (VirtualKeys.TryGetValue(name, out var found))
        {
            virtualKey = found.VirtualKey;
            extended = found.Extended;
            return true;
        }

        virtualKey = 0;
        extended = false;
        return false;
    }

    private static bool IsSyntheticAltGrControl(int virtualKey, int scanCode, bool extended)
    {
        if (virtualKey is not (0xA2 or 0x11))
            return false;

        if (scanCode == 0x21D)
            return true;

        return extended && (scanCode & 0xFF) == 0x1D;
    }

    private static bool IsCharacterKey(int virtualKey) =>
        virtualKey is >= 0x30 and <= 0x39
            or >= 0x41 and <= 0x5A
            or >= 0xBA and <= 0xC0
            or >= 0xDB and <= 0xDF
            or 0xE2;

    private static bool TryKeysym(string configName, KeyLayout layout, out string symbol)
    {
        foreach (var (character, produced) in layout.Symbols())
        {
            if (produced.Equals(configName, StringComparison.OrdinalIgnoreCase) || AliasOf(character, configName))
            {
                symbol = produced;
                return true;
            }
        }

        symbol = "";
        return false;
    }

    private static bool AliasOf(string character, string configName)
    {
        if (character.Length == 1 && character.Equals(configName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (character.Length == 1 && TryParseUnicode(configName, out var parsed) && parsed == character[0])
            return true;

        return character.Length == 1
            && Symbols.TryGetValue(char.ToLowerInvariant(character[0]), out var name)
            && name.Equals(configName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryAlias(string configName, out string symbol)
    {
        symbol = "";
        if (BySymbol.TryGetValue(configName, out var named))
        {
            symbol = Canonical(named.ToString()) ?? "";
            return symbol.Length > 0;
        }

        if (configName.Length == 1)
        {
            symbol = Canonical(configName) ?? "";
            return symbol.Length > 0;
        }

        if (TryParseUnicode(configName, out var parsed))
        {
            symbol = Canonical(parsed.ToString()) ?? "";
            return symbol.Length > 0;
        }

        return false;
    }

    private static bool TryParseUnicode(string text, out char character)
    {
        character = '\0';
        if (text.Length < 3 || text[1] != '+' || text[0] is not ('U' or 'u'))
            return false;

        if (!int.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
            return false;

        if (code is < 0 or > 0xFFFF)
            return false;

        character = (char)code;
        return true;
    }

    // code:N, and a bare number above 9, as Winland reads a keycode. 0 to 9 are digit keys.
    private static bool TryCode(string text, out string code)
    {
        code = "";
        if (text.Length > 1 && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var bare) && bare > 9)
        {
            code = $"code:{bare}";
            return true;
        }

        if (text.Length < 6 || !text.StartsWith("code:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!int.TryParse(text.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0)
            return false;

        code = $"code:{number}";
        return true;
    }

    /// <summary>The scan code for an XKB keycode, the inverse of <see cref="XkbCode"/>. Pause also needs its virtual key.</summary>
    internal static bool TryScan(int xkbCode, out int scan, out bool extended, out int virtualKey)
    {
        virtualKey = 0;
        extended = false;
        scan = 0;
        var evdev = xkbCode - 8;
        if (evdev == 119)
        {
            virtualKey = 0x13;
            scan = 0x45;
            return true;
        }

        if (evdev is >= 0x01 and <= 0x53 or 0x57 or 0x58)
        {
            scan = evdev;
            return true;
        }

        for (var candidate = 0x01; candidate <= 0x5D; candidate++)
        {
            if (Evdev(0, candidate, extended: true) != evdev || evdev == 0)
                continue;

            scan = candidate;
            extended = true;
            return true;
        }

        return false;
    }

    private static int Evdev(int virtualKey, int scan, bool extended)
    {
        // Pause shares num lock's scan. The virtual key tells them apart.
        if (virtualKey == 0x13 && scan == 0x45)
            return 119;

        if (extended)
        {
            return scan switch
            {
                0x1C => 96,
                0x1D => 97,
                0x35 => 98,
                0x37 => 99,
                0x38 => 100,
                0x47 => 102,
                0x48 => 103,
                0x49 => 104,
                0x4B => 105,
                0x4D => 106,
                0x4F => 107,
                0x50 => 108,
                0x51 => 109,
                0x52 => 110,
                0x53 => 111,
                0x5B => 125,
                0x5C => 126,
                0x5D => 127,
                _ => 0,
            };
        }

        // Set-1 scan codes for these keys are the evdev keycodes.
        if (scan is >= 0x01 and <= 0x53 or 0x57 or 0x58)
            return scan;

        return 0;
    }

    private static string VirtualKeyName(int virtualKey, bool extended)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
            return Letters[virtualKey - 0x41];
        if (virtualKey is >= 0x30 and <= 0x39)
            return Digits[virtualKey - 0x30];
        if (virtualKey is >= 0x70 and <= 0x87)
            return Functions[virtualKey - 0x70];
        if (virtualKey is >= 0x60 and <= 0x69)
            return Keypad[virtualKey - 0x60];

        return virtualKey switch
        {
            0x08 => "BackSpace",
            0x09 => "Tab",
            0x0D => extended ? "KP_Enter" : "Return",
            0x13 => "Pause",
            0x14 => "Caps_Lock",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "Prior",
            0x22 => "Next",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "Print",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5B => "Super_L",
            0x5C => "Super_R",
            0x5D => "Menu",
            0x10 or 0xA0 => "Shift_L",
            0xA1 => "Shift_R",
            0x11 or 0xA2 => "Control_L",
            0xA3 => "Control_R",
            0x12 or 0xA4 => "Alt_L",
            0xA5 => "Alt_R",
            0x6A => "KP_Multiply",
            0x6B => "KP_Add",
            0x6D => "KP_Subtract",
            0x6E => "KP_Decimal",
            0x6F => "KP_Divide",
            0x90 => "Num_Lock",
            0x91 => "Scroll_Lock",
            0xAD => "XF86AudioMute",
            0xAE => "XF86AudioLowerVolume",
            0xAF => "XF86AudioRaiseVolume",
            0xB0 => "XF86AudioNext",
            0xB1 => "XF86AudioPrev",
            0xB2 => "XF86AudioStop",
            0xB3 => "XF86AudioPlay",
            0xBA => "semicolon",
            0xBB => "equal",
            0xBC => "comma",
            0xBD => "minus",
            0xBE => "period",
            0xBF => "slash",
            0xC0 => "grave",
            0xDB => "bracketleft",
            0xDC => "backslash",
            0xDD => "bracketright",
            0xDE => "apostrophe",
            _ => "",
        };
    }

    private static Dictionary<string, char> BuildBySymbol()
    {
        var names = new Dictionary<string, char>(Symbols.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (character, name) in Symbols)
            names[name] = character;
        return names;
    }

    private static Dictionary<string, (int VirtualKey, bool Extended)> BuildVirtualKeys()
    {
        var keys = new Dictionary<string, (int, bool)>(StringComparer.OrdinalIgnoreCase);
        for (var virtualKey = 1; virtualKey < 0xFF; virtualKey++)
        {
            var name = VirtualKeyName(virtualKey, false);
            if (name.Length > 0)
                keys[name] = (virtualKey, false);

            var extendedName = VirtualKeyName(virtualKey, true);
            if (extendedName.Length > 0 && !extendedName.Equals(name, StringComparison.Ordinal))
                keys[extendedName] = (virtualKey, true);
        }

        return keys;
    }
}
