namespace Winland.Domain;

/// <summary>Device binds: binds limited to named keyboards, which a raw input packet runs after the hook has decided the key.</summary>
public sealed partial class BindSession
{
    /// <summary>
    /// Runs non-consuming device binds for one raw packet. The key is already in the session: the hook decided the
    /// press with no keyboard, because Windows names the keyboard only after the key is delivered. A second
    /// <see cref="Decide"/> would see a repeat. <paramref name="device"/> empty matches nothing, including an
    /// exclusive bind. <paramref name="remember"/> stores the keyboard for a later repeat or release while the key
    /// is still held. <paramref name="pressedUnder"/> is the modifier mask from the press, and <paramref name="order"/>
    /// is the keys held then, for a packet that arrives after they have been let go.
    /// </summary>
    public BindDecision DispatchDevice(
        BindTable table,
        string key,
        int scanCode,
        bool up,
        bool repeat,
        string? device,
        bool remember,
        string? submap = null,
        Modifier? pressedUnder = null,
        Point anchor = default,
        (string Key, int ScanCode)[]? order = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (key.Length == 0 || string.IsNullOrEmpty(device))
        {
            _eventDevice = null;
            return Passed();
        }

        (string Key, int ScanCode)[]? savedOrder = null;
        if (order is not null)
        {
            savedOrder = Order.ToArray();
            Order.Clear();
            Order.AddRange(order);
        }

        var savedMask = _deviceMatchMask;
        if (!up)
            _deviceMatchMask = pressedUnder;

        var restoredUnder = false;
        if (up && pressedUnder is Modifier under && !_pressedUnder.ContainsKey(key))
        {
            _pressedUnder[key] = under;
            restoredUnder = true;
        }

        try
        {
            ApplyDevice(key, device, remember);
            var map = submap ?? Submap;
            if (repeat)
                return Ran(DeviceRepeats(table, key, scanCode, map), consumed: false, new BindTrigger(key, scanCode, Pressed: true));

            if (up)
            {
                return Ran(DeviceReleases(table, key, scanCode, map, anchor), consumed: false, new BindTrigger(key, scanCode, Pressed: false))
                    with
                    { DisarmLongPress = true };
            }

            return DevicePress(table, key, scanCode, map);
        }
        finally
        {
            if (savedOrder is not null)
            {
                Order.Clear();
                Order.AddRange(savedOrder);
            }

            _deviceMatchMask = savedMask;
            _eventDevice = null;
            if (restoredUnder)
                _pressedUnder.Remove(key);
        }
    }

    private BindDecision DevicePress(BindTable table, string key, int scanCode, string map)
    {
        var immediate = new List<Bind>();
        var longPress = new List<Bind>();
        var sawFull = false;
        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || IsCatchAll(bind) || !DeviceKey(bind, key, scanCode, releasing: false))
                continue;

            var match = MatchKeys(bind, key, scanCode);
            if (match != KeyMatch.Full)
                continue;

