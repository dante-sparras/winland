namespace Winland.Core.Chords;

/// <summary>What a physical key is for.</summary>
public enum KeyRole
{
    /// <summary>The Windows key.</summary>
    Super,

    /// <summary>Shift, left or right.</summary>
    Shift,

    /// <summary>Control, left or right.</summary>
    Ctrl,

    /// <summary>Alt, left or right.</summary>
    Alt,

    /// <summary>A key that can appear in a Chord.</summary>
    Bindable,

    /// <summary>Any other key.</summary>
    Plain,
}

/// <summary>One physical key. Left and right modifiers are different ids.</summary>
/// <param name="Id">Stable id for this key. The Windows hook uses the virtual-key code.</param>
/// <param name="Role">How the ledger treats it.</param>
/// <param name="Key">The chord key when <paramref name="Role"/> is <see cref="KeyRole.Bindable"/>.</param>
public readonly record struct PhysicalKey(int Id, KeyRole Role, ChordKey? Key)
{
    /// <summary>A Windows key.</summary>
    public static PhysicalKey Super(int id) => new(id, KeyRole.Super, null);

    /// <summary>A Shift key.</summary>
    public static PhysicalKey Shift(int id) => new(id, KeyRole.Shift, null);

    /// <summary>A Control key.</summary>
    public static PhysicalKey Ctrl(int id) => new(id, KeyRole.Ctrl, null);

    /// <summary>An Alt key.</summary>
    public static PhysicalKey Alt(int id) => new(id, KeyRole.Alt, null);

    /// <summary>A key Winoma might bind.</summary>
    public static PhysicalKey Bindable(int id, ChordKey key) => new(id, KeyRole.Bindable, key);

    /// <summary>A key Winoma does not bind.</summary>
    public static PhysicalKey Plain(int id) => new(id, KeyRole.Plain, null);
}

/// <summary>A synthetic key-down or key-up to send after the hook has returned.</summary>
/// <param name="Id">The physical key id.</param>
/// <param name="Down">True for key-down.</param>
public readonly record struct KeyPulse(int Id, bool Down);

/// <summary>A chord the shell should run.</summary>
/// <param name="Chord">The combination.</param>
/// <param name="Down">True for the press. A held chord also queues the release.</param>
public readonly record struct QueuedChord(Chord Chord, bool Down);

/// <summary>What the hook does with one physical key event.</summary>
/// <param name="Deliver">True when Windows must see this event.</param>
/// <param name="Queued">A chord to run, when Winoma owns it.</param>
/// <param name="Inject">Synthetic keys, sent only after the hook returns.</param>
/// <param name="Dropped">True when the waiting queue was discarded so this effect could proceed.</param>
public sealed record GateEffect(bool Deliver, QueuedChord? Queued, IReadOnlyList<KeyPulse> Inject, bool Dropped = false)
{
    private static readonly IReadOnlyList<KeyPulse> NoPulses = [];

    /// <summary>Windows does not see the event, and nothing is queued.</summary>
    public static GateEffect Swallow { get; } = new(false, null, NoPulses);

    /// <summary>Windows sees the event.</summary>
    public static GateEffect Pass { get; } = new(true, null, NoPulses);
}

/// <summary>
/// Decides what Windows and Winoma each see for every physical key.
/// The keys this ledger has seen are the account of Super. Windows reporting Super up proves nothing,
/// because a swallowed Super never reaches it. A Super press another hook handed to Windows is adopted with <see cref="AdoptSuper"/>.
/// A Super down is injected only in the same run as its key-up, and only for a chord Winoma does not own.
/// A bare tap sends nothing, so the Windows Start menu stays closed.
/// A key-up reaches Windows only when its key-down did.
/// A key Windows was given is held only while Windows holds it. The hook is not called for a key-up that goes to an administrator window,
/// so the next key-down forgets each such key Windows reports up, before it can count as a chord's modifier.
/// A session lock drops every finger, because the key-up may never arrive.
/// While delivery is withheld, a key that would reach Windows waits for <see cref="ReleaseWithheld"/>, for at most <see cref="WithholdLimitMs"/>.
/// </summary>
public sealed class ChordLedger
{
    /// <summary>How many withheld pulses wait. The effect that does not fit is performed, and the queue is dropped.</summary>
    public const int WithholdLimit = 64;

