using System.Text;

namespace Winland.Application.Tests.Conformance;

/// <summary>
/// Winland's keybind cases. The reference injects key, wheel and button events through its test plugin;
/// here they are core input events. A command the reference runs from a bind shows up as a Launch.
/// </summary>
[Trait("Category", "Conformance")]
public class KeybindsConformance
{
    // The binds and submaps of the reference config, in the line-based syntax.
    internal const string ConfigBinds = """
        bind = SUPER, Q, exec, kitty
        bind = SUPER, C, killactive
        bind = SUPER, M, exit
        bind = SUPER, E, exec, dolphin
        bind = SUPER, V, togglefloating
        bind = SUPER, R, exec, wofi --show drun
        bind = SUPER, P, pseudo
        bind = SUPER, J, layoutmsg, togglesplit
        bind = SUPER, left, movefocus, l
        bind = SUPER, right, movefocus, r
        bind = SUPER, up, movefocus, u
        bind = SUPER, down, movefocus, d
        bind = SUPER, 1, workspace, 1
        bind = SUPER SHIFT, 1, movetoworkspace, 1
        bind = SUPER, 2, workspace, 2
        bind = SUPER SHIFT, 2, movetoworkspace, 2
        bind = SUPER, 3, workspace, 3
        bind = SUPER SHIFT, 3, movetoworkspace, 3
        bind = SUPER, 4, workspace, 4
        bind = SUPER SHIFT, 4, movetoworkspace, 4
        bind = SUPER, 5, workspace, 5
        bind = SUPER SHIFT, 5, movetoworkspace, 5
        bind = SUPER, 6, workspace, 6
        bind = SUPER SHIFT, 6, movetoworkspace, 6
        bind = SUPER, 7, workspace, 7
        bind = SUPER SHIFT, 7, movetoworkspace, 7
        bind = SUPER, 8, workspace, 8
        bind = SUPER SHIFT, 8, movetoworkspace, 8
        bind = SUPER, 9, workspace, 9
        bind = SUPER SHIFT, 9, movetoworkspace, 9
        bind = SUPER, 0, workspace, 10
        bind = SUPER SHIFT, 0, movetoworkspace, 10
        bind = SUPER, S, togglespecialworkspace, magic
        bind = SUPER SHIFT, S, movetoworkspace, special:magic
        bind = SUPER, mouse_down, workspace, e+1
        bind = SUPER, mouse_up, workspace, e-1
        bindm = SUPER, mouse:272, movewindow
        bindm = SUPER, mouse:273, resizewindow
        bindel = , XF86AudioRaiseVolume, exec, wpctl set-volume -l 1 @DEFAULT_AUDIO_SINK@ 5%+
        bindel = , XF86AudioLowerVolume, exec, wpctl set-volume @DEFAULT_AUDIO_SINK@ 5%-
        bindel = , XF86AudioMute, exec, wpctl set-mute @DEFAULT_AUDIO_SINK@ toggle
        bindel = , XF86AudioMicMute, exec, wpctl set-mute @DEFAULT_AUDIO_SOURCE@ toggle
        bindel = , XF86MonBrightnessUp, exec, brightnessctl s 10%+
        bindel = , XF86MonBrightnessDown, exec, brightnessctl s 10%-
        bindl = , XF86AudioNext, exec, playerctl next
        bindl = , XF86AudioPause, exec, playerctl play-pause
        bindl = , XF86AudioPlay, exec, playerctl play-pause
        bindl = , XF86AudioPrev, exec, playerctl previous
        bind = SUPER, U, submap, submap1
        submap = submap1
        bind = , U, submap, submap2
        bind = SUPER, U, submap, submap3
        bind = , I, submap, submap3
        bind = , O, exec, kitty
        bind = , P, submap, reset
        bind = , mouse_down, exec, touch /tmp/hyprtester-keybinds.txt
        bind = , mouse:272, exec, touch /tmp/hyprtester-keybinds.txt
        submap = reset
        submap = submap2, submap1
        bind = , O, exec, kitty
        submap = reset
        submap = submap3, reset
        bind = , O, exec, kitty
        submap = reset
        """;

