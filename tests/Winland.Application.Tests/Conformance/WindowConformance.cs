using System.Globalization;
using System.Text.Json;

namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's window cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class WindowConformance
{
    [Fact]
    public void SwapWindow()
    {
        var desk = new Desk();
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
        var desk = new Desk();
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");

        desk.Keyword("misc:focus_on_activate", "true");

        // Unfullscreen on conflict.
        desk.Keyword("misc:on_focus_under_fullscreen", "2");
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

        // Take over on conflict.
        desk.Keyword("misc:on_focus_under_fullscreen", "1");
        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("fullscreen", "0 set");
        ExpectFocused(desk, "kitty_A", 2);

        desk.Ok("focuswindow", "class:kitty_A");
        ExpectFocused(desk, "kitty_A", 2);

        desk.Ok("focuswindow", "class:kitty_B");
        ExpectFocused(desk, "kitty_B", 2);
        desk.Ok("fullscreen", "0 unset");

        activating = desk.Spawn("kitty_activating");
        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("fullscreen", "0 set");
        ExpectFocused(desk, "kitty_A", 2);
        Activate(desk, activating);
        ExpectFocused(desk, "kitty_activating", 2);
        desk.CloseActive();
        Assert.Equal(2, desk.Clients().Length);

        // Keep the old focus on conflict.
        desk.Keyword("misc:on_focus_under_fullscreen", "0");
        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("fullscreen", "0 set");
        ExpectFocused(desk, "kitty_A", 2);

        desk.Ok("focuswindow", "class:kitty_A");
        ExpectFocused(desk, "kitty_A", 2);

        activating = desk.Spawn("kitty_activating");
        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("fullscreen", "0 set");
        ExpectFocused(desk, "kitty_A", 2);
        Activate(desk, activating);
        ExpectFocused(desk, "kitty_A", 2);
    }

    [Fact]
    public void FocusFloatingOrTilingUnderFullscreen()
    {
        var desk = new Desk("""
            misc:focus_on_activate = true
            misc:on_focus_under_fullscreen = 2
            """);
        desk.Spawn("kitty_a");
        desk.Spawn("kitty_b");

        ExpectFocused(desk, "kitty_b", 0);
        desk.Ok("setfloating");

        // A floating window over a fullscreen tile.
        desk.Ok("focuswindow", "class:kitty_a");
        desk.Ok("fullscreen", "0 set");
        ExpectFocused(desk, "kitty_a", 2);

        desk.Ok("focuswindow", "floating");
        Assert.Contains("kitty_b", desk.ActiveClass(), StringComparison.Ordinal);
        Assert.Equal(2, Desk.Fullscreen(desk.Client("kitty_a")));
        Assert.Equal(2, Desk.FullscreenClient(desk.Client("kitty_a")));

        // A tile under a fullscreen floating window.
        desk.Ok("fullscreen", "0 set");
        desk.Ok("focuswindow", "tiled");
        Assert.Contains("kitty_a", desk.ActiveClass(), StringComparison.Ordinal);
        Assert.Equal(0, Desk.Fullscreen(desk.Client("kitty_b")));
    }

    [Fact]
    public void WindowMaximizeSize()
    {
        var desk = new Desk();
        desk.Spawn("kitty_A");

        Assert.Equal(("22,22", "1876,1036"), desk.PlaceOf("kitty_A"));
        Assert.Equal(0, Desk.Fullscreen(desk.Client("kitty_A")));

        desk.Ok("fullscreen", "1");

        Assert.Equal(("22,22", "1876,1036"), desk.PlaceOf("kitty_A"));
        Assert.Equal(1, Desk.Fullscreen(desk.Client("kitty_A")));
    }

    [Fact]
    public void GroupFallbackFocus()
    {
        var desk = new Desk();
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
    public void BringActiveToTopMouseMovement()
    {
        var desk = new Desk("""
            input:follow_mouse = 2
            input:float_switch_override_focus = 0
            """);

        desk.Spawn("a");
        desk.Ok("setfloating");
        desk.Ok("movewindowpixel", "exact 500 300,activewindow");
        desk.Ok("resizewindowpixel", "exact 400 400,activewindow");

        desk.Spawn("b");
        desk.Ok("setfloating");
        desk.Ok("movewindowpixel", "exact 500 300,activewindow");
        desk.Ok("resizewindowpixel", "exact 400 400,activewindow");

        Assert.Equal("b", TopWindow(desk));
        desk.Ok("movecursor", "700 500");

        desk.Ok("focuswindow", "class:a");
        Assert.Equal("a", desk.ActiveClass());

        desk.Ok("bringactivetotop");
        Assert.Equal("a", TopWindow(desk));

        desk.Click("mouse:272");

        Assert.Equal("a", TopWindow(desk));
    }

    [Fact]
    public void WindowRuleFocusOnActivate()
    {
        var desk = new Desk();
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
        var desk = new Desk();
        desk.Dispatch("workspace", "1337");
        desk.Spawn("kitty");

        desk.Ok("setfloating", "class:kitty");
        desk.Ok("pin", "class:kitty");

        var active = desk.ActiveWindow();
        Assert.Equal(1337, Desk.WorkspaceId(active));
        Assert.True(Desk.Pinned(active));

        desk.Dispatch("workspace", "1338");

        active = desk.ActiveWindow();
        Assert.Equal(1338, Desk.WorkspaceId(active));
        Assert.True(Desk.Pinned(active));

        desk.Ok("settiled", "class:kitty");

        active = desk.ActiveWindow();
        Assert.Equal(1338, Desk.WorkspaceId(active));
        Assert.False(Desk.Pinned(active));
    }

    [Fact]
    public void WindowruleWorkspaceEmpty()
    {
        var desk = new Desk("""
            windowrule = workspace empty, class:kitty_A
            windowrule = workspace emptyn, class:kitty_B
            """);

        desk.Dispatch("workspace", "3");
        desk.Spawn("kitty");
        Assert.Equal(3, Desk.WorkspaceId(desk.ActiveWindow()));

        desk.Spawn("kitty_A");
        Assert.Equal(1, Desk.WorkspaceId(desk.ActiveWindow()));

        desk.Dispatch("workspace", "3");
        desk.Spawn("kitty_B");
        Assert.Equal(4, Desk.WorkspaceId(desk.ActiveWindow()));
    }

    [Fact]
    public void Issue14038()
    {
        var desk = new Desk();
        desk.Spawn("kitty_14038");

        desk.Ok("movetoworkspacesilent", "special:a,class:kitty_14038");
        desk.Ok("togglefloating", "class:kitty_14038");
        desk.Ok("pin", "class:kitty_14038");
        desk.Ok("togglefloating", "class:kitty_14038");
    }

    [Fact]
    public void Issue14134()
    {
        var desk = new Desk();
        desk.AddMonitor("HEADLESS-4", Desk.Second);
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
    public void SpecialFloatRecenters()
    {
        var desk = new Desk();
        desk.Spawn("kitty_special_float_recenter");

        desk.Ok("setfloating", "class:kitty_special_float_recenter");
        desk.Ok("resizewindowpixel", "exact 10 10,class:kitty_special_float_recenter");
        desk.Ok("movetoworkspacesilent", "special:recenter,class:kitty_special_float_recenter");
        desk.Ok("movewindowpixel", "exact 50000 50000,class:kitty_special_float_recenter");

        desk.Ok("togglespecialworkspace", "recenter");
        desk.Ok("focuswindow", "class:kitty_special_float_recenter");

        Assert.Equal("kitty_special_float_recenter", desk.ActiveClass());
        desk.ExpectActive("955,535", "10,10");

        desk.Ok("togglespecialworkspace", "recenter");
        CloseAll(desk);
        Assert.Empty(desk.Clients());
    }

    [Fact]
    public void ExactWindowSelectors()
    {
        var desk = new Desk();
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
        var desk = new Desk();
        foreach (var name in new[] { "a", "b", "c", "d" })
            desk.Spawn(name);

        desk.Ok("setfloating", "class:a");
        desk.Ok("setfloating", "class:c");
        desk.Ok("setfloating", "class:d");

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
        var desk = new Desk();
        foreach (var name in new[] { "a", "b", "c", "d" })
            desk.Spawn(name);

        desk.Ok("setfloating", "class:b");

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
        var desk = new Desk("windowrule = workspace 2, class:kitty_exec_override\n");

        Launch(desk, "[workspace 3] kitty --class kitty_exec_override", pid: Pid(1));
        ExpectProcess(desk.Spawn("kitty_exec_override"), Pid(1));
        Assert.Single(desk.Clients());

        var active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_override", Desk.ClassOf(active));
        Assert.Equal(3, Desk.WorkspaceId(active));

        CloseAll(desk);
    }

    [Fact]
    public void ExecRulesTagMutation()
    {
        var desk = new Desk("""
            windowrule = workspace 2, class:kitty_tag_mutate
            windowrule = tag test_tag, class:kitty_tag_mutate
            """);

        Launch(desk, "[workspace 3] kitty --class kitty_tag_mutate", pid: Pid(1));
        ExpectProcess(desk.Spawn("kitty_tag_mutate"), Pid(1));
        Assert.Single(desk.Clients());

        var active = desk.ActiveWindow();
        Assert.Equal("kitty_tag_mutate", Desk.ClassOf(active));
        Assert.Equal(3, Desk.WorkspaceId(active));

        CloseAll(desk);
    }

    [Fact]
    public void ExecRulesDescendantProcess()
    {
        var desk = new Desk();

        // The shell is the launched process, and kitty is its child.
        Launch(desk, "[workspace 2 silent] kitty --class kitty_exec_workspace & wait", pid: 4001);
        Started(desk, window: 1, parent: 4001);
        ExpectProcess(desk.Spawn("kitty_exec_workspace"), Pid(1));
        Assert.Single(desk.Clients());

        Assert.Contains("false", desk.Send("getprop class:kitty_exec_workspace no_focus"), StringComparison.Ordinal);
        desk.Ok("focuswindow", "class:kitty_exec_workspace");

        var active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_workspace", Desk.ClassOf(active));
        Assert.Equal(2, Desk.WorkspaceId(active));

        // The options in the rule block follow the command's own rules.
        Launch(desk, "[workspace 3; border_size 7; workspace special:exec_rules silent; no_initial_focus; no_focus] kitty --class kitty_exec_special & wait", pid: 4101);
        Started(desk, window: 2, parent: 4101);
        ExpectProcess(desk.Spawn("kitty_exec_special"), Pid(2));
        Assert.Equal(2, desk.Clients().Length);

        Assert.Contains("true", desk.Send("getprop class:kitty_exec_special no_focus"), StringComparison.Ordinal);
        Assert.Contains("7", desk.Send("getprop class:kitty_exec_special border_size"), StringComparison.Ordinal);

        active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_workspace", Desk.ClassOf(active));
        Assert.Equal(2, Desk.WorkspaceId(active));

        Assert.Equal("ok", desk.Send("setprop class:kitty_exec_special no_focus false"));
        desk.Ok("focuswindow", "class:kitty_exec_special");

        active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_special", Desk.ClassOf(active));
        Assert.Equal("special:exec_rules", WorkspaceName(active));

        CloseAll(desk);
    }

    [Fact]
    public void ExecRulesPendingIsolation()
    {
        var desk = new Desk();

        // Both launches are pending before either window opens.
        Launch(desk, "[workspace 2 silent] sleep 1 && kitty --class kitty_exec_first", pid: 5001);
        Launch(desk, "[workspace special:exec_rules_pending silent; no_initial_focus; no_focus] sleep 2 && kitty --class kitty_exec_second", pid: 5101);

        Started(desk, window: 1, parent: 5001);
        ExpectProcess(desk.Spawn("kitty_exec_first"), Pid(1));
        Assert.Single(desk.Clients());

        Assert.Contains("false", desk.Send("getprop class:kitty_exec_first no_focus"), StringComparison.Ordinal);
        desk.Ok("focuswindow", "class:kitty_exec_first");

        var active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_first", Desk.ClassOf(active));
        Assert.Equal(2, Desk.WorkspaceId(active));

        Started(desk, window: 2, parent: 5101);
        ExpectProcess(desk.Spawn("kitty_exec_second"), Pid(2));
        Assert.Equal(2, desk.Clients().Length);

        Assert.Contains("false", desk.Send("getprop class:kitty_exec_first no_focus"), StringComparison.Ordinal);
        Assert.Contains("true", desk.Send("getprop class:kitty_exec_second no_focus"), StringComparison.Ordinal);

        Assert.Equal("ok", desk.Send("setprop class:kitty_exec_second no_focus false"));
        desk.Ok("focuswindow", "class:kitty_exec_second");

        active = desk.ActiveWindow();
        Assert.Equal("kitty_exec_second", Desk.ClassOf(active));
        Assert.Equal("special:exec_rules_pending", WorkspaceName(active));

        CloseAll(desk);
    }

    [Fact]
    public void PinnedRetainsPositionOnWorkspaceChange()
    {
        var desk = new Desk();
        desk.Spawn("a");

        desk.Ok("workspace", "1");
        desk.Ok("setfloating");
        desk.Ok("pin");
        Assert.Contains(desk.Clients(), Desk.Pinned);

        desk.Ok("movewindowpixel", "exact 1900 1000,activewindow");

        desk.Ok("workspace", "2");
        Assert.Contains(desk.Workspaces(), workspace => workspace.GetProperty("id").GetInt32() == 2);

        desk.Ok("workspace", "1");
        Assert.Contains(desk.Workspaces(), workspace => workspace.GetProperty("id").GetInt32() == 1);

        Assert.Contains(desk.Clients(), Desk.Pinned);
        Assert.Equal("1900,1000", desk.PlaceOf("a").At);
    }

    [Fact]
    public void Monitorrule()
    {
        var desk = new Desk("windowrule = monitor HEADLESS-3, class:monitor_kitty\n");
        desk.AddMonitor("HEADLESS-3", Desk.Second);
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
        var desk = new Desk("windowrule = float, class:kitty\n");
        desk.Spawn("kitty");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kitty");

        ResetWindow(desk);
        Assert.Equal("ok", desk.Send("r/keyword bindm , mouse:273, resizewindow"));
        Press(desk, "mouse:273");
        desk.Ok("movecursor", "700 200");
        Release(desk, "mouse:273");
        Assert.Equal("700,200", desk.PlaceOf("kitty").Size);

        ResetWindow(desk);
        Assert.Equal("ok", desk.Send("r/keyword bindm , mouse:273, resizewindow 1"));
        Press(desk, "mouse:273");
        desk.Ok("movecursor", "1280 100");
        Release(desk, "mouse:273");
        Assert.Equal("1280,800", desk.PlaceOf("kitty").Size);
    }

    [Fact]
    public void GetFullscreenHandler()
    {
        var desk = new Desk();
        Assert.Equal("ok", desk.Send("r/keyword general:layout dwindle"));

        desk.Spawn("cat");

        desk.Ok("fullscreen", "0");
        Assert.Equal("default", Handler(desk));
        desk.Ok("fullscreen", "0");

        desk.Ok("setfloating");

        desk.Ok("fullscreen", "0");
        Assert.Equal("default", Handler(desk));
        desk.Ok("fullscreen", "0");

        Assert.Equal("ok", desk.Send("r/keyword general:layout scrolling"));

        desk.Ok("fullscreen", "0");
        Assert.Equal("default", Handler(desk));
        desk.Ok("fullscreen", "0");

        desk.Ok("settiled");

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
        var desk = new Desk();
        desk.Spawn("a");
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", "exact 100 100,activewindow");

        // No constraints.
        desk.Keyword("misc:float_force_onscreen", "0");
        desk.Ok("movewindowpixel", "exact -110 0,activewindow");
        Assert.Equal("-110,0", desk.ActivePlace().At);

        // Partly on screen.
        desk.Keyword("misc:float_force_onscreen", "1");
        desk.Ok("movewindowpixel", "exact -110 0,activewindow");
        Assert.Equal("-50,0", desk.ActivePlace().At);

        // Fully on screen.
        desk.Keyword("misc:float_force_onscreen", "2");
        desk.Ok("movewindowpixel", "exact -10 0,activewindow");
        Assert.Equal("2,2", desk.ActivePlace().At);

        desk.CloseActive();

        // New floating windows open where their rules put them, whatever float_force_onscreen says.
        desk.Keyword("misc:new_float_force_onscreen", "0");
        Launch(desk, "[float; size 100 100; move -110 0] kitty", pid: Pid(2));
        ExpectProcess(desk.Spawn("kitty"), Pid(2));
        Assert.Equal("-110,0", desk.ActivePlace().At);
        desk.CloseActive();

        desk.Keyword("misc:new_float_force_onscreen", "1");
        Launch(desk, "[float; size 100 100; move -110 0] kitty", pid: Pid(3));
        ExpectProcess(desk.Spawn("kitty"), Pid(3));
        Assert.Equal("-50,0", desk.ActivePlace().At);
        desk.CloseActive();

        // The reference places it at 0,0 rather than 2,2: the border is not known yet when the rules run.
        desk.Keyword("misc:new_float_force_onscreen", "2");
        Launch(desk, "[float; size 100 100; move -10 0] kitty", pid: Pid(4));
        ExpectProcess(desk.Spawn("kitty"), Pid(4));
        Assert.Equal("0,0", desk.ActivePlace().At);
        desk.CloseActive();
    }

    [Fact]
    public void SpecialWorkspacePinnedWindowUnmapFallback()
    {
        var desk = new Desk();
        CloseAll(desk);
        Assert.Empty(desk.Clients());

        desk.Ok("workspace", "1");
        desk.Spawn("kitty_normal");

        desk.Spawn("kitty_pinned");
        desk.Ok("setfloating", "class:kitty_pinned");
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
        var desk = new Desk("""
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
        var desk = new Desk("""
            windowrule = float, class:kitty_floatingmove
            windowrule = size 800 600, class:kitty_floatingmove
            windowrule = move (monitor_w-window_w-10) (monitor_h-window_h-10), class:kitty_floatingmove
            """);

        desk.Spawn("kitty_floatingmove");
        Assert.Single(desk.Clients());

        // The active monitor is the first: 1920x1080 at 0,0.
        var expected = $"{Desk.First.X + Desk.First.Width - 800 - 10},{Desk.First.Y + Desk.First.Height - 600 - 10}";

        Assert.True(Desk.Floating(desk.ActiveWindow()));
        desk.ExpectActive(expected, "800,600");

        CloseAll(desk);
    }

    private static string Address(JsonElement client) => client.GetProperty("address").GetString() ?? "";

    private static string WorkspaceName(JsonElement client) =>
        client.GetProperty("workspace").GetProperty("name").GetString() ?? "";

    private static string Handler(Desk desk) => desk.ActiveWindow().GetProperty("fullscreenHandler").GetString() ?? "";

    private static WindowId IdOf(JsonElement client) => new(Convert.ToInt64(Address(client)[2..], 16));

    private static (string At, string Size) Place(Desk desk, WindowId window)
    {
        var bounds = desk.Placed(window);
        return ($"{bounds.X},{bounds.Y}", $"{bounds.Width},{bounds.Height}");
    }

    // The active window's class and fullscreen mode, as the reference's isActiveWindow checks them.
    private static void ExpectFocused(Desk desk, string windowClass, int fullscreen)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(windowClass, Desk.ClassOf(active));
        Assert.Equal(fullscreen, Desk.Fullscreen(active));
    }

    // The window asks for the foreground by itself, as kitty does when the reference removes its file.
    // The test plugin activates with a fresh serial: on Windows, a window taking the foreground right after a click in
    // the focused Client of another program.
    private static void Activate(Desk desk, WindowId window) =>
        desk.Handle(new WindowFocused(window, FocusCause.User) { Clicked = desk.ActiveId() });

    // The test plugin's click with a button held down, and with it let go, where the pointer is.
    private static void Press(Desk desk, string button) =>
        desk.Handle(new PointerButtonPressed(button) { Position = desk.Pointer });

    private static void Release(Desk desk, string button) =>
        desk.Handle(new PointerButtonReleased(button) { Position = desk.Pointer });

    private static void CloseAll(Desk desk)
    {
        foreach (var client in desk.Clients())
            desk.Handle(new WindowClosed(IdOf(client)));
    }

    // The process of the nth window a Desk opens.
    private static int Pid(int window) => 1000 + window;

    // Checks that the window is the one the launch counted on, so its process is the one the launch recorded.
    private static void ExpectProcess(WindowId window, int pid) => Assert.Equal(pid, 1000 + window.Value);

    // A launched shell starts the process of the nth window, as the adapter notes a new process and its parent.
    private static void Started(Desk desk, int window, int parent) => desk.WindowManager.NoteProcess(Pid(window), parent);

    // Runs exec and records the process it starts, as the adapter does.
    private static void Launch(Desk desk, string arguments, int pid)
    {
        var outcome = desk.Handle(new Dispatch("exec", arguments));
        Assert.Null(outcome.Error);
        foreach (var launch in outcome.Launches)
            desk.WindowManager.NoteLaunched(pid, launch);
    }

    private static int MonitorId(Desk desk, string name)
    {
        using var document = JsonDocument.Parse(desk.Send("j/monitors"));
        return document.RootElement.EnumerateArray()
            .Single(monitor => monitor.GetProperty("name").GetString() == name)
            .GetProperty("id").GetInt32();
    }

    // The reference reads the clients list from the bottom of the stack up, so the later of a and b is on top.
    private static string TopWindow(Desk desk)
    {
        var classes = desk.Clients().Select(Desk.ClassOf).ToList();
        return classes.LastIndexOf("a") > classes.LastIndexOf("b") ? "a" : "b";
    }

    // Reloads, unbinds the button and puts the window back, as the reference's RESET_WINDOW does.
    private static void ResetWindow(Desk desk)
    {
        Assert.Equal("ok", desk.Send("r/reload"));
        Assert.Equal("ok", desk.Send("r/keyword unbind , mouse:273"));
        desk.Ok("resizewindowpixel", "exact 640 400,class:kitty");
        desk.Ok("movewindowpixel", "exact 0 0,class:kitty");
        desk.Ok("movecursor", "640 400");
        Assert.Equal(("0,0", "640,400"), desk.PlaceOf("kitty"));
    }

    private static void ExpectNear(string size, int width, int height, int delta)
    {
        var parts = size.Split(',');
        Assert.InRange(int.Parse(parts[0], CultureInfo.InvariantCulture), width - delta, width + delta);
        Assert.InRange(int.Parse(parts[1], CultureInfo.InvariantCulture), height - delta, height + delta);
    }

    private static void ExpectSideBySide(Desk desk, WindowId first, WindowId second)
    {
        var places = new[] { Place(desk, first), Place(desk, second) };
        Assert.All(places, place => Assert.Equal("931,1036", place.Size));
        Assert.Contains(places, place => place.At == "22,22");
        Assert.Contains(places, place => place.At == "967,22");
    }

    private static void ExpectContentProps(Desk desk)
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
