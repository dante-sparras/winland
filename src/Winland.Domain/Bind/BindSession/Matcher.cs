namespace Winland.Domain;

/// <summary>Matches a key or pointer button against a bind table. Takes no lock.</summary>
public sealed partial class BindSession
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

    public BindDecision Decide(BindTable table, Input input)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(input);
        _eventDevice = null;
        _deviceMatchMask = null;
        return input switch
        {
            KeyPressed pressed => Shadow(table, OnPress(table, pressed.Key, pressed.ScanCode, mouse: false, pressed.Device), pressed.Key),
            KeyReleased released => Shadow(table, OnRelease(table, released.Key, released.ScanCode, mouse: false, released.Device), excluded: null),
            PointerButtonPressed button => Shadow(table, OnButton(table, button.Button, button.Position, up: false), button.Button),
            PointerButtonReleased button => Shadow(table, OnButton(table, button.Button, button.Position, up: true), excluded: null),
            PointerMoved moved => OnMoved(moved.Position),
            WheelTurned wheel => OnWheel(table, wheel),
            LongPressElapsed => OnLongPress(table),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };
    }

    /// <summary>Drops held keys whose release the session missed, such as one on the lock screen.</summary>
    public void Forget(Func<string, bool> stillDown)
    {
        ArgumentNullException.ThrowIfNull(stillDown);
        Drop(_downKeys, _caughtKeys, _passedKeys, stillDown);
        Drop(_downButtons, _caughtButtons, _passedButtons, stillDown);
        _buttonsHeld = _downButtons.Count;
        _maskedKeys.IntersectWith(_downKeys);
        foreach (var key in _pressedUnder.Keys.ToArray())
        {
            if (!_downKeys.Contains(key) && !_downButtons.Contains(key))
            {
                _pressedUnder.Remove(key);
                _scanCodes.Remove(key);
                _devices.Remove(key);
            }
        }

        foreach (var (key, _) in Order.ToArray())
        {
            if (!_downKeys.Contains(key))
                Lift(key);
        }

        Held = 0;
        foreach (var key in _downKeys)
        {
            if (ModifierKeys.TryGetValue(key, out var modifier))
                Held |= modifier;
        }

        if (LongPressKey is string pending && !(_longPressMouse ? _downButtons : _downKeys).Contains(pending))
            LongPressKey = null;
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

    private BindDecision OnButton(BindTable table, string button, Point position, bool up)
    {
        Pointer = position;
        return up
            ? OnRelease(table, button, scanCode: 0, mouse: true, device: null)
            : OnPress(table, button, scanCode: 0, mouse: true, device: null);
    }

    private BindDecision OnMoved( Point position)
    {
        Pointer = position;
        return Passed();
    }

    private BindDecision OnPress(BindTable table, string key, int scanCode, bool mouse, string? device)
    {
        if (key.Length == 0)
            return Passed();

        var down = mouse ? _downButtons : _downKeys;
        var repeat = !down.Add(key);
        if (mouse)
            _buttonsHeld = _downButtons.Count;

        if (!mouse && ModifierKeys.TryGetValue(key, out var modifier))
            Held |= modifier;

        ApplyDevice(key, device, remember: !repeat && !mouse);
        if (repeat)
            return OnRepeat(table, key, scanCode, mouse);

        Remember(key, mouse);
        _pressedUnder[key] = MaskFor(key, mouse);
        _scanCodes[key] = scanCode;
        var immediate = new List<Bind>();
        var deferred = new List<Bind>();
        var longPress = new List<Bind>();
        var heldBack = 0;
        var claims = false;
        List<Bind> found;
        if (mouse || ModifierKeys.ContainsKey(key))
        {
            found = Collect(table, key, scanCode, mouse, releasing: false, Submap);
        }
        else
        {
            Order.Add((key, scanCode));
            var chords = MatchChords(table, key, scanCode);
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

        var passed = mouse ? _passedButtons : _passedKeys;
        if (immediate.Count == 0 && deferred.Count == 0 && longPress.Count == 0 && heldBack == 0 && !claims)
        {
            passed.Add(key);
            return CancelLongPress(Passed());
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
            Catch(key, mouse, release);
        }
        else
        {
            passed.Add(key);
        }

        if (longPress.Count == 0)
        {
            LongPressKey = null;
            return WithFailure(MaskHeld(Ran(immediate, consumed, trigger)) with { DisarmLongPress = true }, release);
        }

        LongPressKey = key;
        _longPressCode = scanCode;
        _longPressMouse = mouse;
        return WithFailure(MaskHeld(Ran(immediate, consumed, trigger)) with
        {
            ArmLongPressMs = table.LongPressDelay,
            DisarmLongPress = true,
        }, release);
    }

    private BindDecision OnRepeat(BindTable table, string key, int scanCode, bool mouse)
    {
        var caught = mouse ? _caughtButtons : _caughtKeys;
        var repeating = new List<Bind>();
        foreach (var bind in Collect(table, key, scanCode, mouse, releasing: false, Remembered(key, mouse)))
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
            Catch(key, mouse, release);

        // A key already caught stays swallowed. A non-consuming repeat does not catch a key on its own.
        var trigger = new BindTrigger(key, scanCode, Pressed: true);
        return WithFailure(MaskHeld(Ran(repeating, consumed, trigger)), release);
    }

    private BindDecision OnRelease(BindTable table, string key, int scanCode, bool mouse, string? device)
    {
        var down = mouse ? _downButtons : _downKeys;
        if (key.Length == 0 || !down.Remove(key))
            return Passed();

        if (mouse)
            _buttonsHeld = _downButtons.Count;

        ApplyDevice(key, device, remember: false);
        if (!mouse)
            ReleaseModifier(key);

        var caughtSet = mouse ? _caughtButtons : _caughtKeys;
        var caught = caughtSet.Remove(key);
        var passed = (mouse ? _passedButtons : _passedKeys).Remove(key);
        var masked = !mouse && _maskedKeys.Remove(key);

        var (submap, anchor) = Forget(key, mouse);

        // A bind a longer chord held back runs when its key is released, before the release binds.
        var fire = new List<Bind>();
        if (!mouse && _heldBack.TryGetValue(key, out var heldBack))
            fire.AddRange(heldBack);

        fire.AddRange(Collect(table, key, scanCode, mouse, releasing: true, submap, anchor));
        _pressedUnder.Remove(key);
        _scanCodes.Remove(key);
        _devices.Remove(key);
        if (!mouse)
            Lift(key);

        LongPressKey = null;

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
    private BindDecision Shadow(BindTable table, BindDecision decision, string? excluded)
    {
        if (excluded is not null && !decision.Consumed)
            return decision;

        _shadowed.Clear();
        foreach (var bind in table.Active(Submap))
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) || bind.Flags.HasFlag(BindFlag.Transparent) || Parts(bind).Length > 1 || IsMouseBind(bind) || IsCatchAll(bind))
                continue;

            foreach (var held in _downKeys)
            {
                if (held.Equals(excluded, StringComparison.OrdinalIgnoreCase))
                    continue;

                // A device bind matches the keyboard that held key went down on, not the key being shadowed now.
                var savedDevice = _eventDevice;
                var named = bind.Devices is not null;
                if (named)
                    _eventDevice = _devices.TryGetValue(held, out var remembered) ? remembered : null;

                var matched = Eligible(bind, held, ScanCodeOf(held), mouse: false, releasing: true, anyKey: false);
                if (named)
                    _eventDevice = savedDevice;

                if (matched)
                {
                    _shadowed.Add(bind);
                    break;
                }
            }
        }

        return decision;
    }

    // A swallowed chord under a Super or Alt the OS saw gets one mask per hold.
    private BindDecision MaskHeld( BindDecision decision)
    {
        if (!decision.Consumed)
            return decision;

        var mask = false;
        foreach (var key in _passedKeys)
        {
            if (ModifierKeys.TryGetValue(key, out var modifier) && modifier is Modifier.Super or Modifier.Alt && _maskedKeys.Add(key))
                mask = true;
        }

        return mask ? decision with { Mask = true } : decision;
    }

    private BindDecision OnWheel(BindTable table, WheelTurned wheel)
    {
        if (wheel.Key.Length == 0)
            return Passed();

        if (_scrollSeen && wheel.TimeMs - _scrollAtMs < table.ScrollDelay)
            return CancelLongPress(Passed());

        _scrollSeen = true;
        _scrollAtMs = wheel.TimeMs;
        var pressing = new List<Bind>();
        foreach (var bind in Collect(table, wheel.Key, scanCode: 0, mouse: false, releasing: false, Submap))
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) && !bind.Flags.HasFlag(BindFlag.LongPress))
                pressing.Add(bind);
        }

        if (pressing.Count == 0)
            return CancelLongPress(Passed());

        var claim = Classify(pressing);
        List<string>? release = null;
        if (!claim.Hard && claim.Conditional && CanFail(pressing))
            release = [];
        return WithFailure(MaskHeld(Ran(pressing, claim.Hard || claim.Conditional, trigger: null)) with { DisarmLongPress = true }, release);
    }

    private BindDecision OnLongPress(BindTable table)
    {
        if (LongPressKey is not string key)
            return Passed();

        ApplyDevice(key, device: null, remember: false);
        var mouse = _longPressMouse;
        LongPressKey = null;
        var down = mouse ? _downButtons : _downKeys;
        if (!down.Contains(key))
            return new BindDecision(false, null) { DisarmLongPress = true };

        var longPress = new List<Bind>();
        foreach (var bind in Collect(table, key, _longPressCode, mouse, releasing: false, Remembered(key, mouse)))
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
        var trigger = new BindTrigger(key, _longPressCode, Pressed: true);
        return WithFailure(MaskHeld(Ran(longPress, claim.Hard || claim.Conditional, trigger)) with { DisarmLongPress = true }, release);
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
