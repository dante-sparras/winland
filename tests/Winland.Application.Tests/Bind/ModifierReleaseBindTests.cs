namespace Winland.Application.Tests;

/// <summary>
/// A release bind on a modifier, and the binds that shadow it. A command a bind runs shows up as a Launch.
/// </summary>
public class ModifierReleaseBindTests
{
    // A full set of binds and submaps, so the binds a test adds must work alongside them.
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
    public void ModifierReleaseBindShadowing()
    {
        var desk = new TestDesktop(ConfigBinds);
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
    /// A keyboard on the desktop that counts the commands binds run. A key is sent with the modifiers the test names
    /// for it: a named modifier that no key holds is pressed first, and one no longer named is let go first.
    /// </summary>
    private sealed class Keyboard(TestDesktop desk)
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

        internal Outcome Key(bool pressed, Modifier modifiers, string key)
        {
            Hold(modifiers);
            return pressed ? Press(key) : Release(key);
        }

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
            return Feed(new KeyPressed(key));
        }

        private Outcome Release(string key)
        {
            _held.Remove(key);
            return Feed(new KeyReleased(key));
        }

        private Outcome Feed(Input input)
        {
            var outcome = desk.Handle(input);
            foreach (var launch in outcome.Launches)
                _launches.Add(launch.CommandLine);

            return outcome;
        }
    }
}
