namespace Winland.Domain;

/// <summary>Matches a key or pointer button against a bind table. Takes no lock.</summary>
public static partial class BindMatcher
{
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

        // The lock and level keys, while they are held, as Winland reads the depressed modifiers.
        ["Caps_Lock"] = Modifier.Caps,
        ["Num_Lock"] = Modifier.Mod2,
        ["ISO_Level3_Shift"] = Modifier.Mod5,
    };

    public static BindDecision Decide(BindTable table, BindSession session, Input input)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);
        session.EventDevice = null;
        session.DeviceMatchMask = null;
        return input switch
        {
            KeyPressed pressed => Shadow(table, session, OnPress(table, session, pressed.Key, pressed.ScanCode, mouse: false, pressed.Device), pressed.Key),
            KeyReleased released => Shadow(table, session, OnRelease(table, session, released.Key, released.ScanCode, mouse: false, released.Device), excluded: null),
            PointerButtonPressed button => Shadow(table, session, OnButton(table, session, button.Button, button.Position, up: false), button.Button),
            PointerButtonReleased button => Shadow(table, session, OnButton(table, session, button.Button, button.Position, up: true), excluded: null),
            PointerMoved moved => OnMoved(session, moved.Position),
            WheelTurned wheel => OnWheel(table, session, wheel),
            LongPressElapsed => OnLongPress(table, session),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };
    }

    /// <summary>Drops held keys whose release the session missed, such as one on the lock screen.</summary>
    public static void Forget(BindSession session, Func<string, bool> stillDown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stillDown);
        Drop(session.DownKeys, session.CaughtKeys, session.PassedKeys, stillDown);
        Drop(session.DownButtons, session.CaughtButtons, session.PassedButtons, stillDown);
        session.MaskedKeys.IntersectWith(session.DownKeys);
        foreach (var key in session.PressedUnder.Keys.ToArray())
        {
            if (!session.DownKeys.Contains(key) && !session.DownButtons.Contains(key))
            {
                session.PressedUnder.Remove(key);
                session.ScanCodes.Remove(key);
                session.Devices.Remove(key);
            }
        }

        foreach (var (key, _) in session.Order.ToArray())
        {
            if (!session.DownKeys.Contains(key))
                session.Lift(key);
        }

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
            ? OnRelease(table, session, button, scanCode: 0, mouse: true, device: null)
            : OnPress(table, session, button, scanCode: 0, mouse: true, device: null);
    }

    private static BindDecision OnMoved(BindSession session, Point position)
    {
        session.Pointer = position;
        return Passed();
    }

    private static BindDecision OnPress(BindTable table, BindSession session, string key, int scanCode, bool mouse, string? device)
    {
        if (key.Length == 0)
            return Passed();

        var down = mouse ? session.DownButtons : session.DownKeys;
        var repeat = !down.Add(key);
        if (!mouse && ModifierKeys.TryGetValue(key, out var modifier))
            session.Held |= modifier;

        ApplyDevice(session, key, device, remember: !repeat && !mouse);
        if (repeat)
            return OnRepeat(table, session, key, scanCode, mouse);

        session.Remember(key, mouse);
        session.PressedUnder[key] = MaskFor(session, key, mouse);
        session.ScanCodes[key] = scanCode;
        var immediate = new List<Bind>();
        var deferred = new List<Bind>();
        var longPress = new List<Bind>();
        var heldBack = 0;
        var claims = false;
        List<Bind> found;
        if (mouse || ModifierKeys.ContainsKey(key))
        {
            found = Collect(table, session, key, scanCode, mouse, releasing: false, session.Submap);
        }
        else
        {
            session.Order.Add((key, scanCode));
            var chords = MatchChords(table, session, key, scanCode);
            found = chords.Immediate;
            heldBack = chords.HeldBack;
            claims = chords.Claims;
        }

        foreach (var bind in found)
        {
            if (bind.Flags.HasFlag(BindFlag.LongPress) && !bind.Flags.HasFlag(BindFlag.Release))
                longPress.Add(bind);
            else if (bind.Flags.HasFlag(BindFlag.Release) && !SendsBothEdges(bind))
                deferred.Add(bind);
            else
                immediate.Add(bind);
        }

        var passed = mouse ? session.PassedButtons : session.PassedKeys;
        if (immediate.Count == 0 && deferred.Count == 0 && longPress.Count == 0 && heldBack == 0 && !claims)
        {
            passed.Add(key);
            return CancelLongPress(session, Passed());
        }

        // A long press has not fired yet, so it does not consume the chord on the way down. A bind held back for a
        // longer chord, and a chord this key started, do. An auto-consuming bind does not, until it is invoked: the
        // Winland claims a partial match only when it is not auto-consuming, and a deferred press always claims.
        var now = Classify(immediate);
        var later = Classify(deferred);
        var hard = now.Hard || later.Hard || heldBack > 0 || claims;
        var consumed = hard || now.Conditional;
        List<string>? release = null;
        var trigger = new BindTrigger(key, scanCode, Pressed: true);
        if (consumed)
        {
            if (!hard && CanFail(immediate))
                release = [];
            Catch(session, key, mouse, release);
        }
        else
        {
            passed.Add(key);
        }

        if (longPress.Count == 0)
        {
            session.LongPressKey = null;
            return WithFailure(MaskHeld(session, Ran(immediate, consumed, trigger)) with { DisarmLongPress = true }, release);
        }

        session.LongPressKey = key;
        session.LongPressCode = scanCode;
        session.LongPressMouse = mouse;
        return WithFailure(MaskHeld(session, Ran(immediate, consumed, trigger)) with
        {
            ArmLongPressMs = table.LongPressDelay,
            DisarmLongPress = true,
        }, release);
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

        var claim = Classify(repeating);
        var hard = claim.Hard || caught.Contains(key);
        var consumed = hard || claim.Conditional;
        List<string>? release = null;
        if (consumed && !hard && CanFail(repeating))
            release = [];
        if (consumed)
            Catch(session, key, mouse, release);

        // A key already caught stays swallowed. A non-consuming repeat does not catch a key on its own.
        var trigger = new BindTrigger(key, scanCode, Pressed: true);
        return WithFailure(MaskHeld(session, Ran(repeating, consumed, trigger)), release);
    }

    private static BindDecision OnRelease(BindTable table, BindSession session, string key, int scanCode, bool mouse, string? device)
    {
        var down = mouse ? session.DownButtons : session.DownKeys;
        if (key.Length == 0 || !down.Remove(key))
            return Passed();

        ApplyDevice(session, key, device, remember: false);
        if (!mouse)
            ReleaseModifier(session, key);

        var caughtSet = mouse ? session.CaughtButtons : session.CaughtKeys;
        var caught = caughtSet.Remove(key);
        var passed = (mouse ? session.PassedButtons : session.PassedKeys).Remove(key);
        var masked = !mouse && session.MaskedKeys.Remove(key);

        var (submap, anchor) = session.Forget(key, mouse);

        // A bind a longer chord held back runs when its key is released, before the release binds.
        var fire = new List<Bind>();
        if (!mouse && session.HeldBack.TryGetValue(key, out var heldBack))
            fire.AddRange(heldBack);

        fire.AddRange(Collect(table, session, key, scanCode, mouse, releasing: true, submap, anchor));
        session.PressedUnder.Remove(key);
        session.ScanCodes.Remove(key);
        session.Devices.Remove(key);
        if (!mouse)
            session.Lift(key);

        session.LongPressKey = null;

        // The OS saw this press, so it must see the release, or it keeps the key down. An auto-consuming release bind
        // therefore cannot swallow the release: its press was not claimed, so Windows already has the press.
        var claim = Classify(fire);
        var hard = claim.Hard || caught;
        var consumed = fire.Count > 0 ? hard || claim.Conditional : caught;
        List<string>? release = null;
        if (fire.Count > 0 && consumed && !hard && CanFail(fire))
            release = [];
        var trigger = new BindTrigger(key, scanCode, Pressed: false);
        var decision = fire.Count > 0 ? Ran(fire, consumed, trigger) : caught ? Caught() : Passed();
        decision = WithFailure(decision, release);
        if (passed)
            decision = decision with { Consumed = false, PassesOnFailure = false, ReleaseOnFailure = [] };

        // A release bind on a Super or Alt the OS saw, such as "bindr = SUPER, Super_L", sends a mask
        // before the release passes, so the release does not also open Start or a menu.
        if (passed && fire.Count > 0 && !masked && ModifierKeys.TryGetValue(key, out var modifier) && modifier is Modifier.Super or Modifier.Alt)
            decision = decision with { Mask = true };

        return decision with { DisarmLongPress = true };
    }

    // As Winland does after a caught press, which skips the key just pressed, and after every release: a
    // release bind on one key that a held key fully matches is shadowed, so it does not run when that key is released.
    // With "bindr = ALT, Alt_L" and "bind = ALT, P", Alt+P does not also run the release bind.
    private static BindDecision Shadow(BindTable table, BindSession session, BindDecision decision, string? excluded)
    {
        if (excluded is not null && !decision.Consumed)
            return decision;

        session.Shadowed.Clear();
        foreach (var bind in table.Active(session.Submap))
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) || bind.Flags.HasFlag(BindFlag.Transparent) || Parts(bind).Length > 1 || IsMouseBind(bind) || IsCatchAll(bind))
                continue;

            foreach (var held in session.DownKeys)
            {
                if (held.Equals(excluded, StringComparison.OrdinalIgnoreCase))
                    continue;

                // A device bind matches the keyboard that held key went down on, not the key being shadowed now.
                var savedDevice = session.EventDevice;
                var named = bind.Devices is not null;
                if (named)
                    session.EventDevice = session.Devices.TryGetValue(held, out var remembered) ? remembered : null;

                var matched = Eligible(bind, session, held, session.ScanCodeOf(held), mouse: false, releasing: true, anyKey: false);
                if (named)
                    session.EventDevice = savedDevice;

                if (matched)
                {
                    session.Shadowed.Add(bind);
                    break;
                }
            }
        }

        return decision;
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

        var claim = Classify(pressing);
        List<string>? release = null;
        if (!claim.Hard && claim.Conditional && CanFail(pressing))
            release = [];
        return WithFailure(MaskHeld(session, Ran(pressing, claim.Hard || claim.Conditional, trigger: null)) with { DisarmLongPress = true }, release);
    }

    private static BindDecision OnLongPress(BindTable table, BindSession session)
    {
        if (session.LongPressKey is not string key)
            return Passed();

        ApplyDevice(session, key, device: null, remember: false);
        var mouse = session.LongPressMouse;
        session.LongPressKey = null;
        var down = mouse ? session.DownButtons : session.DownKeys;
        if (!down.Contains(key))
            return new BindDecision(false, null) { DisarmLongPress = true };

        var longPress = new List<Bind>();
        foreach (var bind in Collect(table, session, key, session.LongPressCode, mouse, releasing: false, session.Remembered(key, mouse)))
        {
            if (bind.Flags.HasFlag(BindFlag.LongPress) && !bind.Flags.HasFlag(BindFlag.Release))
                longPress.Add(bind);
        }

        if (longPress.Count == 0)
            return new BindDecision(false, null) { DisarmLongPress = true };

        var claim = Classify(longPress);
        List<string>? release = null;
        if (!claim.Hard && claim.Conditional && CanFail(longPress))
            release = [];
        var trigger = new BindTrigger(key, session.LongPressCode, Pressed: true);
        return WithFailure(MaskHeld(session, Ran(longPress, claim.Hard || claim.Conditional, trigger)) with { DisarmLongPress = true }, release);
    }

    private static BindDecision WithFailure(BindDecision decision, List<string>? release) =>
        release is null ? decision : decision with { PassesOnFailure = true, ReleaseOnFailure = release };

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

            dispatches[index++] = new BindDispatch(bind.Dispatcher, bind.Arguments)
            {
                SubmapReset = bind.SubmapReset,
                Drag = bind.Flags.HasFlag(BindFlag.Mouse),
            };
        }

        return new BindDecision(consumed, dispatches[0].Dispatcher, dispatches[0].Arguments)
        {
            Dispatches = dispatches,
            Trigger = trigger,
        };
    }
}
