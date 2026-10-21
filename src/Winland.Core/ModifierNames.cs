namespace Winland.Core;

/// <summary>Reads a modifier list such as <c>SUPER SHIFT</c>, as a bind and <c>sendshortcut</c> write it.</summary>
internal static class ModifierNames
{
    // Winland upper-cases the list and looks for each name anywhere in it,
    // so SUPER SHIFT, SUPER_SHIFT, SUPER+SHIFT and SUPERSHIFT are the same mask.
    private static readonly (string Name, Modifier Modifier)[] Names =
    [
        ("SHIFT", Modifier.Shift),
        ("CTRL", Modifier.Ctrl),
        ("CONTROL", Modifier.Ctrl),
        ("ALT", Modifier.Alt),
        ("MOD1", Modifier.Alt),
        ("SUPER", Modifier.Super),
        ("WIN", Modifier.Super),
        ("LOGO", Modifier.Super),
        ("MOD4", Modifier.Super),
        ("META", Modifier.Super),
        ("CAPS", Modifier.Caps),
        ("MOD2", Modifier.Mod2),
        ("MOD3", Modifier.Mod3),
        ("MOD5", Modifier.Mod5),
    ];

    internal static bool TryParse(string text, out Modifier modifiers, out string error)
    {
        modifiers = Modifier.None;
        error = "";
        var upper = text.Trim().ToUpperInvariant();
        if (upper.Length == 0)
            return true;

        foreach (var (name, modifier) in Names)
        {
            if (upper.Contains(name, StringComparison.Ordinal))
                modifiers |= modifier;
        }

        if (modifiers == Modifier.None)
        {
            error = $"Invalid modifier '{text.Trim()}'.";
            return false;
        }

        return true;
    }
}
