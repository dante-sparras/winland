namespace Winland.Domain;

/// <summary>Matches a key or pointer button against a bind table. Takes no lock.</summary>
public static class BindMatcher
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

    // The anchor is where the released key went down. Only a release reads it.
    private static List<Bind> Collect(BindTable table, BindSession session, string key, int scanCode, bool mouse, bool releasing, string submap, Point anchor = default)
    {
        var matches = new List<Bind>();
        foreach (var bind in table.Active(submap))
        {
            if (IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse, releasing, anchor, catchAll: false))
                continue;

            matches.Add(bind);
            if (DispatchesNow(bind, releasing) && IsSubmapDispatcher(bind))
                break;
        }

        if (matches.Count == 0 && submap == session.Submap)
        {
            foreach (var bind in table.Active(submap))
            {
                if (!IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse, releasing, anchor, catchAll: true))
                    continue;

                matches.Add(bind);
                if (DispatchesNow(bind, releasing) && IsSubmapDispatcher(bind))
                    break;
            }
        }

        return matches;
    }

    private static bool Include(Bind bind, BindTable table, BindSession session, string key, int scanCode, bool mouse, bool releasing, Point anchor, bool catchAll)
    {
        if (!Eligible(bind, session, key, scanCode, mouse, releasing, anyKey: catchAll))
            return false;

        if (releasing)
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) && !SendsBothEdges(bind))
                return false;
            if (!mouse && Listed(session.Suppressed, key, bind))
                return false;
            if (session.Shadowed.Contains(bind))
                return false;
            if (!mouse && bind.Flags.HasFlag(BindFlag.Release) && Parts(bind).Length > 1 && !Listed(session.Armed, key, bind))
                return false;
            if (!WithinGesture(table, session, bind, anchor))
                return false;
        }

        return true;
    }

    // Mouse, modifiers, the key unless any key will do, the locked flag, and the gesture flags.
    private static bool Eligible(Bind bind, BindSession session, string key, int scanCode, bool mouse, bool releasing, bool anyKey)
    {
        if (IsMouseBind(bind) != mouse)
            return false;
        // A release chord armed on this key matched the modifiers held when the key went down.
        var armed = releasing && !mouse && Listed(session.Armed, key, bind);
        if (!bind.Flags.HasFlag(BindFlag.IgnoreModifiers) && !armed && !ModifiersMatch(bind, session, key, mouse, releasing))
            return false;
        if (!anyKey && !KeyMatches(bind, key, scanCode))
            return false;
        if (!MatchesDevice(bind, session))
            return false;
        if (session.Inhibited && !bind.Flags.HasFlag(BindFlag.Locked))
            return false;
        if (!releasing && bind.Flags.HasFlag(BindFlag.Release) && bind.Flags.HasFlag(BindFlag.LongPress))
            return false;

        return true;
    }

    // A mouse button's bind. A drag bound to a key, through the mouse dispatcher, is the key's.
    private static bool IsMouseBind(Bind bind) =>
        bind.Key.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase);

    private static bool IsCatchAll(Bind bind) =>
        bind.Key.Equals("catchall", StringComparison.Ordinal);

    private static bool IsSubmapDispatcher(Bind bind) =>
        bind.Dispatcher.Equals("submap", StringComparison.OrdinalIgnoreCase);

    // A sendshortcut bind runs on the press and again on the release, so the window it
    // sends to sees both edges.
    private static bool SendsBothEdges(Bind bind) =>
        !bind.Flags.HasFlag(BindFlag.LongPress)
        && bind.Dispatcher.Equals("sendshortcut", StringComparison.OrdinalIgnoreCase);

    private static bool DispatchesNow(Bind bind, bool releasing)
    {
        var release = bind.Flags.HasFlag(BindFlag.Release);
        var longPress = bind.Flags.HasFlag(BindFlag.LongPress);
        if (releasing)
            return release;

        return !release && !longPress;
    }

    private static bool WithinGesture(BindTable table, BindSession session, Bind bind, Point anchor)
    {
        var click = bind.Flags.HasFlag(BindFlag.Click);
        var drag = bind.Flags.HasFlag(BindFlag.Drag);
        if (!click && !drag)
            return true;

        var past = PastThreshold(table, session, anchor);
        if (click && past)
            return false;
        if (drag && !past)
            return false;

        return true;
    }

    private static bool PastThreshold(BindTable table, BindSession session, Point anchor)
    {
        var dx = (long)session.Pointer.X - anchor.X;
        var dy = (long)session.Pointer.Y - anchor.Y;
        var limit = (long)table.DragThreshold * table.DragThreshold;
        return (dx * dx) + (dy * dy) > limit;
    }

    private readonly record struct Claim(bool Hard, bool Conditional);

    // A non-consuming bind never consumes. An auto-consuming bind consumes only when its dispatcher succeeds, so the
    // press is swallowed now and given back on failure. Any other bind consumes outright.
    private static Claim Classify(List<Bind> binds)
    {
        var hard = false;
        var conditional = false;
        foreach (var bind in binds)
        {
            if (bind.Flags.HasFlag(BindFlag.NonConsuming))
                continue;
            if (bind.Flags.HasFlag(BindFlag.AutoConsuming))
                conditional = true;
            else
                hard = true;
        }

        return new Claim(hard, conditional);
    }

    private static bool CanFail(List<Bind> binds)
    {
        foreach (var bind in binds)
        {
            if (bind.Dispatcher.Length > 0 && bind.Flags.HasFlag(BindFlag.AutoConsuming) && !bind.Flags.HasFlag(BindFlag.NonConsuming))
                return true;
        }

        return false;
    }

    private static void Catch(BindSession session, string key, bool mouse, List<string>? added)
    {
        NoteCaught(mouse ? session.CaughtButtons : session.CaughtKeys, key, added);
        foreach (var held in session.DownKeys)
        {
            if (ModifierKeys.ContainsKey(held))
                NoteCaught(session.CaughtKeys, held, added);
        }
    }

    private static void NoteCaught(HashSet<string> caught, string key, List<string>? added)
    {
        if (caught.Add(key) && added is not null)
            added.Add(key);
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

    // On release Winland compares the modifiers the key went down under, with the released key's own
    // modifier added, so "bindr = SUPER, Super_L" runs when Super is let go and "bindr = , Super_L" does not, and
    // "bindr = SUPER, K" runs when Super is let go before K.
    private static bool ModifiersMatch(Bind bind, BindSession session, string key, bool mouse, bool releasing)
    {
        if (!releasing && session.DeviceMatchMask is Modifier forced)
            return bind.Modifiers == forced;

        var mask = releasing && session.PressedUnder.TryGetValue(key, out var under) ? under : MaskFor(session, key, mouse);
        if (releasing && !mouse && ModifierKeys.TryGetValue(key, out var own))
            mask |= own;

        return bind.Modifiers == mask;
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

    // A bind on several keys is triggered by its last key.
    private static bool KeyMatches(Bind bind, string key, int scanCode)
    {
        var parts = Parts(bind);
        return KeyEquals(parts[^1], key, scanCode);
    }

    private static string[] Parts(Bind bind) =>
        bind.Flags.HasFlag(BindFlag.MultiKey) ? bind.Key.Split('&', StringSplitOptions.TrimEntries) : [bind.Key];

    private static bool Contains(Bind bind, string key, int scanCode)
    {
        foreach (var part in Parts(bind))
        {
            if (KeyEquals(part, key, scanCode))
                return true;
        }

        return false;
    }

    private static bool Listed(Dictionary<string, List<Bind>> perKey, string key, Bind bind) =>
        perKey.TryGetValue(key, out var binds) && binds.Exists(listed => ReferenceEquals(listed, bind));

    private enum KeyMatch
    {
        None,
        Partial,
        Full,
    }

    private readonly record struct ChordMatches(List<Bind> Immediate, int HeldBack, bool Claims);

    // A key press as Winland resolves it. A bind on several keys matches the held keys in the order they went
    // down: full once its last key completes them, partial while the press extends a prefix of it. A single key is full
    // when pressed and partial while it is held under another key. A full bind that a longer partial chord could still
    // extend waits for its key's release, and one a longer full chord covers does not run. A partial chord consumes the
    // press unless it is non-consuming or a release bind. A release chord completed in order runs on its last key's release.
    private static ChordMatches MatchChords(BindTable table, BindSession session, string key, int scanCode)
    {
        var full = new List<Bind>();
        var partial = new List<Bind>();
        var completed = new List<Bind>();
        foreach (var bind in table.Active(session.Submap))
        {
            if (IsCatchAll(bind) || !Eligible(bind, session, key, scanCode, mouse: false, releasing: false, anyKey: true))
                continue;

            var match = MatchKeys(bind, session, key, scanCode);
            if (match == KeyMatch.None)
                continue;

            if (bind.Flags.HasFlag(BindFlag.Release) && Parts(bind).Length > 1)
            {
                if (match == KeyMatch.Full)
                {
                    Add(BindSession.Of(session.Armed, key), bind);
                    completed.Add(bind);
                }

                partial.Add(bind);
                continue;
            }

            if (match == KeyMatch.Partial)
            {
                partial.Add(bind);
                continue;
            }

            full.Add(bind);
            if (DispatchesNow(bind, releasing: false) && IsSubmapDispatcher(bind))
                break;
        }

        var immediate = new List<Bind>();
        var heldBack = 0;
        foreach (var bind in full)
        {
            // A release or long-press bind on one key keeps its own timing.
            if (bind.Flags.HasFlag(BindFlag.Release) || bind.Flags.HasFlag(BindFlag.LongPress))
            {
                immediate.Add(bind);
                continue;
            }

            if (full.Exists(other => !ReferenceEquals(other, bind) && SubChord(bind, other, session)))
                continue;

            if (partial.Exists(other => SubChord(bind, other, session)))
            {
                Add(BindSession.Of(session.HeldBack, key), bind);
                completed.Add(bind);
                heldBack++;
                continue;
            }

            immediate.Add(bind);
            completed.Add(bind);
        }

        // A chord that ran, waits for its key's release, or is armed for it holds off the shorter binds inside it.
        foreach (var bind in completed)
        {
            if (Parts(bind).Length > 1)
                Suppress(table, session, bind);
        }

        var claims = partial.Exists(bind => !bind.Flags.HasFlag(BindFlag.NonConsuming) && !bind.Flags.HasFlag(BindFlag.Release) && !bind.Flags.HasFlag(BindFlag.AutoConsuming));
        if (full.Count == 0 && partial.Count == 0)
        {
            foreach (var bind in table.Active(session.Submap))
            {
                if (!IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse: false, releasing: false, anchor: default, catchAll: true))
                    continue;

                immediate.Add(bind);
                if (DispatchesNow(bind, releasing: false) && IsSubmapDispatcher(bind))
                    break;
            }
        }

        return new ChordMatches(immediate, heldBack, claims);
    }

    private static KeyMatch MatchKeys(Bind bind, BindSession session, string key, int scanCode)
    {
        var parts = Parts(bind);
        if (parts.Length == 1)
        {
            if (KeyEquals(parts[0], key, scanCode))
                return KeyMatch.Full;

            foreach (var held in session.Order)
            {
                if (KeyEquals(parts[0], held.Key, held.ScanCode))
                    return KeyMatch.Partial;
            }

            return KeyMatch.None;
        }

        // Only the press that advances the prefix matches.
        var matched = 0;
        var triggered = false;
        foreach (var held in session.Order)
        {
            if (matched == parts.Length)
                break;
            if (!KeyEquals(parts[matched], held.Key, held.ScanCode))
                continue;

            matched++;
            triggered = held.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        if (!triggered)
            return KeyMatch.None;

        return matched == parts.Length && matched == session.Order.Count ? KeyMatch.Full : KeyMatch.Partial;
    }

    // True when every key of the shorter bind is held and is also a key of the longer one.
    private static bool SubChord(Bind shorter, Bind longer, BindSession session)
    {
        var mine = Parts(shorter);
        var theirs = Parts(longer);
        if (mine.Length >= theirs.Length)
            return false;

        var usedHeld = new bool[session.Order.Count];
        var usedTheirs = new bool[theirs.Length];
        foreach (var part in mine)
        {
            var matched = false;
            for (var h = 0; h < session.Order.Count && !matched; h++)
            {
                var (held, code) = session.Order[h];
                if (usedHeld[h] || !KeyEquals(part, held, code))
                    continue;

                for (var o = 0; o < theirs.Length; o++)
                {
                    if (usedTheirs[o] || !KeyEquals(theirs[o], held, code))
                        continue;

                    usedHeld[h] = true;
                    usedTheirs[o] = true;
                    matched = true;
                    break;
                }
            }

            if (!matched)
                return false;
        }

        return true;
    }

    // A completed chord stops the shorter binds inside it from running on the release of its keys.
    private static void Suppress(BindTable table, BindSession session, Bind completed)
    {
        var shorter = new List<Bind>();
        foreach (var bind in table.Active(session.Submap))
        {
            if (!ReferenceEquals(bind, completed) && SubChord(bind, completed, session))
                shorter.Add(bind);
        }

        foreach (var (held, code) in session.Order)
        {
            if (!Contains(completed, held, code))
                continue;

            foreach (var bind in shorter)
            {
                if (!Contains(bind, held, code))
                    continue;

                if (session.HeldBack.TryGetValue(held, out var back))
                    back.RemoveAll(listed => ReferenceEquals(listed, bind));
                Add(BindSession.Of(session.Suppressed, held), bind);
            }
        }
    }

    private static void Add(List<Bind> binds, Bind bind)
    {
        if (!binds.Exists(listed => ReferenceEquals(listed, bind)))
            binds.Add(bind);
    }

    private static bool KeyEquals(string bound, string key, int scanCode)
    {
        if (bound.Length == 0)
            return false;
        if (bound.Equals(key, StringComparison.OrdinalIgnoreCase))
            return true;

        return scanCode > 0 && bound.Equals($"code:{scanCode}", StringComparison.OrdinalIgnoreCase);
    }

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