    [Fact]
    public void OrderedSubChordDeferral()
    {
        var desk = new Desk(ConfigBinds);
        var keys = new Keyboard(desk);
        string Counts() => $"{keys.Count("overlap-x")}:{keys.Count("overlap-d")}:{keys.Count("overlap-long")}";

        Assert.Equal("ok", desk.Send("keyword bind SUPER, X, exec, overlap-x"));
        Assert.Equal("ok", desk.Send("keyword bind SUPER, D, exec, overlap-d"));
        Assert.Equal("ok", desk.Send("keyword binds SUPER, X&D&F, exec, overlap-long"));

        keys.Key(true, Modifier.Super, "X");
        Assert.Equal("0:0:0", Counts());
        keys.Key(false, Modifier.None, "X");
        Assert.Equal("1:0:0", Counts());

        // Without X held first, D cannot extend the longer chord and runs at once.
        keys.Key(true, Modifier.Super, "D");
        Assert.Equal("1:1:0", Counts());
        keys.Key(false, Modifier.None, "D");
        Assert.Equal("1:1:0", Counts());

        keys.Key(true, Modifier.Super, "X");
        Assert.Equal("1:1:0", Counts());
        keys.Key(true, Modifier.Super, "D");
        Assert.Equal("1:1:0", Counts());
        keys.Key(true, Modifier.Super, "F");
        Assert.Equal("1:1:1", Counts());
        keys.Key(false, Modifier.Super, "F");
        keys.Key(false, Modifier.Super, "D");
        keys.Key(false, Modifier.None, "X");
        Assert.Equal("1:1:1", Counts());

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, X"));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, D"));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, X&D&F"));
    }

    [Fact]
    public void ModifierReleaseBindShadowing()
    {
        var desk = new Desk(ConfigBinds);
        var keys = new Keyboard(desk);
        string Counts() => $"{keys.Count("alt-release")}:{keys.Count("alt-p")}";

        Assert.Equal("ok", desk.Send("keyword bindr ALT, Alt_L, exec, alt-release"));
        Assert.Equal("ok", desk.Send("keyword bind ALT, P, exec, alt-p"));

        keys.Key(true, Modifier.None, "Alt_L");
        Assert.Equal("0:0", Counts());
        keys.Key(true, Modifier.Alt, "P");
        Assert.Equal("0:1", Counts());
        keys.Key(false, Modifier.Alt, "P");
        keys.Key(false, Modifier.None, "Alt_L");
        Assert.Equal("0:1", Counts());

        keys.Key(true, Modifier.None, "Alt_L");
        keys.Key(false, Modifier.None, "Alt_L");
        Assert.Equal("1:1", Counts());

        Assert.Equal("ok", desk.Send("keyword unbind ALT, Alt_L"));
        Assert.Equal("ok", desk.Send("keyword unbind ALT, P"));
    }

    /// <summary>
    /// The reference test plugin's keyboard, wheel and button, on the core. A key is sent with the modifiers the
    /// reference sets for it: a modifier it names that no key holds is pressed first, and one no longer named is let
    /// go first. The keyboard repeats the last key held as the system would, and a long press elapses when the
    /// adapter's timer would. Time only passes in <see cref="Wait"/>.
    /// </summary>
    private sealed class Keyboard(Desk desk)
    {
        private static readonly (Modifier Modifier, string Key)[] Pressed =
        [
            (Modifier.Super, "Super_L"),
            (Modifier.Alt, "Alt_L"),
            (Modifier.Ctrl, "Control_L"),
            (Modifier.Shift, "Shift_L"),
        ];

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

        private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _synthetic = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _launches = [];
        private readonly List<WindowId> _windows = [];
        private readonly StringBuilder _typed = new();
        private long _now = 1_000;
        private long? _armedAt;
        private string? _repeating;
        private long _nextRepeat;

        internal int RepeatDelay { get; set; } = 600;

        internal int RepeatRate { get; set; } = 25;

        /// <summary>What reached the window opened last: keys no bind consumed, and keys a bind sent on.</summary>
        internal string Typed => _typed.ToString();

        internal void Spawn(string windowClass)
        {
            _windows.Add(desk.Spawn(windowClass));
            _typed.Clear();
        }

        internal void KillAll()
        {
            foreach (var window in _windows)
                Feed(new WindowClosed(window), fresh: false);
            _windows.Clear();
        }

        internal Outcome Key(bool pressed, Modifier modifiers, string key)
        {
            Hold(modifiers);
            return pressed ? Press(key) : Release(key);
        }

        /// <summary>Presses and releases a key under the same modifiers, as the reference's submap test does.</summary>
        internal void Tap(string key, Modifier modifiers = Modifier.None)
        {
            Key(true, modifiers, key);
            Key(false, modifiers, key);
        }

        internal void Scroll(int delta) =>
            Feed(new WheelTurned(delta > 0 ? "mouse_down" : "mouse_up", _now), fresh: false);

        internal void Click(bool pressed, string button) =>
            Feed(pressed ? new PointerButtonPressed(button) { Position = desk.Pointer } : new PointerButtonReleased(button) { Position = desk.Pointer }, fresh: false);

        internal void Wait(int milliseconds)
        {
            var end = _now + milliseconds;
            while (true)
            {
                long? longPress = _armedAt is long armed ? armed + RepeatDelay : null;
                long? repeat = _repeating is not null && RepeatRate > 0 ? _nextRepeat : null;
                var next = Math.Min(longPress ?? long.MaxValue, repeat ?? long.MaxValue);
                if (next >= end)
                    break;

                _now = next;
                if (longPress == next)
                {
                    _armedAt = null;
                    Feed(new LongPressElapsed(), fresh: false);
                }
                else
                {
                    _nextRepeat += 1000 / RepeatRate;
                    Feed(new KeyPressed(_repeating!), fresh: false);
                }
            }

            _now = end;
        }

        /// <summary>Whether the command ran since it was last taken, and forgets that it did.</summary>
        internal bool Take(string command) => _launches.RemoveAll(line => line == command) > 0;

        internal bool Seen(string command) => _launches.Contains(command);

        internal void Clear(string command) => _launches.RemoveAll(line => line == command);

        internal int Count(string command) => _launches.Count(line => line == command);

        private void Hold(Modifier modifiers)
        {
            foreach (var key in _synthetic.ToArray())
            {
                if ((modifiers & ModifierKeys[key]) == 0)
                {
                    _synthetic.Remove(key);
                    Release(key);
                }
            }

            foreach (var (modifier, key) in Pressed)
            {
                if ((modifiers & modifier) != 0 && !_held.Any(held => ModifierKeys.TryGetValue(held, out var heldModifier) && heldModifier == modifier))
                {
                    _synthetic.Add(key);
                    Press(key);
                }
            }
        }

        private Outcome Press(string key)
        {
            _held.Add(key);
            if (!ModifierKeys.ContainsKey(key))
            {
                _repeating = key;
                _nextRepeat = _now + RepeatDelay;
            }

            return Feed(new KeyPressed(key), fresh: true);
        }

        private Outcome Release(string key)
        {
            _held.Remove(key);
            if (string.Equals(_repeating, key, StringComparison.OrdinalIgnoreCase))
                _repeating = null;

            return Feed(new KeyReleased(key), fresh: false);
        }

        private Outcome Feed(Input input, bool fresh)
        {
            var outcome = desk.Handle(input);
            foreach (var launch in outcome.Launches)
                _launches.Add(launch.CommandLine);

            if (input is KeyPressed { Key: var key } && !outcome.Consumed && key.Length == 1)
                _typed.Append(_held.Contains("Shift_L") || _held.Contains("Shift_R") ? key.ToUpperInvariant() : key.ToLowerInvariant());

            foreach (var relay in outcome.Relays)
            {
                if (relay.Phase != KeyRelayPhase.Release)
                    _typed.Append(relay.Key.Equals("return", StringComparison.OrdinalIgnoreCase) ? "\n" : relay.Key);
            }

            if (desk.WindowManager.KeyState.LongPressKey is null)
                _armedAt = null;
            else if (fresh || _armedAt is null)
                _armedAt = _now;

            return outcome;
        }
    }
}
