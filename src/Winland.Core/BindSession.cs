namespace Winland;

/// <summary>
/// Held keys, caught keys, and inhibit for one input thread.
/// Keyboard and pointer events for that thread share one session.
/// </summary>
public sealed class BindSession
{
    internal readonly HashSet<string> DownKeys = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> DownButtons = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> CaughtKeys = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> CaughtButtons = new(StringComparer.OrdinalIgnoreCase);

    // Presses the OS saw. Their releases always reach the OS too, or the key stays down there.
    internal readonly HashSet<string> PassedKeys = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> PassedButtons = new(StringComparer.OrdinalIgnoreCase);

    // Held Super and Alt keys that already got a mask key during this hold.
    internal readonly HashSet<string> MaskedKeys = new(StringComparer.OrdinalIgnoreCase);
    internal Modifier Held;
    internal bool Inhibited;
    internal Point Pointer;
    internal string? LongPressKey;
    internal int LongPressCode;
    internal bool LongPressMouse;
    internal bool ScrollSeen;
    internal long ScrollAtMs;

    // The hook reads this with no lock. Empty is the default map.
    internal volatile string Submap = "";

    // The submap and pointer position at each held press. A click or drag bind measures from its own press.
    private readonly Dictionary<string, (string Submap, Point Anchor)> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Submap, Point Anchor)> _pressedButtons = new(StringComparer.OrdinalIgnoreCase);

    public void Inhibit(bool inhibited) => Inhibited = inhibited;

    internal void Remember(string key, bool mouse) =>
        Pressed(mouse)[key] = (Submap, Pointer);

    internal string Remembered(string key, bool mouse) =>
        Pressed(mouse).TryGetValue(key, out var press) ? press.Submap : Submap;

    internal (string Submap, Point Anchor) Forget(string key, bool mouse)
    {
        var pressed = Pressed(mouse);
        return pressed.Remove(key, out var press) ? press : (Submap, Pointer);
    }

    private Dictionary<string, (string Submap, Point Anchor)> Pressed(bool mouse) =>
        mouse ? _pressedButtons : _pressedKeys;
}