    /// <summary>How long one stretch of withholding lasts before waiting keys go to whatever window has focus.</summary>
    public const long WithholdLimitMs = 1000;

    private readonly object _sync = new();
    private readonly Dictionary<int, Held> _fingers = [];

    // Kept beside the fingers, so HoldingSuper never takes the lock. Written only under the lock, by the three methods below.
    private bool _holdingSuper;
    private bool _notedSuper;
    private readonly List<int> _order = [];
    private readonly Queue<KeyPulse> _waiting = new();
    private readonly HashSet<int> _pendingDown = [];
    private readonly HashSet<int> _dropped = [];
    private bool _withhold;
    private long? _withheldSince;
    private int _waitingPulses;
    private IBindTable _map;
    private readonly Func<int, bool>? _windowsHolds;
    private int? _superId;
    private int? _holdId;
    private Chord _holdChord;

    /// <summary>Uses <paramref name="chords"/> for both ordinary bindings and hold keys.</summary>
    /// <param name="chords">The binds. A chord resolves when the table returns a dispatch.</param>
    /// <param name="windowsHolds">
    /// Asks Windows whether the key with this id is down. It is asked only about a key Windows was given, and never about Super.
    /// It runs under the ledger's lock, inside <see cref="OnKey"/> and <see cref="KeyHeld"/>, so it must only read.
    /// Null keeps every finger until its key-up.
    /// </param>
    public ChordLedger(IBindTable chords, Func<int, bool>? windowsHolds = null)
    {
        ArgumentNullException.ThrowIfNull(chords);
        _map = chords;
        _windowsHolds = windowsHolds;
    }

    /// <summary>True while a Super finger is recorded. Windows is not asked. Read without the lock, since the hook asks on every key.</summary>
    public bool HoldingSuper => Volatile.Read(ref _holdingSuper);

    /// <summary>True when no finger is recorded. The hook may be installed again only then.</summary>
    public bool Idle
    {
        get
        {
            lock (_sync)
            {
                return _fingers.Count == 0 && _pendingDown.Count == 0;
            }
        }
    }

