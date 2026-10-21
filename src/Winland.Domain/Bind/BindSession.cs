namespace Winland.Domain;

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
    // Held keys that are not modifiers, in the order they went down, with their scan codes. A bind on several keys
    // matches them in this order.
    internal readonly List<(string Key, int ScanCode)> Order = [];

    // Per held key: binds that run when it is released because a longer chord could still extend them, binds a
    // completed chord holds off, and release chords it completed in order.
    internal readonly Dictionary<string, List<Bind>> HeldBack = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, List<Bind>> Suppressed = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, List<Bind>> Armed = new(StringComparer.OrdinalIgnoreCase);
    // Release binds on one key that a held key already fully matches when another key is caught or let go. They do not
    // run on that key's release.
    internal readonly HashSet<Bind> Shadowed = new(ReferenceEqualityComparer.Instance);
    // The modifiers each held key or button went down under. Its release binds match against them.
    internal readonly Dictionary<string, Modifier> PressedUnder = new(StringComparer.OrdinalIgnoreCase);
    // The scan code each held key went down with.
    internal readonly Dictionary<string, int> ScanCodes = new(StringComparer.OrdinalIgnoreCase);

    // The device each held key went down on, so a release or a repeat still matches when that edge has no device.
    internal readonly Dictionary<string, string> Devices = new(StringComparer.OrdinalIgnoreCase);

    // The device of the event being matched. The hook thread sets it for the call and reads it with no lock.
    internal string? EventDevice;

    // When set, a device bind matched from a raw packet uses this modifier mask instead of the keys held now.
    // The packet arrives after the hook returned, so the mask is the one from the press.
    internal Modifier? DeviceMatchMask;
    public Modifier Held { get; internal set; }
    internal bool Inhibited;
    public Point Pointer { get; internal set; }
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

    internal int ScanCodeOf(string key) => ScanCodes.TryGetValue(key, out var code) ? code : 0;

    /// <summary>The modifiers <paramref name="key"/> went down under, or none when it is not held.</summary>
    public Modifier PressedUnderOf(string key) => PressedUnder.TryGetValue(key, out var under) ? under : default;

    /// <summary>The held keys that are not modifiers, in the order they went down.</summary>
    public (string Key, int ScanCode)[] HeldInOrder() => [.. Order];

    /// <summary>Forgets that <paramref name="key"/> was caught, when the hook gives its press back to the OS.</summary>
    public void ForgetCaught(string key) => CaughtKeys.Remove(key);

    internal void Remember(string key, bool mouse) =>
        Pressed(mouse)[key] = (Submap, Pointer);

    public string Remembered(string key, bool mouse) =>
        Pressed(mouse).TryGetValue(key, out var press) ? press.Submap : Submap;

    internal (string Submap, Point Anchor) Forget(string key, bool mouse)
    {
        var pressed = Pressed(mouse);
        return pressed.Remove(key, out var press) ? press : (Submap, Pointer);
    }

    internal static List<Bind> Of(Dictionary<string, List<Bind>> perKey, string key)
    {
        if (!perKey.TryGetValue(key, out var binds))
            perKey[key] = binds = [];
        return binds;
    }

    // Drops what a key that is no longer held carried.
    internal void Lift(string key)
    {
        Order.RemoveAll(held => held.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        HeldBack.Remove(key);
        Suppressed.Remove(key);
        Armed.Remove(key);
    }

    private Dictionary<string, (string Submap, Point Anchor)> Pressed(bool mouse) =>
        mouse ? _pressedButtons : _pressedKeys;
}
