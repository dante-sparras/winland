using System.Globalization;
using System.Text.Json;

namespace Winland.Application.Tests;

/// <summary>
/// Windows, driven through pipe lines on a <see cref="TestDesktop"/>: swapping, fullscreen, pins, rules, exec rules and floating moves.
/// </summary>
public class WindowBehaviorTests
{
    [Fact]
    public void SwapWindow()
    {
        var desk = new TestDesktop();
        desk.Ok("workspace", "2");
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");
        Assert.Equal(2, desk.Clients().Length);

        desk.Dispatch("focuswindow", "class:kitty_A");
        var at = desk.ActivePlace().At;
        desk.Ok("swapwindow", "r");
        desk.Ok("focuswindow", "class:kitty_B");
        Assert.Equal(at, desk.ActivePlace().At);

        desk.Dispatch("focuswindow", "class:kitty_A");
        at = desk.ActivePlace().At;
        desk.Ok("swapwindow", "class:kitty_B");
        desk.Ok("focuswindow", "class:kitty_B");
        Assert.Equal(at, desk.ActivePlace().At);

        desk.Dispatch("focuswindow", "class:kitty_B");
        var address = Address(desk.ActiveWindow());
        desk.Dispatch("focuswindow", "class:kitty_A");
        at = desk.ActivePlace().At;
        desk.Ok("swapwindow", "address:" + address);
        desk.Ok("focuswindow", "address:" + address);
        Assert.Equal(at, desk.ActivePlace().At);

        desk.Ok("fullscreen", "0");
        Assert.Contains("Can't swap fullscreen window", desk.Dispatch("swapwindow", "l"), StringComparison.Ordinal);
        desk.Ok("fullscreen", "0");

        desk.Dispatch("focuswindow", "class:kitty_B");
        address = Address(desk.ActiveWindow());
        var workspace = WorkspaceName(desk.ActiveWindow());
        desk.Ok("movetoworkspacesilent", "3");
        desk.Ok("swapwindow", "address:" + address);
        desk.Dispatch("focuswindow", "class:kitty_B");
        Assert.Equal(workspace, WorkspaceName(desk.ActiveWindow()));
    }

    [Fact]
    public void WindowFocusOnFullscreenConflict()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");

        desk.Keyword("misc:focus_on_activate", "true");

        // Unfullscreen on conflict.
        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("fullscreen", "0 set");
        ExpectFocused(desk, "kitty_A", 2);

        desk.Ok("focuswindow", "class:kitty_A");
        ExpectFocused(desk, "kitty_A", 2);

        desk.Ok("focuswindow", "class:kitty_B");
        ExpectFocused(desk, "kitty_B", 0);

