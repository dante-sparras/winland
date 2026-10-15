namespace Winland;

public readonly record struct BindDispatch(string Dispatcher, string Arguments)
{
    /// <summary>Submap to enter after this dispatcher when the dispatcher left the map unchanged.</summary>
    public string? SubmapReset { get; init; }
}

public readonly record struct BindDecision(bool Consumed, string? Dispatcher, string Arguments = "")
{
    public IReadOnlyList<BindDispatch> Dispatches { get; init; } = [];

    /// <summary>The key edge that ran these dispatchers. Null for a wheel notch.</summary>
    public BindTrigger? Trigger { get; init; }

    /// <summary>Start the long-press timer. The hook thread arms it; matching itself does not wait.</summary>
    public int? ArmLongPressMs { get; init; }

    /// <summary>Cancel a long press armed earlier. A repeat leaves this clear so the timer can elapse.</summary>
    public bool DisarmLongPress { get; init; }

    /// <summary>
    /// Inject a mask key now, while a Super or Alt the OS saw is still held. This chord was
    /// swallowed, so without it the shell takes the modifier's release as a lone tap and
    /// opens Start or a menu bar. The release itself always reaches the OS.
    /// </summary>
    public bool Mask { get; init; }
}

/// <summary>Matches a key or pointer button against a bind table. Takes no lock.</summary>
public static class BindMatcher
{
    internal const int LongPressDelayMs = 500;

