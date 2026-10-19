namespace Winland;

/// <summary>The XKB option that switches to the next keyboard layout, of those <c>grp:</c> offers.</summary>
public enum LayoutToggle
{
    None,

    /// <summary><c>grp:alt_shift_toggle</c>: Alt and Shift together.</summary>
    AltShift,

    /// <summary><c>grp:ctrl_shift_toggle</c>: Ctrl and Shift together.</summary>
    CtrlShift,

    /// <summary><c>grp:ctrl_alt_toggle</c>: Ctrl and Alt together.</summary>
    CtrlAlt,

    /// <summary><c>grp:win_space_toggle</c>: Win and Space.</summary>
    WinSpace,

    /// <summary><c>grp:alt_space_toggle</c>: Alt and Space.</summary>
    AltSpace,

    /// <summary><c>grp:alts_toggle</c>: both Alt keys together.</summary>
    BothAlts,

    /// <summary><c>grp:caps_toggle</c>: Caps Lock alone.</summary>
    Caps,
}

/// <summary>
/// The <c>input:kb_options</c> Winland honors: keys the options move, by Windows virtual key (0 drops the key), and the
/// combination that switches layouts. The options it does not know are listed so the config can warn.
/// </summary>
public sealed record KeyboardOptions(IReadOnlyDictionary<int, int> Remaps, LayoutToggle Toggle, IReadOnlyList<string> Unknown)
{
    public const int CapsLock = 0x14;
    public const int Escape = 0x1B;
    public const int Backspace = 0x08;
    public const int LeftControl = 0xA2;
    public const int LeftAlt = 0xA4;
    public const int RightAlt = 0xA5;
    public const int LeftWin = 0x5B;
    public const int RightWin = 0x5C;

    public static KeyboardOptions None { get; } = new(new Dictionary<int, int>(), LayoutToggle.None, []);

    private static readonly Dictionary<string, (int From, int To)[]> Moves = new(StringComparer.Ordinal)
    {
        ["caps:escape"] = [(CapsLock, Escape)],
        ["caps:escape_shifted_capslock"] = [(CapsLock, Escape)],
        ["caps:swapescape"] = [(CapsLock, Escape), (Escape, CapsLock)],
        ["caps:backspace"] = [(CapsLock, Backspace)],
        ["caps:super"] = [(CapsLock, LeftWin)],
        ["caps:none"] = [(CapsLock, 0)],
        ["caps:ctrl_modifier"] = [(CapsLock, LeftControl)],
        ["ctrl:nocaps"] = [(CapsLock, LeftControl)],
        ["ctrl:swapcaps"] = [(CapsLock, LeftControl), (LeftControl, CapsLock)],
        ["altwin:swap_alt_win"] = [(LeftAlt, LeftWin), (LeftWin, LeftAlt), (RightAlt, RightWin), (RightWin, RightAlt)],
        ["altwin:swap_lalt_lwin"] = [(LeftAlt, LeftWin), (LeftWin, LeftAlt)],
    };

    private static readonly Dictionary<string, LayoutToggle> Toggles = new(StringComparer.Ordinal)
    {
        ["grp:alt_shift_toggle"] = LayoutToggle.AltShift,
        ["grp:ctrl_shift_toggle"] = LayoutToggle.CtrlShift,
        ["grp:ctrl_alt_toggle"] = LayoutToggle.CtrlAlt,
        ["grp:win_space_toggle"] = LayoutToggle.WinSpace,
        ["grp:alt_space_toggle"] = LayoutToggle.AltSpace,
        ["grp:alts_toggle"] = LayoutToggle.BothAlts,
        ["grp:caps_toggle"] = LayoutToggle.Caps,
    };

    /// <summary>Reads a comma-separated <c>kb_options</c> value. A later option that moves the same key wins.</summary>
    public static KeyboardOptions Parse(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return None;

        var remaps = new Dictionary<int, int>();
        var toggle = LayoutToggle.None;
        var unknown = new List<string>();
        foreach (var option in options.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Moves.TryGetValue(option, out var moves))
            {
                foreach (var (from, to) in moves)
                    remaps[from] = to;
            }
            else if (Toggles.TryGetValue(option, out var found))
            {
                toggle = found;
            }
            else
            {
                unknown.Add(option);
            }
        }

        return new KeyboardOptions(remaps, toggle, unknown);
    }
}
