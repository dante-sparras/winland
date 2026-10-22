namespace Winland.Domain;

/// <summary>Device binds: binds limited to named keyboards, which a raw input packet runs after the hook has decided the key.</summary>
public static partial class BindMatcher
{
    /// <summary>
    /// Runs non-consuming device binds for one raw packet. The key is already in the session: the hook decided the
    /// press with no keyboard, because Windows names the keyboard only after the key is delivered. A second
    /// <see cref="Decide"/> would see a repeat. <paramref name="device"/> empty matches nothing, including an
    /// exclusive bind. <paramref name="remember"/> stores the keyboard for a later repeat or release while the key
    /// is still held. <paramref name="pressedUnder"/> is the modifier mask from the press, and <paramref name="order"/>
    /// is the keys held then, for a packet that arrives after they have been let go.
    /// </summary>
    public static BindDecision DispatchDevice(
        BindTable table,
        BindSession session,
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
        ArgumentNullException.ThrowIfNull(session);
        if (key.Length == 0 || string.IsNullOrEmpty(device))
        {
            session.EventDevice = null;
            return Passed();
        }

        (string Key, int ScanCode)[]? savedOrder = null;
        if (order is not null)
        {
            savedOrder = session.Order.ToArray();
            session.Order.Clear();
            session.Order.AddRange(order);
        }

        var savedMask = session.DeviceMatchMask;
        if (!up)
            session.DeviceMatchMask = pressedUnder;

        var restoredUnder = false;
        if (up && pressedUnder is Modifier under && !session.PressedUnder.ContainsKey(key))
        {
            session.PressedUnder[key] = under;
            restoredUnder = true;
        }

        try
        {
            ApplyDevice(session, key, device, remember);
            var map = submap ?? session.Submap;
            if (repeat)
                return Ran(DeviceRepeats(table, session, key, scanCode, map), consumed: false, new BindTrigger(key, scanCode, Pressed: true));

            if (up)
            {
                return Ran(DeviceReleases(table, session, key, scanCode, map, anchor), consumed: false, new BindTrigger(key, scanCode, Pressed: false))
                    with
                    { DisarmLongPress = true };
            }

            return DevicePress(table, session, key, scanCode, map);
        }
        finally
        {
            if (savedOrder is not null)
            {
                session.Order.Clear();
                session.Order.AddRange(savedOrder);
            }

            session.DeviceMatchMask = savedMask;
            session.EventDevice = null;
            if (restoredUnder)
                session.PressedUnder.Remove(key);
        }
    }

    private static BindDecision DevicePress(BindTable table, BindSession session, string key, int scanCode, string map)
    {
        var immediate = new List<Bind>();
        var longPress = new List<Bind>();
        var sawFull = false;
        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || IsCatchAll(bind) || !DeviceKey(bind, session, key, scanCode, releasing: false))
                continue;

            var match = MatchKeys(bind, session, key, scanCode);
            if (match != KeyMatch.Full)
                continue;

            sawFull = true;
            TakeDevice(bind, session, key, immediate, longPress);
            if (DispatchesNow(bind, releasing: false) && IsSubmapDispatcher(bind))
                break;
        }

        if (!sawFull)
        {
            foreach (var bind in table.Active(map))
            {
                if (!DeviceBind(bind) || !IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse: false, releasing: false, anchor: default, catchAll: true))
                    continue;

                TakeDevice(bind, session, key, immediate, longPress);
                if (DispatchesNow(bind, releasing: false) && IsSubmapDispatcher(bind))
                    break;
            }
        }

        var decision = Ran(immediate, consumed: false, new BindTrigger(key, scanCode, Pressed: true));
        if (longPress.Count > 0 && session.LongPressKey is null && session.DownKeys.Contains(key))
        {
            session.LongPressKey = key;
            session.LongPressCode = scanCode;
            session.LongPressMouse = false;
            decision = decision with { ArmLongPressMs = table.LongPressDelay };
        }

        return decision;
    }

    private static List<Bind> DeviceRepeats(BindTable table, BindSession session, string key, int scanCode, string map)
    {
        var repeating = new List<Bind>();
        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || IsCatchAll(bind) || !Repeating(bind) || !DeviceKey(bind, session, key, scanCode, releasing: false))
                continue;
            if (MatchKeys(bind, session, key, scanCode) != KeyMatch.Full)
                continue;

            repeating.Add(bind);
        }

        if (repeating.Count > 0)
            return repeating;

        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || !IsCatchAll(bind) || !Repeating(bind))
                continue;
            if (!Include(bind, table, session, key, scanCode, mouse: false, releasing: false, anchor: default, catchAll: true))
                continue;

            repeating.Add(bind);
            if (IsSubmapDispatcher(bind))
                break;
        }

        return repeating;
    }

    private static List<Bind> DeviceReleases(BindTable table, BindSession session, string key, int scanCode, string map, Point anchor)
    {
        var fire = new List<Bind>();
        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || IsCatchAll(bind))
                continue;
            if (Include(bind, table, session, key, scanCode, mouse: false, releasing: true, anchor, catchAll: false))
                fire.Add(bind);
        }

        if (fire.Count > 0)
            return fire;

        foreach (var bind in table.Active(map))
        {
            if (!DeviceBind(bind) || !IsCatchAll(bind))
                continue;
            if (!Include(bind, table, session, key, scanCode, mouse: false, releasing: true, anchor, catchAll: true))
                continue;

            fire.Add(bind);
            if (IsSubmapDispatcher(bind))
                break;
        }

        return fire;
    }

    private static bool DeviceBind(Bind bind) =>
        bind.Devices is not null && bind.Flags.HasFlag(BindFlag.NonConsuming);

    private static bool DeviceKey(Bind bind, BindSession session, string key, int scanCode, bool releasing) =>
        Eligible(bind, session, key, scanCode, mouse: false, releasing, anyKey: true);

    private static bool Repeating(Bind bind) =>
        bind.Flags.HasFlag(BindFlag.Repeat) && !bind.Flags.HasFlag(BindFlag.Release) && !bind.Flags.HasFlag(BindFlag.LongPress);

    private static void TakeDevice(Bind bind, BindSession session, string key, List<Bind> immediate, List<Bind> longPress)
    {
        if (bind.Flags.HasFlag(BindFlag.Release) && Parts(bind).Length > 1)
        {
            Add(BindSession.Of(session.Armed, key), bind);
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

    private static BindDecision CancelLongPress(BindSession session, BindDecision decision)
    {
        session.LongPressKey = null;
        return decision with { DisarmLongPress = true };
    }

    // A bind with no device list matches every keyboard. One with a list matches only a named keyboard. An event that
    // names no keyboard matches neither. The keyboard is known only after the key is delivered, so an unknown one must
    // not run an exclusive bind.
    private static bool MatchesDevice(Bind bind, BindSession session)
    {
        if (bind.Devices is null)
            return !bind.Flags.HasFlag(BindFlag.DeviceInclusive);

        var name = session.EventDevice;
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
    private static void ApplyDevice(BindSession session, string key, string? device, bool remember)
    {
        if (string.IsNullOrEmpty(device) && session.Devices.TryGetValue(key, out var remembered))
            device = remembered;

        session.EventDevice = string.IsNullOrEmpty(device) ? null : device;
        if (!remember)
            return;

        if (string.IsNullOrEmpty(session.EventDevice))
            session.Devices.Remove(key);
        else
            session.Devices[key] = session.EventDevice;
    }
}
