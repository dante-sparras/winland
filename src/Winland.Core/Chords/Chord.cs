namespace Winland.Core.Chords;

/// <summary>Modifiers that participate in a chord. Alt+Tab is not represented here; Windows keeps it.</summary>
[Flags]
public enum ChordModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>The Windows key.</summary>
    Super = 1,

    /// <summary>Shift.</summary>
    Shift = 2,

    /// <summary>Control.</summary>
    Ctrl = 4,

    /// <summary>Alt.</summary>
    Alt = 8,
}

/// <summary>Keys the window manager knows how to bind.</summary>
public enum ChordKey
{
    /// <summary>Digit 1.</summary>
    D1,
    /// <summary>Digit 2.</summary>
    D2,
    /// <summary>Digit 3.</summary>
    D3,
    /// <summary>Digit 4.</summary>
    D4,
    /// <summary>Digit 5.</summary>
    D5,
    /// <summary>Digit 6.</summary>
    D6,
    /// <summary>Digit 7.</summary>
    D7,
    /// <summary>Digit 8.</summary>
    D8,
    /// <summary>Digit 9.</summary>
    D9,
    /// <summary>Left arrow.</summary>
    Left,
    /// <summary>Right arrow.</summary>
    Right,
    /// <summary>Up arrow.</summary>
    Up,
    /// <summary>Down arrow.</summary>
    Down,
    /// <summary>Tab.</summary>
    Tab,
    /// <summary>Enter.</summary>
    Return,
    /// <summary>Space.</summary>
    Space,
    /// <summary>Escape.</summary>
    Escape,
    /// <summary>W.</summary>
    W,
    /// <summary>Q.</summary>
    Q,
    /// <summary>T.</summary>
    T,
    /// <summary>F.</summary>
    F,
    /// <summary>G.</summary>
    G,
    /// <summary>L.</summary>
    L,
    /// <summary>J.</summary>
    J,
    /// <summary>C.</summary>
    C,
    /// <summary>X.</summary>
    X,
    /// <summary>V.</summary>
    V,
    /// <summary>I.</summary>
    I,
    /// <summary>S.</summary>
    S,
    /// <summary>Minus.</summary>
    Minus,
    /// <summary>Equal or plus.</summary>
    Equal,
    /// <summary>Backspace.</summary>
    Backspace,
    /// <summary>Comma.</summary>
    Comma,
    /// <summary>F9.</summary>
    F9,
    /// <summary>Print Screen.</summary>
    PrintScreen,

    /// <summary>P.</summary>
    P,

    /// <summary>A.</summary>
    A,

    /// <summary>B.</summary>
    B,

    /// <summary>D.</summary>
    D,

    /// <summary>E.</summary>
    E,

    /// <summary>O.</summary>
    O,

    /// <summary>The grave key, to the left of 1.</summary>
    Grave,

    /// <summary>K.</summary>
    K,

    /// <summary>R.</summary>
    R,

    /// <summary>H.</summary>
    H,

    /// <summary>M.</summary>
    M,

    /// <summary>N.</summary>
    N,

    /// <summary>U.</summary>
    U,

    /// <summary>Y.</summary>
    Y,

    /// <summary>Z.</summary>
    Z,

    /// <summary>Slash. On a layout with no key for it, the key Windows gives the same code.</summary>
    Slash,

    /// <summary>Period.</summary>
    Period,

    /// <summary>The opening square bracket. On a layout with no key for it, the key Windows gives the same code.</summary>
    LeftBracket,

    /// <summary>The closing square bracket. On a layout with no key for it, the key Windows gives the same code.</summary>
    RightBracket,

    /// <summary>Home.</summary>
    Home,

    /// <summary>Delete. <c>Ctrl+Alt+Delete</c> is not bindable (<see cref="WindowsReserve"/>).</summary>
    Delete,
}

/// <summary>One key combination.</summary>
/// <param name="Modifiers">Held modifiers.</param>
/// <param name="Key">The non-modifier key.</param>
public readonly record struct Chord(ChordModifiers Modifiers, ChordKey Key);
