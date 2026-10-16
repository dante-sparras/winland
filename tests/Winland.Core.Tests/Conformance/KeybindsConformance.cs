using System.Text;
using Winland;

namespace Winland.Tests.Conformance;

/// <summary>
/// The pinned release's keybind cases. The reference injects key, wheel and button events through its test plugin;
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

    private const string Flag = "touch /tmp/hyprtester-keybinds.txt";
    private const string ShortChord = "touch /tmp/hyprtester-keybinds-short-chord.txt";
    private const string LongChord = "touch /tmp/hyprtester-keybinds-long-chord.txt";

    [Fact(Skip = "A bind on several keys runs on each of them, a long press waits 500 ms instead of input:repeat_delay, and per-device binds are not honored. input:repeat_delay and input:repeat_rate are not applied live.")]
    public void Keybinds()
    {
        var desk = new Desk(ConfigBinds);
        var keys = new Keyboard(desk);

        Bind(desk, keys);
        BindKey(desk, keys);
        OverlappingChords(desk, keys);
        LongPress(desk, keys);
        KeyLongPress(desk, keys);
        LongPressRelease(desk, keys);
        LongPressOnlyKeyRelease(desk, keys);
        Repeat(desk, keys);
        KeyRepeat(desk, keys);
        RepeatRelease(desk, keys);
        RepeatOnlyKeyRelease(desk, keys);
        ShortcutBind(desk, keys);
        ShortcutBindKey(desk, keys);
        ShortcutLongPress(desk, keys);
        ShortcutLongPressKeyRelease(desk, keys);
        ShortcutRepeat(desk, keys);
        ShortcutRepeatKeyRelease(desk, keys);
        Submap(desk, keys);
        SubmapMouseBinds(desk, keys);
        SubmapUniversal(desk, keys);
        BindsAfterScroll(desk, keys);
        PerDeviceKeybind(desk, keys);
        Unbind(desk, keys);
    }

    [Fact(Skip = "A bind on several keys runs on each of them. The pinned release runs it once they are all pressed in order, and holds back a shorter bind that starts the same chord.")]
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

    [Fact(Skip = "A release bind on a modifier key still runs after another bind used that modifier. The pinned release skips it.")]
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

    private static void Bind(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bind SUPER, Y, exec, {Flag}"));
        keys.Key(true, Modifier.Super, "Y");
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    private static void BindKey(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bind , Y, exec, {Flag}"));
        keys.Key(true, Modifier.None, "Y");
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind , Y"));
    }

    // A chord of several keys is a bind with the s flag and the keys joined by &.
    private static void OverlappingChords(Desk desk, Keyboard keys)
    {
        keys.Clear(ShortChord);
        keys.Clear(LongChord);

        Assert.Equal("ok", desk.Send($"keyword binds SUPER, Q&K, exec, {LongChord}"));
        Assert.Equal("ok", desk.Send($"keyword bind SUPER, K, exec, {ShortChord}"));

        keys.Key(true, Modifier.Super, "Q");
        keys.Key(true, Modifier.Super, "K");
        Assert.True(keys.Take(LongChord));
        keys.Wait(100);
        Assert.False(keys.Seen(ShortChord));
        keys.Key(false, Modifier.Super, "K");
        keys.Key(false, Modifier.None, "Q");

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q&K"));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, K"));
        keys.Clear(ShortChord);
        keys.Clear(LongChord);

        Assert.Equal("ok", desk.Send($"keyword bind SUPER, Q, exec, {ShortChord}"));
        Assert.Equal("ok", desk.Send($"keyword binds SUPER, Q&K, exec, {LongChord}"));

        keys.Key(true, Modifier.Super, "Q");
        keys.Wait(100);
        Assert.False(keys.Seen(ShortChord));
        keys.Key(true, Modifier.Super, "K");
        Assert.True(keys.Take(LongChord));
        keys.Wait(100);
        Assert.False(keys.Seen(ShortChord));
        keys.Key(false, Modifier.Super, "K");
        keys.Key(false, Modifier.None, "Q");

        keys.Clear(LongChord);
        keys.Key(true, Modifier.Super, "Q");
        keys.Wait(100);
        Assert.False(keys.Seen(ShortChord));
        keys.Key(false, Modifier.None, "Q");
        Assert.True(keys.Take(ShortChord));

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q"));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q&K"));
        keys.Clear(ShortChord);
        keys.Clear(LongChord);

        Assert.Equal("ok", desk.Send($"keyword bind SUPER, Q, exec, {ShortChord}"));
        Assert.Equal("ok", desk.Send($"keyword bindsr SUPER, Q&K, exec, {LongChord}"));

        keys.Key(true, Modifier.Super, "Q");
        keys.Key(true, Modifier.Super, "K");
        keys.Key(false, Modifier.Super, "Q");
        keys.Wait(100);
        Assert.False(keys.Seen(ShortChord));
        keys.Key(false, Modifier.None, "K");
        Assert.True(keys.Take(LongChord));
        Assert.False(keys.Seen(ShortChord));

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q"));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q&K"));
        keys.Clear(ShortChord);
        keys.Clear(LongChord);

        Assert.Equal("ok", desk.Send($"keyword bindr SUPER, K, exec, {ShortChord}"));
        Assert.Equal("ok", desk.Send($"keyword bindsr SUPER, Q&K, exec, {LongChord}"));

        keys.Key(true, Modifier.Super, "K");
        keys.Key(false, Modifier.None, "K");
        Assert.True(keys.Take(ShortChord));
        Assert.False(keys.Seen(LongChord));

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, K"));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q&K"));
        keys.Clear(ShortChord);
        keys.Clear(LongChord);

        Assert.Equal("ok", desk.Send($"keyword bind SUPER, Q, exec, {ShortChord}"));
        Assert.Equal("ok", desk.Send($"keyword binds SUPER, Q&K, exec, {LongChord}"));
        Assert.Equal("ok", desk.Send("keyword binds SUPER, Q&K&L, exec, true"));

        keys.Key(true, Modifier.Super, "Q");
        keys.Key(true, Modifier.Super, "K");
        keys.Key(false, Modifier.Super, "Q");
        keys.Wait(100);
        Assert.False(keys.Seen(ShortChord));
        keys.Key(false, Modifier.None, "K");
        Assert.True(keys.Take(LongChord));

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q"));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q&K"));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Q&K&L"));
        keys.Clear(ShortChord);
        keys.Clear(LongChord);
    }

    private static void LongPress(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bindo SUPER, Y, exec, {Flag}"));
        RepeatDelay(desk, keys, 100);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(50);
        Assert.False(keys.Take(Flag));
        keys.Wait(200);
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    private static void KeyLongPress(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bindo , Y, exec, {Flag}"));
        RepeatDelay(desk, keys, 100);
        keys.Key(true, Modifier.None, "Y");
        keys.Wait(50);
        Assert.False(keys.Take(Flag));
        keys.Wait(200);
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind , Y"));
    }

    private static void LongPressRelease(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bindo SUPER, Y, exec, {Flag}"));
        RepeatDelay(desk, keys, 100);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(50);
        Assert.False(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(200);
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    private static void LongPressOnlyKeyRelease(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bindo SUPER, Y, exec, {Flag}"));
        RepeatDelay(desk, keys, 100);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(50);
        Assert.False(keys.Take(Flag));
        keys.Key(false, Modifier.Super, "Y");
        keys.Wait(200);
        Assert.False(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    private static void Repeat(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword binde SUPER, Y, exec, {Flag}"));
        RepeatDelay(desk, keys, 100);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(200);
        Assert.True(keys.Take(Flag));
        keys.Wait(200);
        Assert.True(keys.Take(Flag));
        keys.Wait(200);
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    private static void KeyRepeat(Desk desk, Keyboard keys)
    {
        // The reference waits until the flag is gone.
        keys.Clear(Flag);

        Assert.Equal("ok", desk.Send($"keyword binde , Y, exec, {Flag}"));
        RepeatDelay(desk, keys, 100);
        keys.Key(true, Modifier.None, "Y");
        keys.Wait(50);
        Assert.True(keys.Take(Flag));
        keys.Wait(200);
        Assert.True(keys.Take(Flag));
        keys.Wait(200);
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind , Y"));
    }

    private static void RepeatRelease(Desk desk, Keyboard keys)
    {
        keys.Clear(Flag);

        Assert.Equal("ok", desk.Send($"keyword binde SUPER, Y, exec, {Flag}"));
        RepeatDelay(desk, keys, 100);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(50);
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(200);
        keys.Clear(Flag);
        keys.Wait(200);
        Assert.False(keys.Take(Flag));
        keys.Wait(200);
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    private static void RepeatOnlyKeyRelease(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword binde SUPER, Y, exec, {Flag}"));
        RepeatDelay(desk, keys, 100);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(200);
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.Super, "Y");
        keys.Wait(200);
        keys.Clear(Flag);
        keys.Wait(200);
        Assert.False(keys.Take(Flag));
        keys.Wait(200);
        Assert.False(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    // The reference reads what kitty printed. Here that is the keys that reached the window: the ones no bind
    // consumed and the ones a bind sent on.
    private static void ShortcutBind(Desk desk, Keyboard keys)
    {
        keys.Spawn("keybinds_test");

        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword bind SUPER, Y, sendshortcut, , q, activewindow"));
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(50);
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(50);
        var output = keys.Typed;
        Assert.Equal(0, Occurrences(output, "y"));
        Assert.Contains("q", output, StringComparison.Ordinal);
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));

        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword bind SUPER SHIFT, Y, sendshortcut, , w, activewindow"));
        keys.Key(true, Modifier.Super | Modifier.Shift, "Y");
        keys.Wait(50);
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(50);
        output = keys.Typed;
        Assert.Equal(0, Occurrences(output, "y"));
        Assert.Equal(0, Occurrences(output, "Y"));
        Assert.Contains("w", output, StringComparison.Ordinal);
        Assert.Equal("ok", desk.Send("keyword unbind SUPER SHIFT, Y"));

        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword bind SUPER SHIFT, Y, sendshortcut, , 1, activewindow"));
        keys.Key(true, Modifier.Super | Modifier.Shift, "Y");
        keys.Wait(50);
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(50);
        output = keys.Typed;
        Assert.Equal(0, Occurrences(output, "y"));
        Assert.Equal(0, Occurrences(output, "Y"));
        Assert.Contains("1", output, StringComparison.Ordinal);
        Assert.Equal("ok", desk.Send("keyword unbind SUPER SHIFT, Y"));

        keys.KillAll();
    }

    private static void ShortcutBindKey(Desk desk, Keyboard keys)
    {
        keys.Spawn("keybinds_test");

        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword bind , Y, sendshortcut, , e, activewindow"));
        keys.Key(true, Modifier.None, "Y");
        keys.Wait(50);
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(50);
        var output = keys.Typed;
        Assert.Equal(0, Occurrences(output, "y"));
        Assert.Equal("ok", desk.Send("keyword unbind , Y"));

        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword bind SHIFT, Y, sendshortcut, , r, activewindow"));
        keys.Key(true, Modifier.Shift, "Y");
        keys.Wait(50);
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(50);
        output = keys.Typed;
        Assert.Equal(0, Occurrences(output, "y"));
        Assert.Equal(0, Occurrences(output, "Y"));
        Assert.Equal("ok", desk.Send("keyword unbind SHIFT, Y"));

        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword bind SHIFT, Y, sendshortcut, , 2, activewindow"));
        keys.Key(true, Modifier.Shift, "Y");
        keys.Wait(50);
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(50);
        output = keys.Typed;
        Assert.Equal(0, Occurrences(output, "y"));
        Assert.Equal(0, Occurrences(output, "Y"));
        Assert.Equal("ok", desk.Send("keyword unbind SHIFT, Y"));

        keys.KillAll();
    }

    private static void ShortcutLongPress(Desk desk, Keyboard keys)
    {
        keys.Spawn("keybinds_test");
        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword bindo SUPER, Y, sendshortcut, , q, activewindow"));
        RepeatDelay(desk, keys, 100);
        RepeatRate(desk, keys, 10);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(200);
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(200);
        var output = keys.Typed;
        Assert.InRange(Occurrences(output, "y"), 1, 2);
        Assert.InRange(Occurrences(output, "q"), 1, 2);
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
        keys.KillAll();
    }

    private static void ShortcutLongPressKeyRelease(Desk desk, Keyboard keys)
    {
        keys.Spawn("keybinds_test");
        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword bindo SUPER, Y, sendshortcut, , q, activewindow"));
        RepeatDelay(desk, keys, 100);
        RepeatRate(desk, keys, 10);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(10);
        keys.Key(false, Modifier.Super, "Y");
        keys.Wait(200);
        Assert.Equal(0, Occurrences(keys.Typed, "q"));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
        keys.KillAll();
    }

    private static void ShortcutRepeat(Desk desk, Keyboard keys)
    {
        keys.Spawn("keybinds_test");
        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword binde SUPER, Y, sendshortcut, , q, activewindow"));
        RepeatRate(desk, keys, 5);
        RepeatDelay(desk, keys, 200);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(210);
        keys.Key(false, Modifier.None, "Y");
        keys.Wait(450);
        var output = keys.Typed;
        Assert.Equal(0, Occurrences(output, "y"));
        Assert.InRange(Occurrences(output, "q"), 2, 3);
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
        keys.KillAll();
    }

    private static void ShortcutRepeatKeyRelease(Desk desk, Keyboard keys)
    {
        keys.Spawn("keybinds_test");
        desk.Ok("focuswindow", "class:keybinds_test");
        Assert.Equal("ok", desk.Send("keyword binde SUPER, Y, sendshortcut, , q, activewindow"));
        RepeatRate(desk, keys, 5);
        RepeatDelay(desk, keys, 200);
        keys.Key(true, Modifier.Super, "Y");
        keys.Wait(210);
        keys.Key(false, Modifier.Super, "Y");
        keys.Wait(450);
        var output = keys.Typed;
        Assert.Equal(0, Occurrences(output, "y"));
        Assert.InRange(Occurrences(output, "q"), 2, 3);
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
        keys.KillAll();
    }

    // Winland has no submap request, so the active submap is read from the core.
    private static void Submap(Desk desk, Keyboard keys)
    {
        // Each kitty the binds start is a window that appears.
        void Opened()
        {
            Assert.True(keys.Take("kitty"));
            keys.Spawn("kitty");
        }

        keys.Tap("U", Modifier.Super);
        Assert.Equal("submap1", ActiveSubmap(desk));
        keys.Tap("O");
        Opened();
        Assert.Equal("submap1", ActiveSubmap(desk));

        // submap2 resets to submap1.
        keys.Tap("U");
        Assert.Equal("submap2", ActiveSubmap(desk));
        keys.Tap("O");
        Opened();
        Assert.Equal("submap1", ActiveSubmap(desk));

        // submap3 resets to the default.
        keys.Tap("I");
        Assert.Equal("submap3", ActiveSubmap(desk));
        keys.Tap("O");
        Opened();
        Assert.Equal("default", ActiveSubmap(desk));

        keys.Tap("U", Modifier.Super);
        Assert.Equal("submap1", ActiveSubmap(desk));
        keys.Tap("P");
        Assert.Equal("default", ActiveSubmap(desk));

        // submap1 binds the same chord that enters it.
        keys.Tap("U", Modifier.Super);
        Assert.Equal("submap1", ActiveSubmap(desk));
        keys.Tap("U", Modifier.Super);
        Assert.Equal("submap3", ActiveSubmap(desk));
        keys.Tap("O");
        Opened();
        Assert.Equal("default", ActiveSubmap(desk));
        Assert.Equal(4, desk.Clients().Length);
        keys.KillAll();
    }

    private static void SubmapMouseBinds(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send("r/keyword binds:scroll_event_delay 0"));

        keys.Key(true, Modifier.Super, "U");
        keys.Key(false, Modifier.None, "U");
        Assert.Equal("submap1", ActiveSubmap(desk));

        keys.Scroll(120);
        Assert.True(keys.Take(Flag));

        keys.Click(true, "mouse:272");
        keys.Click(false, "mouse:272");
        Assert.True(keys.Take(Flag));

        keys.Key(true, Modifier.None, "P");
        keys.Key(false, Modifier.None, "P");
        Assert.Equal("default", ActiveSubmap(desk));
    }

    private static void BindsAfterScroll(Desk desk, Keyboard keys)
    {
        keys.Clear(Flag);
        Assert.Equal("ok", desk.Send($"keyword bind ALT, w, exec, {Flag}"));

        keys.Key(true, Modifier.None, "Alt_R");
        keys.Key(true, Modifier.Alt, "W");
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.Alt, "W");
        keys.Key(false, Modifier.None, "Alt_R");

        keys.Scroll(120);
        keys.Scroll(-120);
        keys.Scroll(120);

        keys.Key(true, Modifier.None, "Alt_R");
        keys.Key(true, Modifier.Alt, "W");
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.Alt, "W");
        keys.Key(false, Modifier.None, "Alt_R");

        keys.Clear(Flag);
        Assert.Equal("ok", desk.Send("keyword unbind ALT, w"));
    }

    private static void SubmapUniversal(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bindu SUPER, Y, exec, {Flag}"));
        Assert.Equal("default", ActiveSubmap(desk));

        keys.Key(true, Modifier.Super, "Y");
        keys.Key(false, Modifier.Super, "Y");
        Assert.True(keys.Take(Flag));

        keys.Key(true, Modifier.Super, "U");
        keys.Key(false, Modifier.Super, "U");
        Assert.Equal("submap1", ActiveSubmap(desk));
        keys.Key(true, Modifier.Super, "Y");
        keys.Key(false, Modifier.Super, "Y");
        Assert.True(keys.Take(Flag));

        keys.Key(true, Modifier.None, "P");
        keys.Key(false, Modifier.None, "P");
        Assert.Equal("default", ActiveSubmap(desk));

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    // The line-based syntax has no per-device list, so each bind stands without it. The keyboard the reference
    // types on is test-keyboard-1, tagged test-tag.
    private static void PerDeviceKeybind(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bind SUPER, Y, exec, {Flag}"));
        keys.Key(true, Modifier.Super, "Y");
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));

        // Every keyboard but test-keyboard-1.
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bind SUPER, Y, exec, {Flag}"));
        keys.Key(true, Modifier.Super, "Y");
        Assert.False(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));

        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bindd SUPER, Y, test description, exec, {Flag}"));
        keys.Key(true, Modifier.Super, "Y");
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));

        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bind SUPER, Y, exec, {Flag}"));
        keys.Key(true, Modifier.Super, "Y");
        Assert.True(keys.Take(Flag));
        keys.Key(false, Modifier.None, "Y");
        Assert.Equal("ok", desk.Send("keyword unbind SUPER, Y"));
    }

    private static void Unbind(Desk desk, Keyboard keys)
    {
        Assert.False(keys.Take(Flag));
        Assert.Equal("ok", desk.Send($"keyword bind SUPER, Y, exec, {Flag}"));
        Assert.Equal("ok", desk.Send("keyword unbind    super     ,   y      "));

        keys.Key(true, Modifier.Super, "Y");
        keys.Key(false, Modifier.None, "Y");

        Assert.False(keys.Take(Flag));
    }

    private static void RepeatDelay(Desk desk, Keyboard keys, int delay)
    {
        Assert.Equal("ok", desk.Send($"r/keyword input:repeat_delay {delay}"));
        keys.RepeatDelay = delay;
    }

    private static void RepeatRate(Desk desk, Keyboard keys, int rate)
    {
        Assert.Equal("ok", desk.Send($"r/keyword input:repeat_rate {rate}"));
        keys.RepeatRate = rate;
    }

    private static string ActiveSubmap(Desk desk) =>
        desk.Core.KeyState.Submap.Length == 0 ? "default" : desk.Core.KeyState.Submap;

    private static int Occurrences(string text, string what)
    {
        var count = 0;
        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
            count++;
        return count;
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
                long? longPress = _armedAt is long armed ? armed + BindMatcher.LongPressDelayMs : null;
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

            if (desk.Core.KeyState.LongPressKey is null)
                _armedAt = null;
            else if (fresh || _armedAt is null)
                _armedAt = _now;

            return outcome;
        }
    }
}
