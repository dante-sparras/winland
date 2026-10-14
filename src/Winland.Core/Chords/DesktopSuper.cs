namespace Winland.Core.Chords;

/// <summary>The Super finger the desktop hook holds, as it shares it with the elevated helper.</summary>
/// <param name="Key">The Windows key's physical id.</param>
/// <param name="Press">Counts the desktop hook's Super presses, so the helper tells a new press from one it saw go up.</param>
public readonly record struct DesktopSuper(int Key, int Press)
{
    /// <summary>The shared word for <paramref name="super"/>. Zero when no Super is held.</summary>
    /// <param name="super">The held finger, or null.</param>
    /// <returns>The press count above the key's low byte.</returns>
    public static int Pack(DesktopSuper? super) =>
        super is DesktopSuper held ? unchecked((held.Press << 8) | (held.Key & 0xFF)) : 0;

    /// <summary>Reads a shared word written by <see cref="Pack"/>.</summary>
    /// <param name="word">The shared word.</param>
    /// <returns>Null when no Super is held.</returns>
    public static DesktopSuper? Unpack(int word) =>
        word == 0 ? null : new DesktopSuper(word & 0xFF, (int)((uint)word >> 8));
}
