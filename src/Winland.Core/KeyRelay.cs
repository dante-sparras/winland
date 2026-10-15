namespace Winland;

/// <summary>Whether a relayed key goes down, comes up, or goes down and up.</summary>
public enum KeyRelayPhase
{
    Press,
    Release,
    Tap,
}

/// <summary>
/// A key or mouse button that <c>pass</c>, <c>sendshortcut</c> or <c>sendkeystate</c> sends on.
/// A null <see cref="Target"/> sends it to the focused window. <see cref="Key"/> is a key name,
/// <c>code:N</c>, or <c>mouse:N</c>. <see cref="Code"/> is the XKB keycode when the key came from a
/// bind. A null <see cref="Modifiers"/> leaves the held modifiers alone; otherwise exactly those
/// modifiers are down around the key.
/// </summary>
public sealed record KeyRelay(WindowId? Target, string Key, KeyRelayPhase Phase)
{
    public int Code { get; init; }

    public Modifier? Modifiers { get; init; }
}

/// <summary>The key or button edge that ran a bind. <c>pass</c> sends the same edge on.</summary>
public readonly record struct BindTrigger(string Key, int Code, bool Pressed);