    private static readonly Dictionary<string, Modifier> ModifierKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Super_L"] = Modifier.Super,
        ["Super_R"] = Modifier.Super,
        ["Alt_L"] = Modifier.Alt,
        ["Alt_R"] = Modifier.Alt,
        ["Control_L"] = Modifier.Ctrl,
        ["Control_R"] = Modifier.Ctrl,
        ["Shift_L"] = Modifier.Shift,
        ["Shift_R"] = Modifier.Shift,
    };

    // Settings under binds: that are read. Add a keyword here when it is applied.
    internal static IReadOnlySet<string> Keywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "binds:drag_threshold",
        "binds:scroll_event_delay",
        "binds:window_direction_monitor_fallback",
    };

    public static BindDecision Decide(BindTable table, BindSession session, Input input)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);
        return input switch
        {
            KeyPressed pressed => OnPress(table, session, pressed.Key, pressed.ScanCode, mouse: false),
            KeyReleased released => OnRelease(table, session, released.Key, released.ScanCode, mouse: false),
            PointerButtonPressed button => OnButton(table, session, button.Button, button.Position, up: false),
            PointerButtonReleased button => OnButton(table, session, button.Button, button.Position, up: true),
            PointerMoved moved => OnMoved(session, moved.Position),
            WheelTurned wheel => OnWheel(table, session, wheel),
            LongPressElapsed => OnLongPress(table, session),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };
    }

    /// <summary>
    /// Modifiers the OS holds down that this session did not see pressed. A key held by the
    /// user is seen, by its press or by its repeats, so these are left over from a lost release.
    /// </summary>
    public static IReadOnlyList<string> Stale(BindSession session, IEnumerable<string> logicallyDown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(logicallyDown);
        var stale = new List<string>();
        foreach (var key in logicallyDown)
        {
            if (ModifierKeys.ContainsKey(key) && !session.DownKeys.Contains(key))
                stale.Add(key);
        }

        return stale;
    }

    /// <summary>Drops held keys whose release the session missed, such as one on the lock screen.</summary>
    public static void Forget(BindSession session, Func<string, bool> stillDown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stillDown);
        Drop(session.DownKeys, session.CaughtKeys, session.PassedKeys, stillDown);
        Drop(session.DownButtons, session.CaughtButtons, session.PassedButtons, stillDown);
        session.MaskedKeys.IntersectWith(session.DownKeys);
        session.Held = 0;
        foreach (var key in session.DownKeys)
        {
            if (ModifierKeys.TryGetValue(key, out var modifier))
                session.Held |= modifier;
        }

        if (session.LongPressKey is string pending && !(session.LongPressMouse ? session.DownButtons : session.DownKeys).Contains(pending))
            session.LongPressKey = null;
    }

    private static void Drop(HashSet<string> down, HashSet<string> caught, HashSet<string> passed, Func<string, bool> stillDown)
    {
        foreach (var key in down.ToArray())
        {
            if (stillDown(key))
                continue;

            down.Remove(key);
            caught.Remove(key);
            passed.Remove(key);
        }
    }

    private static BindDecision OnButton(BindTable table, BindSession session, string button, Point position, bool up)
    {
        session.Pointer = position;
        return up
            ? OnRelease(table, session, button, scanCode: 0, mouse: true)
            : OnPress(table, session, button, scanCode: 0, mouse: true);
    }

    private static BindDecision OnMoved(BindSession session, Point position)
    {
        session.Pointer = position;
        return Passed();
    }

    private static BindDecision OnPress(BindTable table, BindSession session, string key, int scanCode, bool mouse)
    {
        if (key.Length == 0)
            return Passed();

        var down = mouse ? session.DownButtons : session.DownKeys;
        var repeat = !down.Add(key);
        if (!mouse && ModifierKeys.TryGetValue(key, out var modifier))
            session.Held |= modifier;

        if (repeat)
            return OnRepeat(table, session, key, scanCode, mouse);

        session.Anchor = session.Pointer;
        session.Remember(key, mouse);
        var immediate = new List<Bind>();
        var deferred = new List<Bind>();
        var longPress = new List<Bind>();
        foreach (var bind in Collect(table, session, key, scanCode, mouse, releasing: false, session.Submap))
        {
            if (bind.Flags.HasFlag(BindFlag.LongPress) && !bind.Flags.HasFlag(BindFlag.Release))
                longPress.Add(bind);
            else if (bind.Flags.HasFlag(BindFlag.Release) && !SendsBothEdges(bind))
                deferred.Add(bind);
            else
                immediate.Add(bind);
        }

        var passed = mouse ? session.PassedButtons : session.PassedKeys;
        if (immediate.Count == 0 && deferred.Count == 0 && longPress.Count == 0)
        {
            passed.Add(key);
            return CancelLongPress(session, Passed());
        }

        // A long press has not fired yet, so it does not consume the chord on the way down.
        var consumed = Consumes(immediate) || Consumes(deferred);
        var trigger = new BindTrigger(key, scanCode, Pressed: true);
        if (consumed)
            Catch(session, key, mouse);
        else
            passed.Add(key);

        if (longPress.Count == 0)
        {
            session.LongPressKey = null;
            return MaskHeld(session, Ran(immediate, consumed, trigger)) with { DisarmLongPress = true };
        }

        session.LongPressKey = key;
        session.LongPressMouse = mouse;
        return MaskHeld(session, Ran(immediate, consumed, trigger)) with
        {
            ArmLongPressMs = LongPressDelayMs,
            DisarmLongPress = true,
        };
    }

    private static BindDecision OnRepeat(BindTable table, BindSession session, string key, int scanCode, bool mouse)
    {
        var caught = mouse ? session.CaughtButtons : session.CaughtKeys;
        var repeating = new List<Bind>();
        foreach (var bind in Collect(table, session, key, scanCode, mouse, releasing: false, session.Remembered(key, mouse)))
        {
            if (bind.Flags.HasFlag(BindFlag.Repeat) && !bind.Flags.HasFlag(BindFlag.Release) && !bind.Flags.HasFlag(BindFlag.LongPress))
                repeating.Add(bind);
        }

        if (repeating.Count == 0)
            return caught.Contains(key) ? Caught() : Passed();

        if (Consumes(repeating))
            Catch(session, key, mouse);

        // A key already caught stays swallowed. A non-consuming repeat does not catch a key on its own.
        var trigger = new BindTrigger(key, scanCode, Pressed: true);
        return MaskHeld(session, Ran(repeating, Consumes(repeating) || caught.Contains(key), trigger));
    }

    private static BindDecision OnRelease(BindTable table, BindSession session, string key, int scanCode, bool mouse)
    {
        var down = mouse ? session.DownButtons : session.DownKeys;
        if (key.Length == 0 || !down.Remove(key))
            return Passed();

        if (!mouse)
            ReleaseModifier(session, key);

        var caughtSet = mouse ? session.CaughtButtons : session.CaughtKeys;
        var caught = caughtSet.Remove(key);
        var passed = (mouse ? session.PassedButtons : session.PassedKeys).Remove(key);
        if (!mouse)
            session.MaskedKeys.Remove(key);

        var fire = Collect(table, session, key, scanCode, mouse, releasing: true, session.Forget(key, mouse));
        session.LongPressKey = null;

        // The OS saw this press, so it must see the release, or it keeps the key down.
        var trigger = new BindTrigger(key, scanCode, Pressed: false);
        var decision = fire.Count > 0 ? Ran(fire, Consumes(fire) || caught, trigger) : caught ? Caught() : Passed();
        if (passed)
            decision = decision with { Consumed = false };

        return decision with { DisarmLongPress = true };
    }

    // A swallowed chord under a Super or Alt the OS saw gets one mask per hold.
    private static BindDecision MaskHeld(BindSession session, BindDecision decision)
    {
        if (!decision.Consumed)
            return decision;

        var mask = false;
        foreach (var key in session.PassedKeys)
        {
            if (ModifierKeys.TryGetValue(key, out var modifier) && modifier is Modifier.Super or Modifier.Alt && session.MaskedKeys.Add(key))
                mask = true;
        }

        return mask ? decision with { Mask = true } : decision;
    }

    private static BindDecision OnWheel(BindTable table, BindSession session, WheelTurned wheel)
    {
        if (wheel.Key.Length == 0)
            return Passed();

        if (session.ScrollSeen && wheel.TimeMs - session.ScrollAtMs < table.ScrollDelay)
            return CancelLongPress(session, Passed());

        session.ScrollSeen = true;
        session.ScrollAtMs = wheel.TimeMs;
        var pressing = new List<Bind>();
        foreach (var bind in Collect(table, session, wheel.Key, scanCode: 0, mouse: false, releasing: false, session.Submap))
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) && !bind.Flags.HasFlag(BindFlag.LongPress))
                pressing.Add(bind);
        }

        if (pressing.Count == 0)
            return CancelLongPress(session, Passed());

        return MaskHeld(session, Ran(pressing, Consumes(pressing), trigger: null)) with { DisarmLongPress = true };
    }

    private static BindDecision OnLongPress(BindTable table, BindSession session)
    {
        if (session.LongPressKey is not string key)
            return Passed();

        var mouse = session.LongPressMouse;
        session.LongPressKey = null;
        var down = mouse ? session.DownButtons : session.DownKeys;
        if (!down.Contains(key))
            return new BindDecision(false, null) { DisarmLongPress = true };

        var longPress = new List<Bind>();
        foreach (var bind in Collect(table, session, key, scanCode: 0, mouse, releasing: false, session.Remembered(key, mouse)))
        {
            if (bind.Flags.HasFlag(BindFlag.LongPress) && !bind.Flags.HasFlag(BindFlag.Release))
                longPress.Add(bind);
        }

        if (longPress.Count == 0)
            return new BindDecision(false, null) { DisarmLongPress = true };

        var trigger = new BindTrigger(key, Code: 0, Pressed: true);
        return MaskHeld(session, Ran(longPress, Consumes(longPress), trigger)) with { DisarmLongPress = true };
    }

    private static List<Bind> Collect(BindTable table, BindSession session, string key, int scanCode, bool mouse, bool releasing, string submap)
    {
        var matches = new List<Bind>();
        foreach (var bind in table.Active(submap))
        {
            if (IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse, releasing, catchAll: false))
                continue;

            matches.Add(bind);
            if (DispatchesNow(bind, releasing) && IsSubmapDispatcher(bind))
                break;
        }

        if (matches.Count == 0 && submap == session.Submap)
        {
            foreach (var bind in table.Active(submap))
            {
                if (!IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse, releasing, catchAll: true))
                    continue;

                matches.Add(bind);
                if (DispatchesNow(bind, releasing) && IsSubmapDispatcher(bind))
                    break;
            }
        }

        return matches;
    }

    private static bool Include(Bind bind, BindTable table, BindSession session, string key, int scanCode, bool mouse, bool releasing, bool catchAll)
    {
        if (!Eligible(bind, session, key, scanCode, mouse, releasing, catchAll))
            return false;

        if (releasing)
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) && !SendsBothEdges(bind))
                return false;
            if (!WithinGesture(table, session, bind))
                return false;
        }

        return true;
    }

    // Mouse, modifiers, the key, the locked flag, and the gesture flags.
    private static bool Eligible(Bind bind, BindSession session, string key, int scanCode, bool mouse, bool releasing, bool catchAll)
    {
        if (IsMouseBind(bind) != mouse)
            return false;
        if (!bind.Flags.HasFlag(BindFlag.IgnoreModifiers) && bind.Modifiers != MaskFor(session, key, mouse))
            return false;
        if (!catchAll && !KeyMatches(bind, key, scanCode))
            return false;
        if (session.Inhibited && !bind.Flags.HasFlag(BindFlag.Locked))
            return false;
        if (!releasing && bind.Flags.HasFlag(BindFlag.Release) && bind.Flags.HasFlag(BindFlag.LongPress))
            return false;

        return true;
    }

    private static bool IsMouseBind(Bind bind) =>
        bind.Flags.HasFlag(BindFlag.Mouse) || bind.Key.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase);

    private static bool IsCatchAll(Bind bind) =>
        bind.Key.Equals("catchall", StringComparison.Ordinal);

    private static bool IsSubmapDispatcher(Bind bind) =>
        bind.Dispatcher.Equals("submap", StringComparison.OrdinalIgnoreCase);

    // A bind that sends its key on runs on the press and again on the release, so the
    // window it sends to sees both edges.
    private static bool SendsBothEdges(Bind bind) =>
        !bind.Flags.HasFlag(BindFlag.LongPress)
        && (bind.Dispatcher.Equals("pass", StringComparison.OrdinalIgnoreCase)
            || bind.Dispatcher.Equals("sendshortcut", StringComparison.OrdinalIgnoreCase));

    private static bool DispatchesNow(Bind bind, bool releasing)
    {
        var release = bind.Flags.HasFlag(BindFlag.Release);
        var longPress = bind.Flags.HasFlag(BindFlag.LongPress);
        if (releasing)
            return release;

        return !release && !longPress;
    }

    private static bool WithinGesture(BindTable table, BindSession session, Bind bind)
    {
        var click = bind.Flags.HasFlag(BindFlag.Click);
        var drag = bind.Flags.HasFlag(BindFlag.Drag);
        if (!click && !drag)
            return true;

        var past = PastThreshold(table, session);
        if (click && past)
            return false;
        if (drag && !past)
            return false;

        return true;
    }

    private static bool PastThreshold(BindTable table, BindSession session)
    {
        var dx = (long)session.Pointer.X - session.Anchor.X;
        var dy = (long)session.Pointer.Y - session.Anchor.Y;
        var limit = (long)table.DragThreshold * table.DragThreshold;
        return (dx * dx) + (dy * dy) > limit;
    }

    private static bool Consumes(List<Bind> binds)
    {
        foreach (var bind in binds)
        {
            if (!bind.Flags.HasFlag(BindFlag.NonConsuming))
                return true;
        }

        return false;
    }

    private static void Catch(BindSession session, string key, bool mouse)
    {
        (mouse ? session.CaughtButtons : session.CaughtKeys).Add(key);
        foreach (var held in session.DownKeys)
        {
            if (ModifierKeys.ContainsKey(held))
                session.CaughtKeys.Add(held);
        }
    }

    private static void ReleaseModifier(BindSession session, string key)
    {
        if (!ModifierKeys.TryGetValue(key, out var modifier))
            return;

        foreach (var pair in ModifierKeys)
        {
            if (pair.Value == modifier && session.DownKeys.Contains(pair.Key))
                return;
        }

        session.Held &= ~modifier;
    }

    // A modifier keysym is matched against the mask without that key folded in,
    // unless the other side of the same modifier is already held.
    private static Modifier MaskFor(BindSession session, string key, bool mouse)
    {
        if (mouse || !ModifierKeys.TryGetValue(key, out var modifier))
            return session.Held;

        foreach (var pair in ModifierKeys)
        {
            if (pair.Value != modifier || pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (session.DownKeys.Contains(pair.Key))
                return session.Held;
        }

        return session.Held & ~modifier;
    }

    private static bool KeyMatches(Bind bind, string key, int scanCode)
    {
        if (!bind.Flags.HasFlag(BindFlag.MultiKey))
            return KeyEquals(bind.Key, key, scanCode);

        foreach (var part in bind.Key.Split('&'))
        {
            if (KeyEquals(part.Trim(), key, scanCode))
                return true;
        }

        return false;
    }

    private static bool KeyEquals(string bound, string key, int scanCode)
    {
        if (bound.Length == 0)
            return false;
        if (bound.Equals(key, StringComparison.OrdinalIgnoreCase))
            return true;

        return scanCode > 0 && bound.Equals($"code:{scanCode}", StringComparison.OrdinalIgnoreCase);
    }

    private static BindDecision CancelLongPress(BindSession session, BindDecision decision)
    {
        session.LongPressKey = null;
        return decision with { DisarmLongPress = true };
    }

    private static BindDecision Passed() => new(false, null);

    private static BindDecision Caught() => new(true, null);

    private static BindDecision Ran(List<Bind> binds, bool consumed, BindTrigger? trigger)
    {
        var count = 0;
        foreach (var bind in binds)
        {
            if (bind.Dispatcher.Length > 0)
                count++;
        }

        if (count == 0)
            return consumed ? Caught() : Passed();

        var dispatches = new BindDispatch[count];
        var index = 0;
        foreach (var bind in binds)
        {
            if (bind.Dispatcher.Length == 0)
                continue;

            dispatches[index++] = new BindDispatch(bind.Dispatcher, bind.Arguments) { SubmapReset = bind.SubmapReset };
        }

        return new BindDecision(consumed, dispatches[0].Dispatcher, dispatches[0].Arguments)
        {
            Dispatches = dispatches,
            Trigger = trigger,
        };
    }
}