            sawFull = true;
            TakeDevice(bind, key, immediate, longPress);
            if (DispatchesNow(bind, releasing: false) && IsSubmapDispatcher(bind))
                break;
        }

        if (!sawFull)
        {
            foreach (var bind in table.Active(map))
            {
                if (!DeviceBind(bind) || !IsCatchAll(bind) || !Include(bind, table, key, scanCode, mouse: false, releasing: false, anchor: default, catchAll: true))
                    continue;

                TakeDevice(bind, key, immediate, longPress);
                if (DispatchesNow(bind, releasing: false) && IsSubmapDispatcher(bind))
                    break;
            }
        }

        var decision = Ran(immediate, consumed: false, new BindTrigger(key, scanCode, Pressed: true));
        if (longPress.Count > 0 && LongPressKey is null && _downKeys.Contains(key))
        {
            LongPressKey = key;
            _longPressCode = scanCode;
            _longPressMouse = false;
            decision = decision with { ArmLongPressMs = table.LongPressDelay };
        }

        return decision;
    }

    private List<Bind> DeviceRepeats(BindTable table, string key, int scanCode, string map)
    {
        var repeating = new List<Bind>();
        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || IsCatchAll(bind) || !Repeating(bind) || !DeviceKey(bind, key, scanCode, releasing: false))
                continue;
            if (MatchKeys(bind, key, scanCode) != KeyMatch.Full)
                continue;

            repeating.Add(bind);
        }

        if (repeating.Count > 0)
            return repeating;

        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || !IsCatchAll(bind) || !Repeating(bind))
                continue;
            if (!Include(bind, table, key, scanCode, mouse: false, releasing: false, anchor: default, catchAll: true))
                continue;

            repeating.Add(bind);
            if (IsSubmapDispatcher(bind))
                break;
        }

        return repeating;
    }

    private List<Bind> DeviceReleases(BindTable table, string key, int scanCode, string map, Point anchor)
    {
        var fire = new List<Bind>();
        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || IsCatchAll(bind))
                continue;
            if (Include(bind, table, key, scanCode, mouse: false, releasing: true, anchor, catchAll: false))
                fire.Add(bind);
        }

        if (fire.Count > 0)
            return fire;

        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || !IsCatchAll(bind))
                continue;
            if (!Include(bind, table, key, scanCode, mouse: false, releasing: true, anchor, catchAll: true))
                continue;

            fire.Add(bind);
            if (IsSubmapDispatcher(bind))
                break;
        }

        return fire;
    }

    private static bool DeviceBind(Bind bind) =>
        bind.Devices is not null && bind.Flags.HasFlag(BindFlag.NonConsuming);

    private bool DeviceKey(Bind bind, string key, int scanCode, bool releasing) =>
        Eligible(bind, key, scanCode, mouse: false, releasing, anyKey: true);

    private static bool Repeating(Bind bind) =>
        bind.Flags.HasFlag(BindFlag.Repeat) && !bind.Flags.HasFlag(BindFlag.Release) && !bind.Flags.HasFlag(BindFlag.LongPress);

    private void TakeDevice(Bind bind, string key, List<Bind> immediate, List<Bind> longPress)
    {
        if (bind.Flags.HasFlag(BindFlag.Release) && Parts(bind).Length > 1)
        {
            Add(BindSession.Of(_armed, key), bind);
            return;
        }

        if (bind.Flags.HasFlag(BindFlag.LongPress) && !bind.Flags.HasFlag(BindFlag.Release))
        {
            longPress.Add(bind);
            return;
        }

        if (bind.Flags.HasFlag(BindFlag.Release) && !SendsBothEdges(bind))
            return;

        immediate.Add(bind);
    }

    private BindDecision CancelLongPress( BindDecision decision)
    {
        LongPressKey = null;
        return decision with { DisarmLongPress = true };
    }

    // A bind with no device list matches every keyboard. One with a list matches only a named keyboard. An event that
    // names no keyboard matches neither. The keyboard is known only after the key is delivered, so an unknown one must
    // not run an exclusive bind.
    private bool MatchesDevice(Bind bind)
    {
        if (bind.Devices is null)
            return !bind.Flags.HasFlag(BindFlag.DeviceInclusive);

        var name = _eventDevice;
        if (string.IsNullOrEmpty(name))
            return false;

        var listed = ListedDevice(bind.Devices, name);
        return bind.Flags.HasFlag(BindFlag.DeviceInclusive) ? listed : !listed;
    }

    private static bool ListedDevice(IReadOnlyList<string> devices, string name)
    {
        foreach (var device in devices)
        {
            if (device.Equals(name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // The device of this edge, or the one the key went down on when this edge has none. Remembering an unnamed edge
    // forgets the key, so a later packet can store the real keyboard. Storing a null would keep that packet from
    // being remembered.
    private void ApplyDevice( string key, string? device, bool remember)
    {
        if (string.IsNullOrEmpty(device) && _devices.TryGetValue(key, out var remembered))
            device = remembered;

        _eventDevice = string.IsNullOrEmpty(device) ? null : device;
        if (!remember)
            return;

        if (string.IsNullOrEmpty(_eventDevice))
            _devices.Remove(key);
        else
            _devices[key] = _eventDevice;
    }
}
