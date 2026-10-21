namespace Winland.Domain;

[Flags]
public enum Modifier
{
    None = 0,
    Super = 1 << 0,
    Alt = 1 << 1,
    Ctrl = 1 << 2,
    Shift = 1 << 3,

    /// <summary>The Caps Lock key held down. The lock itself is not a modifier.</summary>
    Caps = 1 << 4,

    /// <summary>The Num Lock key held down.</summary>
    Mod2 = 1 << 5,

    /// <summary>No key on Windows holds it, as on Winland's default keymap.</summary>
    Mod3 = 1 << 6,

    /// <summary>AltGr held down, on a keyboard layout that has it.</summary>
    Mod5 = 1 << 7,
}
