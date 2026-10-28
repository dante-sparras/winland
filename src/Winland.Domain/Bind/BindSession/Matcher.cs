namespace Winland.Domain;

/// <summary>Matches a key or pointer button against a bind table, under the session's lock.</summary>
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
        lock (_gate)
            return Match(table, input);
    }

    private BindDecision Match(BindTable table, Input input) =>
        input switch
        {
            KeyPressed pressed => Shadow(table, OnPress(table, pressed.Key, pressed.ScanCode, mouse: false), pressed.Key),
            KeyReleased released => Shadow(table, OnRelease(table, released.Key, released.ScanCode, mouse: false), excluded: null),
            PointerButtonPressed button => Shadow(table, OnButton(table, button.Button, button.Position, up: false), button.Button),
            PointerButtonReleased button => Shadow(table, OnButton(table, button.Button, button.Position, up: true), excluded: null),
            PointerMoved moved => OnMoved(moved.Position),
            WheelTurned wheel => OnWheel(table, wheel),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };

    /// <summary>Drops held keys whose release the session missed, such as one on the lock screen.</summary>
    public void Forget(Func<string, bool> stillDown)
    {
        ArgumentNullException.ThrowIfNull(stillDown);
        lock (_gate)
            ForgetMissed(stillDown);
    }

    private void ForgetMissed(Func<string, bool> stillDown)
    {
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
            }
        }

        Held = 0;
        foreach (var key in _downKeys)
        {
            if (ModifierKeys.TryGetValue(key, out var modifier))
                Held |= modifier;
        }
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
            ? OnRelease(table, button, scanCode: 0, mouse: true)
            : OnPress(table, button, scanCode: 0, mouse: true);
    }

    private BindDecision OnMoved(Point position)
    {
        Pointer = position;
        return Passed();
    }

    private BindDecision OnPress(BindTable table, string key, int scanCode, bool mouse)
    {
        if (key.Length == 0)
            return Passed();

        var down = mouse ? _downButtons : _downKeys;
        var repeat = !down.Add(key);
        if (mouse)
            _buttonsHeld = _downButtons.Count;

        if (!mouse && ModifierKeys.TryGetValue(key, out var modifier))
            Held |= modifier;

        if (repeat)
            return OnRepeat(table, key, scanCode, mouse);

        _pressedUnder[key] = MaskFor(key, mouse);
        _scanCodes[key] = scanCode;
        var immediate = new List<Bind>();
        var deferred = new List<Bind>();
        foreach (var bind in Collect(table, key, scanCode, mouse, releasing: false))
        {
            if (bind.Flags.HasFlag(BindFlag.Release) && !SendsBothEdges(bind))
                deferred.Add(bind);
            else
                immediate.Add(bind);
        }

        var passed = mouse ? _passedButtons : _passedKeys;
        if (immediate.Count == 0 && deferred.Count == 0)
        {
            passed.Add(key);
            return Passed();
        }

        // A release bind consumes its press too, so the window does not see half of the key.
        var consumed = Consumes(immediate) || Consumes(deferred);
        if (consumed)
            Catch(key, mouse);
        else
            passed.Add(key);

        return MaskHeld(Ran(immediate, consumed, new BindTrigger(key, scanCode, Pressed: true)));
    }

    private BindDecision OnRepeat(BindTable table, string key, int scanCode, bool mouse)
    {
        var caught = mouse ? _caughtButtons : _caughtKeys;
        var repeating = new List<Bind>();
        foreach (var bind in Collect(table, key, scanCode, mouse, releasing: false))
        {
            if (bind.Flags.HasFlag(BindFlag.Repeat) && !bind.Flags.HasFlag(BindFlag.Release))
                repeating.Add(bind);
        }

        if (repeating.Count == 0)
            return caught.Contains(key) ? Caught() : Passed();

        // A key already caught stays swallowed. A non-consuming repeat does not catch a key on its own.
        var consumed = Consumes(repeating) || caught.Contains(key);
        if (consumed)
            Catch(key, mouse);

        return MaskHeld(Ran(repeating, consumed, new BindTrigger(key, scanCode, Pressed: true)));
    }

    private BindDecision OnRelease(BindTable table, string key, int scanCode, bool mouse)
    {
        var down = mouse ? _downButtons : _downKeys;
        if (key.Length == 0 || !down.Remove(key))
            return Passed();

        if (mouse)
            _buttonsHeld = _downButtons.Count;

        if (!mouse)
            ReleaseModifier(key);

        var caught = (mouse ? _caughtButtons : _caughtKeys).Remove(key);
        var passed = (mouse ? _passedButtons : _passedKeys).Remove(key);
        var masked = !mouse && _maskedKeys.Remove(key);

        var fire = Collect(table, key, scanCode, mouse, releasing: true);
        _pressedUnder.Remove(key);
        _scanCodes.Remove(key);

        // The OS saw this press, so it must see the release, or it keeps the key down.
        var consumed = Consumes(fire) || caught;
        var trigger = new BindTrigger(key, scanCode, Pressed: false);
        var decision = fire.Count > 0 ? Ran(fire, consumed, trigger) : caught ? Caught() : Passed();
        if (passed)
            decision = decision with { Consumed = false };

        // A release bind on a Super or Alt the OS saw, such as "bindr = SUPER, Super_L", sends a mask
        // before the release passes, so the release does not also open Start or a menu.
        if (passed && fire.Count > 0 && !masked && ModifierKeys.TryGetValue(key, out var modifier) && modifier is Modifier.Super or Modifier.Alt)
            decision = decision with { Mask = true };

        return decision;
    }

    // As Winland does after a caught press, which skips the key just pressed, and after every release: a
    // release bind on one key that a held key fully matches is shadowed, so it does not run when that key is released.
    // With "bindr = ALT, Alt_L" and "bind = ALT, P", Alt+P does not also run the release bind.
    private BindDecision Shadow(BindTable table, BindDecision decision, string? excluded)
    {
        if (excluded is not null && !decision.Consumed)
            return decision;

        _shadowed.Clear();
        foreach (var bind in table.Binds)
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) || IsMouseBind(bind))
                continue;

            foreach (var held in _downKeys)
            {
                if (held.Equals(excluded, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Eligible(bind, held, ScanCodeOf(held), mouse: false, releasing: true))
                {
                    _shadowed.Add(bind);
                    break;
                }
            }
        }

        return decision;
    }

    // A swallowed chord under a Super or Alt the OS saw gets one mask per hold.
    private BindDecision MaskHeld(BindDecision decision)
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

        var pressing = new List<Bind>();
        foreach (var bind in Collect(table, wheel.Key, scanCode: 0, mouse: false, releasing: false))
        {
            if (!bind.Flags.HasFlag(BindFlag.Release))
                pressing.Add(bind);
        }

        if (pressing.Count == 0)
            return Passed();

        // Binds wait a moment between notches, so one spin does not run them many times. A notch in that wait is
        // still the bind's, and does not scroll the window under the pointer. Plain scrolling starts no wait.
        if (_scrollSeen && wheel.TimeMs - _scrollAtMs < BindTable.ScrollDelay)
            return Consumes(pressing) ? Caught() : Passed();

        _scrollSeen = true;
        _scrollAtMs = wheel.TimeMs;
        return MaskHeld(Ran(pressing, Consumes(pressing), trigger: null));
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

            dispatches[index++] = new BindDispatch(bind.Dispatcher, bind.Arguments) { Drag = bind.Flags.HasFlag(BindFlag.Mouse) };
        }

        return new BindDecision(consumed, dispatches[0].Dispatcher, dispatches[0].Arguments)
        {
            Dispatches = dispatches,
            Trigger = trigger,
        };
    }
}
