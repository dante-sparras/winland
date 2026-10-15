using System.Globalization;
using static Winland.OrdinalText;

namespace Winland;

public sealed partial class Core
{
    private BindTrigger? _trigger;

    // The key that ran the bind goes on to the window: its press on the press, its release on the release.
    private Outcome Pass(string arguments, bool allowsElevation)
    {
        const string name = "pass";
        if (Select(arguments, emptyMeansActive: false) is not WindowId target)
            return Rejected(name, "pass: window not found", allowsElevation);

        if (_trigger is not BindTrigger trigger || trigger.Key.Length == 0)
            return Rejected(name, "pass: no key to pass", allowsElevation);

        var relay = new KeyRelay(target, trigger.Key, trigger.Pressed ? KeyRelayPhase.Press : KeyRelayPhase.Release)
        {
            Code = trigger.Code,
        };
        return Relayed(name, relay, allowsElevation);
    }

    private Outcome SendShortcut(string arguments, bool allowsElevation)
    {
        const string name = "sendshortcut";
        if (!TryReadShortcut(name, arguments, parts: 3, out var shortcut, out var error))
            return Rejected(name, error, allowsElevation);

        var phase = _trigger switch
        {
            BindTrigger { Pressed: true } => KeyRelayPhase.Press,
            BindTrigger { Pressed: false } => KeyRelayPhase.Release,
            _ => KeyRelayPhase.Tap,
        };
        return Relayed(name, shortcut.Relay(phase), allowsElevation);
    }

    private Outcome SendKeyState(string arguments, bool allowsElevation)
    {
        const string name = "sendkeystate";
        var parts = arguments.Split(',', 4, StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            return Rejected(name, "sendkeystate: invalid args", allowsElevation);

        var state = parts[2];
        if (state is not ("down" or "repeat" or "up"))
            return Rejected(name, "sendkeystate: invalid state, must be 'down', 'repeat', or 'up'", allowsElevation);

        var rest = string.Join(',', parts[0], parts[1], parts[3]);
        if (!TryReadShortcut(name, rest, parts: 3, out var shortcut, out var error))
            return Rejected(name, error, allowsElevation);

        if (state == "up")
            return Relayed(name, shortcut.Relay(KeyRelayPhase.Release), allowsElevation);

        var press = shortcut.Relay(KeyRelayPhase.Press);
        IReadOnlyList<KeyRelay> relays = state == "repeat" ? [press, press] : [press];
        return View([], dispatcher: name, allowsElevation: allowsElevation) with { Relays = relays };
    }

    private Outcome Relayed(string name, KeyRelay relay, bool allowsElevation) =>
        View([], dispatcher: name, allowsElevation: allowsElevation) with { Relays = [relay] };

    private bool TryReadShortcut(string name, string arguments, int parts, out Shortcut shortcut, out string error)
    {
        shortcut = default;
        var fields = arguments.Split(',', parts, StringSplitOptions.TrimEntries);
        if (fields.Length < parts - 1)
        {
            error = name + ": invalid args";
            return false;
        }

        if (!ModifierNames.TryParse(fields[0], out var modifiers, out _))
        {
            error = name + ": invalid modifiers";
            return false;
        }

        if (!TryReadKey(name, fields[1], out var key, out error))
            return false;

        WindowId? target = null;
        var selector = fields.Length > 2 ? fields[2] : "";
        if (selector.Length > 0)
        {
            if (Select(selector, emptyMeansActive: false) is not WindowId found)
            {
                error = name + ": window not found";
                return false;
            }

            target = found;
        }

        shortcut = new Shortcut(target, key, modifiers);
        error = "";
        return true;
    }

    // A number above 9 and code:N are keycodes, mouse:N is a button from 272, anything else is a key name.
    private static bool TryReadKey(string name, string text, out string key, out string error)
    {
        key = "";
        error = "";
        if (text.Length > 0 && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 9)
        {
            key = "code:" + number.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (KeyStrokes.TryMouse(text, out var button))
        {
            if (button < KeyStrokes.FirstMouseButton)
            {
                error = name + ": invalid mouse button";
                return false;
            }

            key = text;
            return true;
        }

        if (KeyStrokes.TryCode(text, out _) || KeyStrokes.TryResolve(text, 0, KeyLayout.UnitedStates, out _))
        {
            key = text;
            return true;
        }

        error = text.Length == 0 || Is(text, "code:0") ? name + ": invalid key" : name + ": key not found";
        return false;
    }

    private readonly record struct Shortcut(WindowId? Target, string Key, Modifier Modifiers)
    {
        public KeyRelay Relay(KeyRelayPhase phase) => new(Target, Key, phase) { Modifiers = Modifiers };
    }
}