    /// <summary>
    /// True while a key other than Super is down. A key Windows was given counts only while Windows still holds it.
    /// Nothing is forgotten here, so this may be asked from any thread, between keys.
    /// </summary>
    /// <returns>True when a swallowed key is recorded, or a delivered one that Windows still holds.</returns>
    public bool KeyHeld()
    {
        lock (_sync)
        {
            foreach (var held in _fingers.Values)
            {
                if (held.Key.Role != KeyRole.Super && !Released(held))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// True while the key with this id is a finger whose key-down was kept from Windows.
    /// A key Windows was given is not one, and neither is a key that waits for <see cref="ReleaseWithheld"/>.
    /// The hook asks before it lets another program's key-up pass: that key-up must come here, or the finger stays.
    /// It runs inside the hook callback, so it only reads and allocates nothing.
    /// </summary>
    /// <param name="id">Physical key id.</param>
    /// <returns>True when <see cref="OnKey"/> would swallow this key's key-up and forget the finger.</returns>
    public bool HoldsSwallowed(int id)
    {
        lock (_sync)
        {
            return _fingers.TryGetValue(id, out var held) && !held.Passed && !_pendingDown.Contains(id);
        }
    }

    /// <summary>True when <see cref="ReleaseWithheld"/> has keys waiting.</summary>
    public bool HasWithheld
    {
        get
        {
            lock (_sync)
            {
                return _waitingPulses > 0;
            }
        }
    }

    /// <summary>
    /// While <paramref name="withhold"/> is true, a key that would reach Windows waits.
    /// The hook reports this. The ledger does not learn why.
    /// A stretch of withholding ends after <see cref="WithholdLimitMs"/> even when the hook still reports it,
    /// so a focus that never lands cannot keep typing from Windows. The next stretch starts once the hook reports false.
    /// No stretch starts while a Super finger is recorded. A Workspace switch is made with Super held, and one to an empty Workspace
    /// leaves the keyboard on the window Winoma just hid until Super goes up (ADR 0020). Nothing typed reaches a window while Super is down,
    /// so nothing has to wait, and a Windows chord pressed then is not stalled.
    /// </summary>
    /// <param name="withhold">True while Windows must not see a waiting key yet.</param>
    /// <param name="now">Tick count, the same clock passed to <see cref="OnKey"/>.</param>
    /// <returns>True on the report that ends a stretch on the time limit while keys were waiting. A stretch nothing waited in ends silently.</returns>
    public bool Withhold(bool withhold, long now)
    {
        lock (_sync)
        {
            if (!withhold)
            {
                _withheldSince = null;
                _withhold = false;
                return false;
            }

            if (_withheldSince is null && _holdingSuper)
            {
                return false;
            }

            _withheldSince ??= now;
            var lapsed = now - _withheldSince.Value >= WithholdLimitMs;
            var ended = _withhold && lapsed && _waitingPulses > 0;
            _withhold = !lapsed;
            return ended;
        }
    }

    /// <summary>Replaces the table. Call this before the hook is installed.</summary>
    public void Use(IBindTable chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        lock (_sync)
        {
            _map = chords;
        }
    }

    /// <summary>One physical key went down or up.</summary>
    /// <param name="key">The key.</param>
    /// <param name="down">True for key-down, including auto-repeat.</param>
    /// <param name="now">Tick count.</param>
    /// <returns>What the hook should do after it returns.</returns>
    public GateEffect OnKey(PhysicalKey key, bool down, long now)
    {
        lock (_sync)
        {
            // A Super key-up the hook never saw is applied here, before this key is read as a chord,
            // the same moment a delivered key is forgotten. Windows is not asked.
            ApplyNotedSuper();
            if (down && !_fingers.ContainsKey(key.Id))
            {
                ForgetReleased();
            }

            if (_dropped.Contains(key.Id))
            {
                if (!down)
                {
                    _dropped.Remove(key.Id);
                }

                return GateEffect.Swallow;
            }

            if (down && _fingers.TryGetValue(key.Id, out var held))
            {
                if (_withhold)
                {
                    return GateEffect.Swallow;
                }

                return held.Passed ? GateEffect.Pass : GateEffect.Swallow;
            }

            if (!down && _pendingDown.Contains(key.Id))
            {
                var chord = Queue(key, false);
                Release(key.Id);
                _pendingDown.Remove(key.Id);
                return Admit(false, chord, [new KeyPulse(key.Id, false)], key, false, false, true);
            }

            if (!down && !_fingers.ContainsKey(key.Id))
            {
                return Admit(key.Role != KeyRole.Super, null, GateEffect.Swallow.Inject, key, key.Role != KeyRole.Super, false, false);
            }

            if (key.Role == KeyRole.Super)
            {
                return OnSuper(key, down, now);
            }

            if (!down)
            {
                var deliver = _fingers[key.Id].Passed;
                var chord = Queue(key, false);
                Release(key.Id);
                return Admit(deliver, chord, GateEffect.Swallow.Inject, key, deliver, false, false);
            }

            var super = SuperHeld();
            SetFinger(key.Id, new Held(key, false, now));
            _order.Add(key.Id);
            var queued = Queue(key, true);
            var owned = queued is not null;
            if (super && key.Role == KeyRole.Bindable && !owned)
            {
                return Admit(false, null, Replay(key.Id), key, false, false, false);
            }

            if (owned || super)
            {
                return Admit(false, queued, GateEffect.Swallow.Inject, key, false, false, false);
            }

            SetFinger(key.Id, new Held(key, true, now));
            return Admit(true, queued, GateEffect.Swallow.Inject, key, true, true, false);
        }
    }

    /// <summary>
    /// Keys that waited while <see cref="Withhold"/> was set.
    /// Empty while delivery is still withheld, and empty when nothing is waiting.
    /// A down that is still held is marked seen. A down whose up is in the same result is not.
    /// </summary>
    /// <returns>Pulses to inject, in the order the keys were withheld.</returns>
    public GateEffect ReleaseWithheld()
    {
        lock (_sync)
        {
            if (_withhold || _waitingPulses == 0)
            {
                return GateEffect.Swallow;
            }

            var pulses = new KeyPulse[_waitingPulses];
            for (var i = 0; i < pulses.Length; i++)
            {
                pulses[i] = _waiting.Dequeue();
            }

            _waitingPulses = 0;
            foreach (var id in _pendingDown)
            {
                if (_fingers.TryGetValue(id, out var held))
                {
                    SetFinger(id, new Held(held.Key, true, held.DownAt));
                }
            }

            _pendingDown.Clear();
            return new GateEffect(false, null, pulses);
        }
    }

    /// <summary>
    /// Drops every finger. A session lock or unlock hides the matching key-up.
    /// The result asks for a key-up of each Super finger and of each key Windows was given.
    /// A key-up that was waiting, for a down Windows already has, is included.
    /// A swallowed key is forgotten. Windows never saw it.
    /// </summary>
    /// <returns>Key-ups to inject, and a hold-key release when one was down.</returns>
    public GateEffect ReleaseForLock()
    {
        lock (_sync)
        {
            var pulses = new List<KeyPulse>();
            CollectWaiting(new HashSet<int>(), pulses);
            ForgetWaiting();
            QueuedChord? queued = null;
            if (_holdId is int)
            {
                queued = new QueuedChord(_holdChord, false);
                _holdId = null;
            }

            for (var i = _order.Count - 1; i >= 0; i--)
            {
                var id = _order[i];
                if (!_fingers.TryGetValue(id, out var held))
                {
                    continue;
                }

                if (held.Key.Role == KeyRole.Super || held.Passed)
                {
                    pulses.Add(new KeyPulse(id, false));
                }
            }

            ClearFingers();
            _order.Clear();
            _superId = null;
            _notedSuper = false;
            return pulses.Count == 0 && queued is null
                ? GateEffect.Swallow
                : new GateEffect(false, queued, pulses);
        }
    }

    /// <summary>
    /// Forgets every finger and injects nothing.
    /// The elevated chord hook calls this when the desktop hook has the keyboard again.
    /// A swallowed Super never reached Windows, so a key-up here would be a second account of it.
    /// </summary>
    public void Abandon()
    {
        lock (_sync)
        {
            ForgetWaiting();
            ClearFingers();
            _order.Clear();
            _superId = null;
            _holdId = null;
            _notedSuper = false;
        }
    }

    /// <summary>
    /// Records a Super press that reached Windows without passing through this ledger.
    /// Another keyboard hook handed it to Windows, so Windows holds it.
    /// The press still chords, and its key-up is delivered so Windows lets go.
    /// </summary>
    /// <param name="key">The Windows key.</param>
    /// <param name="now">Tick count.</param>
    /// <returns>True when the press was not recorded before. The caller then masks Start.</returns>
    public bool AdoptSuper(PhysicalKey key, long now)
    {
        lock (_sync)
        {
            if (key.Role != KeyRole.Super || _fingers.ContainsKey(key.Id))
            {
                return false;
            }

            SetFinger(key.Id, new Held(key, true, now));
            _order.Add(key.Id);
            _superId = key.Id;
            return true;
        }
    }

    /// <summary>
    /// Notes that a Super key-up reached Windows without passing through this ledger.
    /// The next key applies it, before that key is read as a chord. Nothing is injected. Windows already has the key-up.
    /// </summary>
    public void NoteSuperUp()
    {
        lock (_sync)
        {
            _notedSuper = true;
        }
    }

    /// <summary>
    /// Forgets a Super press whose key-up reached Windows without passing through this ledger.
    /// Nothing is injected. Windows already has the key-up.
    /// </summary>
    /// <param name="id">Physical key id.</param>
    /// <returns>True when a recorded Super press was forgotten.</returns>
    public bool ForgetSuper(int id)
    {
        lock (_sync)
        {
            if (!_fingers.TryGetValue(id, out var held) || held.Key.Role != KeyRole.Super)
            {
                return false;
            }

            Release(id);
            return true;
        }
    }

    private GateEffect OnSuper(PhysicalKey key, bool down, long now)
    {
        if (down)
        {
            SetFinger(key.Id, new Held(key, false, now));
            _order.Add(key.Id);
            _superId = key.Id;
            return GateEffect.Swallow;
        }

        var passed = _fingers.TryGetValue(key.Id, out var held) && held.Passed;
        Release(key.Id);
        if (!passed)
        {
            return GateEffect.Swallow;
        }

        return Admit(true, null, GateEffect.Swallow.Inject, key, true, false, false);
    }

    private QueuedChord? Queue(PhysicalKey key, bool down)
    {
        if (key.Role != KeyRole.Bindable || key.Key is not ChordKey chordKey)
        {
            return null;
        }

        if (!down)
        {
            if (_holdId != key.Id)
            {
                return null;
            }

            _holdId = null;
            return new QueuedChord(_holdChord, false);
        }

        var chord = new Chord(Modifiers(), chordKey);
        if (_map.Resolve(chord) is not null)
        {
            return new QueuedChord(chord, true);
        }

        if (!_map.IsHold(chord))
        {
            return null;
        }

        if (_holdId is int held && held != key.Id)
        {
            return new QueuedChord(_holdChord, true);
        }

        _holdId = key.Id;
        _holdChord = chord;
        return new QueuedChord(chord, true);
    }

    private KeyPulse[] Replay(int keyId)
    {
        var downs = new List<int>();
        if (_superId is int super && !(_fingers.TryGetValue(super, out var superHeld) && superHeld.Passed))
        {
            downs.Add(super);
        }

        foreach (var id in _order)
        {
            if (id == keyId || !_fingers.TryGetValue(id, out var held) || held.Passed)
            {
                continue;
            }

            if (held.Key.Role is KeyRole.Shift or KeyRole.Ctrl or KeyRole.Alt)
            {
                downs.Add(id);
            }
        }

        downs.Add(keyId);
        var pulses = new KeyPulse[downs.Count * 2];
        for (var i = 0; i < downs.Count; i++)
        {
            pulses[i] = new KeyPulse(downs[i], true);
            pulses[pulses.Length - 1 - i] = new KeyPulse(downs[i], false);
        }

        return pulses;
    }

    private ChordModifiers Modifiers()
    {
        var modifiers = ChordModifiers.None;
        foreach (var held in _fingers.Values)
        {
            modifiers |= held.Key.Role switch
            {
                KeyRole.Super => ChordModifiers.Super,
                KeyRole.Shift => ChordModifiers.Shift,
                KeyRole.Ctrl => ChordModifiers.Ctrl,
                KeyRole.Alt => ChordModifiers.Alt,
                _ => ChordModifiers.None,
            };
        }

        return modifiers;
    }

    private void SetFinger(int id, Held held)
    {
        _fingers[id] = held;
        Volatile.Write(ref _holdingSuper, SuperHeld());
    }

    private void RemoveFinger(int id)
    {
        _fingers.Remove(id);
        Volatile.Write(ref _holdingSuper, SuperHeld());
    }

    private void ClearFingers()
    {
        _fingers.Clear();
        Volatile.Write(ref _holdingSuper, false);
    }

    private bool SuperHeld()
    {
        foreach (var held in _fingers.Values)
        {
            if (held.Key.Role == KeyRole.Super)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when Windows was given this key and no longer holds it, so its key-up went past the hook.
    /// Super is never asked about: a swallowed Super is up for Windows through the whole hold.
    /// </summary>
    private bool Released(Held held) =>
        held.Passed && held.Key.Role != KeyRole.Super && _windowsHolds is { } holds && !holds(held.Key.Id);

    /// <summary>Drops every Super finger a missed key-up noted. The next key calls this before it reads modifiers.</summary>
    private void ApplyNotedSuper()
    {
        if (!_notedSuper)
        {
            return;
        }

        _notedSuper = false;
        for (var i = _order.Count - 1; i >= 0; i--)
        {
            var id = _order[i];
            if (_fingers.TryGetValue(id, out var held) && held.Key.Role == KeyRole.Super)
            {
                Release(id);
            }
        }
    }

    /// <summary>
    /// Forgets each key whose key-up went past the hook. A new key-down is when this runs: the hook is being called again,
    /// Windows has finished every earlier key, and the fingers are about to be read as a chord's modifiers.
    /// </summary>
    private void ForgetReleased()
    {
        if (_windowsHolds is null)
        {
            return;
        }

        for (var i = _order.Count - 1; i >= 0; i--)
        {
            var id = _order[i];
            if (_fingers.TryGetValue(id, out var held) && Released(held))
            {
                Release(id);
            }
        }
    }

    private void Release(int id)
    {
        RemoveFinger(id);
        _order.Remove(id);
        if (_superId != id)
        {
            return;
        }

        _superId = null;
        foreach (var held in _fingers.Values)
        {
            if (held.Key.Role == KeyRole.Super)
            {
                _superId = held.Key.Id;
                return;
            }
        }
    }

    private GateEffect Admit(
        bool deliver,
        QueuedChord? queued,
        IReadOnlyList<KeyPulse> inject,
        PhysicalKey live,
        bool includeLive,
        bool liveDown,
        bool forceQueue)
    {
        if (!_withhold && !forceQueue)
        {
            // Plain typing lands here on every key. The shared effects keep the hook from allocating.
            if (queued is null && inject.Count == 0)
            {
                return deliver ? GateEffect.Pass : GateEffect.Swallow;
            }

            return new GateEffect(deliver, queued, inject);
        }

        var pulses = new List<KeyPulse>(inject);
        if (includeLive && deliver)
        {
            pulses.Add(new KeyPulse(live.Id, liveDown));
        }

        if (pulses.Count == 0)
        {
            return new GateEffect(false, queued, inject);
        }

        if (_waitingPulses + pulses.Count > WithholdLimit)
        {
            var dropped = DropWaiting(out var release);
            if (pulses.Count == 1 && !pulses[0].Down && dropped.Contains(pulses[0].Id))
            {
                _dropped.Remove(pulses[0].Id);
                return new GateEffect(false, queued, release, true);
            }

            return new GateEffect(deliver, queued, Join(release, inject), true);
        }

        foreach (var pulse in pulses)
        {
            _waiting.Enqueue(pulse);
        }

        _waitingPulses += pulses.Count;
        if (includeLive && deliver && liveDown && _fingers.TryGetValue(live.Id, out var held))
        {
            SetFinger(live.Id, new Held(held.Key, false, held.DownAt));
            _pendingDown.Add(live.Id);
        }

        return new GateEffect(false, queued, GateEffect.Swallow.Inject);
    }

    private HashSet<int> DropWaiting(out IReadOnlyList<KeyPulse> release)
    {
        var unmatched = new HashSet<int>();
        var ups = new List<KeyPulse>();
        CollectWaiting(unmatched, ups);
        foreach (var id in unmatched)
        {
            _dropped.Add(id);
            Release(id);
        }

        _waiting.Clear();
        _waitingPulses = 0;
        _pendingDown.Clear();
        release = ups.Count == 0 ? GateEffect.Swallow.Inject : ups;
        return unmatched;
    }

    private void CollectWaiting(HashSet<int> unmatchedDown, List<KeyPulse> loneUps)
    {
        var seen = new HashSet<int>();
        foreach (var pulse in _waiting)
        {
            if (pulse.Down)
            {
                unmatchedDown.Add(pulse.Id);
                continue;
            }

            if (!unmatchedDown.Remove(pulse.Id) && seen.Add(pulse.Id))
            {
                loneUps.Add(new KeyPulse(pulse.Id, false));
            }
        }
    }

    private static IReadOnlyList<KeyPulse> Join(IReadOnlyList<KeyPulse> first, IReadOnlyList<KeyPulse> second)
    {
        if (first.Count == 0)
        {
            return second;
        }

        if (second.Count == 0)
        {
            return first;
        }

        var joined = new KeyPulse[first.Count + second.Count];
        for (var i = 0; i < first.Count; i++)
        {
            joined[i] = first[i];
        }

        for (var i = 0; i < second.Count; i++)
        {
            joined[first.Count + i] = second[i];
        }

        return joined;
    }

    private void ForgetWaiting()
    {
        _waiting.Clear();
        _waitingPulses = 0;
        _pendingDown.Clear();
        _dropped.Clear();
    }

    private readonly record struct Held(PhysicalKey Key, bool Passed, long DownAt);
}