        var activating = desk.Spawn("kitty_activating");
        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("fullscreen", "0 set");
        ExpectFocused(desk, "kitty_A", 2);
        Activate(desk, activating);
        ExpectFocused(desk, "kitty_activating", 0);
        desk.CloseActive();
        Assert.Equal(2, desk.Clients().Length);
    }

    [Fact]
    public void FocusFloatingOrTilingUnderFullscreen()
    {
        var desk = new TestDesktop("""
            misc:focus_on_activate = true
            """);
        desk.Spawn("kitty_a");
        desk.Spawn("kitty_b");

        ExpectFocused(desk, "kitty_b", 0);
        desk.Ok("togglefloating");

        // A floating window over a fullscreen tile.
        desk.Ok("focuswindow", "class:kitty_a");
        desk.Ok("fullscreen", "0 set");
        ExpectFocused(desk, "kitty_a", 2);

        desk.Ok("focuswindow", "floating");
        Assert.Contains("kitty_b", desk.ActiveClass(), StringComparison.Ordinal);
        Assert.Equal(2, TestDesktop.Fullscreen(desk.Client("kitty_a")));
        Assert.Equal(2, TestDesktop.FullscreenClient(desk.Client("kitty_a")));

        // A tile under a fullscreen floating window.
        desk.Ok("fullscreen", "0 set");
        desk.Ok("focuswindow", "tiled");
        Assert.Contains("kitty_a", desk.ActiveClass(), StringComparison.Ordinal);
        Assert.Equal(0, TestDesktop.Fullscreen(desk.Client("kitty_b")));
    }

    [Fact]
    public void WindowMaximizeSize()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_A");

        Assert.Equal(("22,22", "1876,1036"), desk.PlaceOf("kitty_A"));
        Assert.Equal(0, TestDesktop.Fullscreen(desk.Client("kitty_A")));

        desk.Ok("fullscreen", "1");

        Assert.Equal(("22,22", "1876,1036"), desk.PlaceOf("kitty_A"));
        Assert.Equal(1, TestDesktop.Fullscreen(desk.Client("kitty_A")));
    }

    [Fact]
    public void GroupFallbackFocus()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_A");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_B");
        desk.Spawn("kitty_C");
        desk.Spawn("kitty_D");

        Assert.Equal("kitty_D", desk.ActiveClass());

        desk.Ok("focuswindow", "class:kitty_B");
        desk.Ok("focuswindow", "class:kitty_D");
        desk.CloseActive();
        Assert.Equal(3, desk.Clients().Length);

        Assert.Equal("kitty_B", desk.ActiveClass());
    }

    [Fact]
    public void WindowRuleFocusOnActivate()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_default");

        // focus_on_activate off on the rule wins.
        desk.Keyword("misc:focus_on_activate", "true");
        Assert.Equal("ok", desk.Send("keyword windowrule focus_on_activate 0, class:kitty_antifocus"));

        var antifocus = desk.Spawn("kitty_antifocus");
        ExpectFocused(desk, "kitty_antifocus", 0);
        desk.Ok("focuswindow", "class:kitty_default");
        ExpectFocused(desk, "kitty_default", 0);

        Activate(desk, antifocus);
        Assert.NotEqual("kitty_antifocus", desk.ActiveClass());

        // focus_on_activate on on the rule wins.
        desk.Keyword("misc:focus_on_activate", "false");
        Assert.Equal("ok", desk.Send("keyword windowrule focus_on_activate 1, class:kitty_superfocus"));

        var superfocus = desk.Spawn("kitty_superfocus");
        ExpectFocused(desk, "kitty_superfocus", 0);
        desk.Ok("focuswindow", "class:kitty_default");
        ExpectFocused(desk, "kitty_default", 0);

        Activate(desk, superfocus);
        ExpectFocused(desk, "kitty_superfocus", 0);
    }

    [Fact]
    public void PinnedWorkspacesValid()
    {
        var desk = new TestDesktop();
        desk.Dispatch("workspace", "1337");
        desk.Spawn("kitty");

        desk.Ok("togglefloating", "class:kitty");
        desk.Ok("pin", "class:kitty");

        var active = desk.ActiveWindow();
        Assert.Equal(1337, TestDesktop.WorkspaceId(active));
        Assert.True(TestDesktop.Pinned(active));

        desk.Dispatch("workspace", "1338");

        active = desk.ActiveWindow();
        Assert.Equal(1338, TestDesktop.WorkspaceId(active));
        Assert.True(TestDesktop.Pinned(active));

        desk.Ok("togglefloating", "class:kitty");

        active = desk.ActiveWindow();
        Assert.Equal(1338, TestDesktop.WorkspaceId(active));
        Assert.False(TestDesktop.Pinned(active));
    }

    [Fact]
    public void WindowruleWorkspaceEmpty()
    {
        var desk = new TestDesktop("""
            windowrule = workspace empty, class:kitty_A
            windowrule = workspace emptyn, class:kitty_B
            """);

        desk.Dispatch("workspace", "3");
        desk.Spawn("kitty");
        Assert.Equal(3, TestDesktop.WorkspaceId(desk.ActiveWindow()));

        desk.Spawn("kitty_A");
        Assert.Equal(1, TestDesktop.WorkspaceId(desk.ActiveWindow()));

        desk.Dispatch("workspace", "3");
        desk.Spawn("kitty_B");
        Assert.Equal(4, TestDesktop.WorkspaceId(desk.ActiveWindow()));
    }

    [Fact]
    public void Issue14038()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_14038");

        desk.Ok("movetoworkspacesilent", "special:a,class:kitty_14038");
        desk.Ok("togglefloating", "class:kitty_14038");
        desk.Ok("pin", "class:kitty_14038");
        desk.Ok("togglefloating", "class:kitty_14038");
    }

    [Fact]
    public void Issue14134()
    {
        var desk = new TestDesktop();
        desk.AddMonitor("HEADLESS-4", TestDesktop.Second);
        desk.Ok("focusmonitor", "HEADLESS-4");

        desk.Spawn("kitty_14134");

        desk.Ok("togglefloating", "class:kitty_14134");
        desk.Ok("pin", "class:kitty_14134");

        // Disabling the monitor takes it away, as unplugging it does.
        desk.RemoveMonitor("HEADLESS-4");

        // The reference answers an error here and only checks that it is still alive.
        desk.Dispatch("pin", "class:kitty_14134");

        Assert.Equal("ok", desk.Send("reload"));
        CloseAll(desk);
        Assert.Empty(desk.Clients());
    }

    [Fact]
    public void ExactWindowSelectors()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");

        desk.Dispatch("focuswindow", "class:kitty_B");
        var target = desk.ActiveWindow();
        var address = Address(target);
        var pid = target.GetProperty("pid").GetInt64().ToString(CultureInfo.InvariantCulture);
        var stableId = target.GetProperty("stableId").GetString();

        desk.Dispatch("focuswindow", "class:kitty_A");
        desk.Ok("focuswindow", "address:" + address);
        Assert.Equal("kitty_B", desk.ActiveClass());

        desk.Dispatch("focuswindow", "class:kitty_A");
        desk.Ok("focuswindow", "pid:" + pid);
        Assert.Equal("kitty_B", desk.ActiveClass());

        desk.Dispatch("focuswindow", "class:kitty_A");
        desk.Ok("focuswindow", "stableid:" + stableId);
        Assert.Equal("kitty_B", desk.ActiveClass());
    }

    [Fact]
    public void CycleNextTiled()
    {
        var desk = new TestDesktop();
        foreach (var name in new[] { "a", "b", "c", "d" })
            desk.Spawn(name);

        desk.Ok("togglefloating", "class:a");
        desk.Ok("togglefloating", "class:c");
        desk.Ok("togglefloating", "class:d");

        // Focus history: b is the only tile.
        desk.Ok("focuswindow", "class:a");
        desk.Ok("focuswindow", "class:b");
        desk.Ok("focuswindow", "class:c");
        desk.Ok("focuswindow", "class:d");

        desk.Ok("cyclenext", "tiled");

        Assert.Equal("b", desk.ActiveClass());
    }

    [Fact]
    public void CycleNextFloating()
    {
        var desk = new TestDesktop();
        foreach (var name in new[] { "a", "b", "c", "d" })
            desk.Spawn(name);

        desk.Ok("togglefloating", "class:b");

        // Focus history: b is the only floating window.
        desk.Ok("focuswindow", "class:a");
        desk.Ok("focuswindow", "class:b");
        desk.Ok("focuswindow", "class:c");
        desk.Ok("focuswindow", "class:d");

        desk.Ok("cyclenext", "floating");

        Assert.Equal("b", desk.ActiveClass());
    }

    [Fact]
    public void ExecRulesWorkspaceOverride()
    {
        var desk = new TestDesktop("windowrule = workspace 2, class:kitty_exec_override\n");

        Launch(desk, "[workspace 3] kitty --class kitty_exec_override", pid: Pid(1));
        ExpectProcess(desk.Spawn("kitty_exec_override"), Pid(1));
        Assert.Single(desk.Clients());

        var active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_override", TestDesktop.ClassOf(active));
        Assert.Equal(3, TestDesktop.WorkspaceId(active));

        CloseAll(desk);
    }

    [Fact]
    public void ExecRulesTagMutation()
    {
        var desk = new TestDesktop("""
            windowrule = workspace 2, class:kitty_tag_mutate
            windowrule = tag test_tag, class:kitty_tag_mutate
            """);

        Launch(desk, "[workspace 3] kitty --class kitty_tag_mutate", pid: Pid(1));
        ExpectProcess(desk.Spawn("kitty_tag_mutate"), Pid(1));
        Assert.Single(desk.Clients());

        var active = desk.ActiveWindow();
        Assert.Equal("kitty_tag_mutate", TestDesktop.ClassOf(active));
        Assert.Equal(3, TestDesktop.WorkspaceId(active));

        CloseAll(desk);
    }

    [Fact]
    public void ExecRulesDescendantProcess()
    {
        var desk = new TestDesktop();

        // The shell is the launched process, and kitty is its child.
        Launch(desk, "[workspace 2 silent] kitty --class kitty_exec_workspace & wait", pid: 4001);
        Started(desk, window: 1, parent: 4001);
        ExpectProcess(desk.Spawn("kitty_exec_workspace"), Pid(1));
        Assert.Single(desk.Clients());

        Assert.Contains("false", desk.Send("getprop class:kitty_exec_workspace no_focus"), StringComparison.Ordinal);
        desk.Ok("focuswindow", "class:kitty_exec_workspace");

        var active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_workspace", TestDesktop.ClassOf(active));
        Assert.Equal(2, TestDesktop.WorkspaceId(active));

        // The options in the rule block follow the command's own rules.
        Launch(desk, "[workspace 3; border_size 7; workspace special:exec_rules silent; no_initial_focus; no_focus] kitty --class kitty_exec_special & wait", pid: 4101);
        Started(desk, window: 2, parent: 4101);
        ExpectProcess(desk.Spawn("kitty_exec_special"), Pid(2));
        Assert.Equal(2, desk.Clients().Length);

        Assert.Contains("true", desk.Send("getprop class:kitty_exec_special no_focus"), StringComparison.Ordinal);
        Assert.Contains("7", desk.Send("getprop class:kitty_exec_special border_size"), StringComparison.Ordinal);

        active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_workspace", TestDesktop.ClassOf(active));
        Assert.Equal(2, TestDesktop.WorkspaceId(active));

        Assert.Equal("ok", desk.Send("setprop class:kitty_exec_special no_focus false"));
        desk.Ok("focuswindow", "class:kitty_exec_special");

        active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_special", TestDesktop.ClassOf(active));
        Assert.Equal("special:exec_rules", WorkspaceName(active));

        CloseAll(desk);
    }

    [Fact]
    public void ExecRulesPendingIsolation()
    {
        var desk = new TestDesktop();

        // Both launches are pending before either window opens.
        Launch(desk, "[workspace 2 silent] sleep 1 && kitty --class kitty_exec_first", pid: 5001);
        Launch(desk, "[workspace special:exec_rules_pending silent; no_initial_focus; no_focus] sleep 2 && kitty --class kitty_exec_second", pid: 5101);

        Started(desk, window: 1, parent: 5001);
        ExpectProcess(desk.Spawn("kitty_exec_first"), Pid(1));
        Assert.Single(desk.Clients());

        Assert.Contains("false", desk.Send("getprop class:kitty_exec_first no_focus"), StringComparison.Ordinal);
        desk.Ok("focuswindow", "class:kitty_exec_first");

        var active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_first", TestDesktop.ClassOf(active));
        Assert.Equal(2, TestDesktop.WorkspaceId(active));

        Started(desk, window: 2, parent: 5101);
        ExpectProcess(desk.Spawn("kitty_exec_second"), Pid(2));
        Assert.Equal(2, desk.Clients().Length);

        Assert.Contains("false", desk.Send("getprop class:kitty_exec_first no_focus"), StringComparison.Ordinal);
        Assert.Contains("true", desk.Send("getprop class:kitty_exec_second no_focus"), StringComparison.Ordinal);

        Assert.Equal("ok", desk.Send("setprop class:kitty_exec_second no_focus false"));
        desk.Ok("focuswindow", "class:kitty_exec_second");

        active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_second", TestDesktop.ClassOf(active));
        Assert.Equal("special:exec_rules_pending", WorkspaceName(active));

        CloseAll(desk);
    }

    [Fact]
    public void PinnedRetainsPositionOnWorkspaceChange()
    {
        var desk = new TestDesktop();
        desk.Spawn("a");

        desk.Ok("workspace", "1");
        desk.Ok("togglefloating");
        desk.Ok("pin");
        Assert.Contains(desk.Clients(), TestDesktop.Pinned);

        desk.MoveTo(desk.ActiveId(), 500, 300);

        desk.Ok("workspace", "2");
        Assert.Contains(desk.Workspaces(), workspace => workspace.GetProperty("id").GetInt32() == 2);

        desk.Ok("workspace", "1");
        Assert.Contains(desk.Workspaces(), workspace => workspace.GetProperty("id").GetInt32() == 1);

        Assert.Contains(desk.Clients(), TestDesktop.Pinned);
        Assert.Equal("500,300", desk.PlaceOf("a").At);
    }

    [Fact]
    public void Monitorrule()
    {
        var desk = new TestDesktop("windowrule = monitor HEADLESS-3, class:monitor_kitty\n");
        desk.AddMonitor("HEADLESS-3", TestDesktop.Second);
        var third = MonitorId(desk, "HEADLESS-3");

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Spawn("monitor_kitty");
        Assert.Single(desk.Clients());
        Assert.Equal(third, desk.ActiveWindow().GetProperty("monitor").GetInt32());
        Assert.Equal("HEADLESS-3", desk.ActiveWorkspace().GetProperty("monitor").GetString());

        CloseAll(desk);
        Assert.Equal("ok", desk.Send("reload"));

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Keyword("windowrule", "monitor HEADLESS-3 silent, class:silent_kitty");

        desk.Spawn("silent_kitty");
        Assert.Equal(third, desk.Clients()[0].GetProperty("monitor").GetInt32());
        Assert.Equal("HEADLESS-2", desk.ActiveWorkspace().GetProperty("monitor").GetString());
    }

    [Fact]
    public void MouseResize()
    {
        var desk = new TestDesktop("windowrule = float, class:kitty\n");
        desk.Spawn("kitty");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kitty");

        ResetWindow(desk);
        Assert.Equal("ok", desk.Send("keyword bindm , mouse:273, resizewindow"));
        Press(desk, "mouse:273");
        desk.MovePointer(700, 200);
        Release(desk, "mouse:273");
        Assert.Equal("700,200", desk.PlaceOf("kitty").Size);

        ResetWindow(desk);
        Assert.Equal("ok", desk.Send("keyword bindm , mouse:273, resizewindow 1"));
        Press(desk, "mouse:273");
        desk.MovePointer(1280, 100);
        Release(desk, "mouse:273");
        Assert.Equal("1280,800", desk.PlaceOf("kitty").Size);
    }

    [Fact]
    public void GetFullscreenHandler()
    {
        var desk = new TestDesktop();
        Assert.Equal("ok", desk.Send("keyword general:layout dwindle"));

        desk.Spawn("cat");

        desk.Ok("fullscreen", "0");
        Assert.Equal("default", Handler(desk));
        desk.Ok("fullscreen", "0");

        desk.Ok("togglefloating");

        desk.Ok("fullscreen", "0");
        Assert.Equal("default", Handler(desk));
        desk.Ok("fullscreen", "0");

        Assert.Equal("ok", desk.Send("keyword general:layout scrolling"));

        desk.Ok("fullscreen", "0");
        Assert.Equal("default", Handler(desk));
        desk.Ok("fullscreen", "0");

        desk.Ok("togglefloating");

        // The reference turns layout_aware off here.
        desk.Ok("fullscreen", "0 default");
        Assert.Equal("default", Handler(desk));
        desk.Ok("fullscreen", "0 default");

        desk.Ok("fullscreen", "0");
        Assert.Equal("scrolling", Handler(desk));
        desk.Ok("fullscreen", "0");
    }

    [Fact]
    public void FloatingForceOnscreen()
    {
        var desk = new TestDesktop();
        desk.Spawn("a");
        desk.Ok("togglefloating");
        // Fully on screen.
        desk.Place(desk.ActiveId(), -10, 0, 100, 100);
        Assert.Equal("2,2", desk.ActivePlace().At);

        desk.CloseActive();

        // The reference places a new floating window at 0,0 rather than 2,2: the border is not known yet when the rules run.
        Launch(desk, "[float; size 100 100; move -10 0] kitty", pid: Pid(2));
        ExpectProcess(desk.Spawn("kitty"), Pid(2));
        Assert.Equal("0,0", desk.ActivePlace().At);
        desk.CloseActive();
    }

    [Fact]
    public void SpecialWorkspacePinnedWindowUnmapFallback()
    {
        var desk = new TestDesktop();
        CloseAll(desk);
        Assert.Empty(desk.Clients());

        desk.Ok("workspace", "1");
        desk.Spawn("kitty_normal");

        desk.Spawn("kitty_pinned");
        desk.Ok("togglefloating", "class:kitty_pinned");
        desk.Ok("pin", "enable class:kitty_pinned");

        desk.Ok("togglespecialworkspace", "fallback");

        desk.Spawn("kitty_sp_normal");
        Assert.Equal(3, desk.Clients().Length);

        desk.Ok("focuswindow", "class:kitty_pinned");
        desk.CloseActive();
        Assert.Equal(2, desk.Clients().Length);

        Assert.Equal("special:fallback", WorkspaceName(desk.ActiveWindow()));

        desk.Ok("togglespecialworkspace", "fallback");
        CloseAll(desk);
    }

    [Fact]
    public void WindowRuleBorderColors()
    {
        var desk = new TestDesktop("""
            general:col.active_border = rgba(00ff00ff)
            general:col.inactive_border = rgba(ff0000ff)
            windowrule = border_color rgba(ffff00ff) rgba(000000ff), class:border_color_kitty
            """);

        // Both colors.
        desk.Spawn("border_color_kitty");
        desk.Ok("focuswindow", "class:border_color_kitty");
        Assert.Contains("ffffff00", desk.Send("getprop active active_border_color"), StringComparison.Ordinal);
        Assert.Contains("ff000000", desk.Send("getprop active inactive_border_color"), StringComparison.Ordinal);

        CloseAll(desk);

        // Both colors, a later rule.
        desk.Keyword("windowrule", "border_color rgba(ffff00ff) rgba(0000ffff), class:border_color_kitty");
        desk.Spawn("border_color_kitty");
        desk.Ok("focuswindow", "class:border_color_kitty");
        ExpectColor(desk.Send("getprop active active_border_color"), "ffffff00");
        ExpectColor(desk.Send("getprop active inactive_border_color"), "ff0000ff");
    }

    [Fact]
    public void FloatingMoveExpression()
    {
        var desk = new TestDesktop("""
            windowrule = float, class:kitty_floatingmove
            windowrule = size 800 600, class:kitty_floatingmove
            windowrule = move (monitor_w-window_w-10) (monitor_h-window_h-10), class:kitty_floatingmove
            """);

        desk.Spawn("kitty_floatingmove");
        Assert.Single(desk.Clients());

        // The active monitor is the first: 1920x1080 at 0,0.
        var expected = $"{TestDesktop.First.X + TestDesktop.First.Width - 800 - 10},{TestDesktop.First.Y + TestDesktop.First.Height - 600 - 10}";

        Assert.True(TestDesktop.Floating(desk.ActiveWindow()));
        desk.ExpectActive(expected, "800,600");

        CloseAll(desk);
    }

    private static string Address(JsonElement client) => client.GetProperty("address").GetString() ?? "";

    private static string WorkspaceName(JsonElement client) =>
        client.GetProperty("workspace").GetProperty("name").GetString() ?? "";

    private static string Handler(TestDesktop desk) => desk.ActiveWindow().GetProperty("fullscreenHandler").GetString() ?? "";

    private static WindowId IdOf(JsonElement client) => new(Convert.ToInt64(Address(client)[2..], 16));

    private static (string At, string Size) Place(TestDesktop desk, WindowId window)
    {
        var bounds = desk.Placed(window);
        return ($"{bounds.X},{bounds.Y}", $"{bounds.Width},{bounds.Height}");
    }

    // The active window's class and fullscreen mode, as the reference's isActiveWindow checks them.
    private static void ExpectFocused(TestDesktop desk, string windowClass, int fullscreen)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(windowClass, TestDesktop.ClassOf(active));
        Assert.Equal(fullscreen, TestDesktop.Fullscreen(active));
    }

    // The window asks for the foreground by itself, as kitty does when the reference removes its file.
    // The test plugin activates with a fresh serial: on Windows, a window taking the foreground right after a click in
    // the focused Client of another program.
    private static void Activate(TestDesktop desk, WindowId window) =>
        desk.Handle(new WindowFocused(window, FocusCause.User) { Clicked = desk.ActiveId() });

    // The test plugin's click with a button held down, and with it let go, where the pointer is.
    private static void Press(TestDesktop desk, string button) =>
        desk.Handle(new PointerButtonPressed(button) { Position = desk.Pointer });

    private static void Release(TestDesktop desk, string button) =>
        desk.Handle(new PointerButtonReleased(button) { Position = desk.Pointer });

    private static void CloseAll(TestDesktop desk)
    {
        foreach (var client in desk.Clients())
            desk.Handle(new WindowClosed(IdOf(client)));
    }

    // The process of the nth window a TestDesktop opens.
    private static int Pid(int window) => 1000 + window;

    // Checks that the window is the one the launch counted on, so its process is the one the launch recorded.
    private static void ExpectProcess(WindowId window, int pid) => Assert.Equal(pid, 1000 + window.Value);

    // A launched shell starts the process of the nth window, as the adapter notes a new process and its parent.
    private static void Started(TestDesktop desk, int window, int parent) => desk.WindowManager.NoteLineage(Pid(window), [parent]);

    // Runs exec and records the process it starts, as the adapter does.
    private static void Launch(TestDesktop desk, string arguments, int pid)
    {
        var outcome = desk.Handle(new Dispatch("exec", arguments));
        Assert.Null(outcome.Error);
        foreach (var launch in outcome.Launches)
            desk.WindowManager.NoteLaunched(pid, launch);
    }

    private static int MonitorId(TestDesktop desk, string name)
    {
        using var document = JsonDocument.Parse(desk.Send("j/monitors"));
        return document.RootElement.EnumerateArray()
            .Single(monitor => monitor.GetProperty("name").GetString() == name)
            .GetProperty("id").GetInt32();
    }

    // Reloads, unbinds the button and puts the window back, as the reference's RESET_WINDOW does.
    private static void ResetWindow(TestDesktop desk)
    {
        Assert.Equal("ok", desk.Send("reload"));
        Assert.Equal("ok", desk.Send("keyword unbind , mouse:273"));
        desk.Place("kitty", 0, 0, 640, 400);
        desk.MovePointer(640, 400);
        Assert.Equal(("2,2", "640,400"), desk.PlaceOf("kitty"));
    }

    private static void ExpectNear(string size, int width, int height, int delta)
    {
        var parts = size.Split(',');
        Assert.InRange(int.Parse(parts[0], CultureInfo.InvariantCulture), width - delta, width + delta);
        Assert.InRange(int.Parse(parts[1], CultureInfo.InvariantCulture), height - delta, height + delta);
    }

    private static void ExpectSideBySide(TestDesktop desk, WindowId first, WindowId second)
    {
        var places = new[] { Place(desk, first), Place(desk, second) };
        Assert.All(places, place => Assert.Equal("931,1036", place.Size));
        Assert.Contains(places, place => place.At == "22,22");
        Assert.Contains(places, place => place.At == "967,22");
    }

    private static void ExpectContentProps(TestDesktop desk)
    {
        Assert.Contains("10", desk.Send("getprop active border_size"), StringComparison.Ordinal);
        Assert.Contains("0.5", desk.Send("getprop active opacity"), StringComparison.Ordinal);
    }

    private static void ExpectColor(string reply, params string[] parts)
    {
        foreach (var part in parts)
            Assert.Contains(part, reply, StringComparison.Ordinal);
    }
}
