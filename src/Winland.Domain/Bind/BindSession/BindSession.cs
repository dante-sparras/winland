namespace Winland.Domain;

/// <summary>
/// Held keys, caught keys, and inhibit for one input thread.
/// Keyboard and pointer events for that thread share one session. The hook decides each event on it and the core
/// decides the same event again on its worker, so every change to the held state takes the session's lock.
/// </summary>
public sealed partial class BindSession
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _downKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _downButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _caughtKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _caughtButtons = new(StringComparer.OrdinalIgnoreCase);

    // Presses the OS saw. Their releases always reach the OS too, or the key stays down there.
    private readonly HashSet<string> _passedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _passedButtons = new(StringComparer.OrdinalIgnoreCase);

    // Held Super and Alt keys that already got a mask key during this hold.
    private readonly HashSet<string> _maskedKeys = new(StringComparer.OrdinalIgnoreCase);

    // Release binds on one key that a held key already fully matches when another key is caught or let go. They do not
    // run on that key's release.
    private readonly HashSet<Bind> _shadowed = new(ReferenceEqualityComparer.Instance);

    // The modifiers each held key or button went down under. Its release binds match against them.
    private readonly Dictionary<string, Modifier> _pressedUnder = new(StringComparer.OrdinalIgnoreCase);

    // The scan code each held key went down with.
    private readonly Dictionary<string, int> _scanCodes = new(StringComparer.OrdinalIgnoreCase);

    public Modifier Held { get; private set; }
    private volatile bool _inhibited;
    private volatile int _buttonsHeld;
    public Point Pointer { get; private set; }
    private bool _scrollSeen;
    private long _scrollAtMs;

    /// <summary>Set while binds are paused, as on the lock screen. Any thread may read or set it.</summary>
    public bool Inhibited => _inhibited;

    /// <summary>Whether a mouse button is held. Any thread may read it, while the hook thread changes the buttons.</summary>
    public bool ButtonsHeld => _buttonsHeld > 0;

    public void Inhibit(bool inhibited) => _inhibited = inhibited;

    internal int ScanCodeOf(string key)
    {
        lock (_gate)
            return _scanCodes.TryGetValue(key, out var code) ? code : 0;
    }

    /// <summary>Forgets that <paramref name="key"/> was caught, when the hook gives its press back to the OS.</summary>
    public void ForgetCaught(string key)
    {
        lock (_gate)
            _caughtKeys.Remove(key);
    }
}
