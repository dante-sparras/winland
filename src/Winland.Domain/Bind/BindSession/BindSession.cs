namespace Winland.Domain;

/// <summary>
/// Held keys, caught keys, and inhibit for one input thread.
/// Keyboard and pointer events for that thread share one session.
/// </summary>
public sealed partial class BindSession
{
    private readonly HashSet<string> _downKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _downButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _caughtKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _caughtButtons = new(StringComparer.OrdinalIgnoreCase);

    // Presses the OS saw. Their releases always reach the OS too, or the key stays down there.
    private readonly HashSet<string> _passedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _passedButtons = new(StringComparer.OrdinalIgnoreCase);

    // Held Super and Alt keys that already got a mask key during this hold.
    private readonly HashSet<string> _maskedKeys = new(StringComparer.OrdinalIgnoreCase);
    // Held keys that are not modifiers, in the order they went down, with their scan codes. A bind on several keys
    // matches them in this order.
    internal readonly List<(string Key, int ScanCode)> Order = [];

    // Per held key: binds that run when it is released because a longer chord could still extend them, binds a
    // completed chord holds off, and release chords it completed in order.
    private readonly Dictionary<string, List<Bind>> _heldBack = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Bind>> _suppressed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Bind>> _armed = new(StringComparer.OrdinalIgnoreCase);
    // Release binds on one key that a held key already fully matches when another key is caught or let go. They do not
    // run on that key's release.
    private readonly HashSet<Bind> _shadowed = new(ReferenceEqualityComparer.Instance);
    // The modifiers each held key or button went down under. Its release binds match against them.
    private readonly Dictionary<string, Modifier> _pressedUnder = new(StringComparer.OrdinalIgnoreCase);
    // The scan code each held key went down with.
    private readonly Dictionary<string, int> _scanCodes = new(StringComparer.OrdinalIgnoreCase);

    // The device each held key went down on, so a release or a repeat still matches when that edge has no device.
    private readonly Dictionary<string, string> _devices = new(StringComparer.OrdinalIgnoreCase);

    // The device of the event being matched. The hook thread sets it for the call and reads it with no lock.
    private string? _eventDevice;

    // When set, a device bind matched from a raw packet uses this modifier mask instead of the keys held now.
    // The packet arrives after the hook returned, so the mask is the one from the press.
    private Modifier? _deviceMatchMask;
    public Modifier Held { get; private set; }
    private volatile bool _inhibited;
    private volatile int _buttonsHeld;
    public Point Pointer { get; private set; }
    internal string? LongPressKey { get; private set; }
    private int _longPressCode;
    private bool _longPressMouse;
    private bool _scrollSeen;
    private long _scrollAtMs;

    // The hook reads this with no lock. Empty is the default map.
    private volatile string _submap = "";

    /// <summary>The submap binds match in. Empty is the default map. Any thread may read it.</summary>
    public string Submap
    {
        get => _submap;
        set => _submap = value;
    }

    // The submap and pointer position at each held press. A click or drag bind measures from its own press.
    private readonly Dictionary<string, (string Submap, Point Anchor)> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Submap, Point Anchor)> _pressedButtons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set while binds are paused, as on the lock screen. Any thread may read or set it.</summary>
    public bool Inhibited => _inhibited;

    /// <summary>Whether a mouse button is held. Any thread may read it, while the hook thread changes the buttons.</summary>
    public bool ButtonsHeld => _buttonsHeld > 0;

    public void Inhibit(bool inhibited) => _inhibited = inhibited;

    internal int ScanCodeOf(string key) => _scanCodes.TryGetValue(key, out var code) ? code : 0;

    /// <summary>The modifiers <paramref name="key"/> went down under, or none when it is not held.</summary>
    public Modifier PressedUnderOf(string key) => _pressedUnder.TryGetValue(key, out var under) ? under : default;

    /// <summary>The held keys that are not modifiers, in the order they went down.</summary>
    public (string Key, int ScanCode)[] HeldInOrder() => [.. Order];

    /// <summary>Forgets that <paramref name="key"/> was caught, when the hook gives its press back to the OS.</summary>
    public void ForgetCaught(string key) => _caughtKeys.Remove(key);

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
        _heldBack.Remove(key);
        _suppressed.Remove(key);
        _armed.Remove(key);
    }

    private Dictionary<string, (string Submap, Point Anchor)> Pressed(bool mouse) =>
        mouse ? _pressedButtons : _pressedKeys;
}
