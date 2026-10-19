using System.Globalization;
using System.Text.Json;

namespace Winland.Tests.Conformance;

/// <summary>The pinned release's workspace cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class WorkspacesConformance
{
    [Fact]
    public void WorkspacesCombined()
    {
        var desk = new Desk("workspace = 966, persistent:true\n");
        ReloadsReferenceConfig(desk);

        Assert.Contains("966 (966)", Labels(desk));
        Assert.Equal("ok", desk.Send("reload"));

        desk.Ok("workspace", "1");
        desk.Spawn("a");
        desk.Ok("workspace", "3");
        desk.Spawn("b");
        desk.Ok("workspace", "1");

        desk.Ok("workspace", "+1");
        Assert.Equal("2 (2)", ActiveLabel(desk));
        Assert.Contains("3 (3)", Labels(desk));
        Assert.Contains("1 (1)", Labels(desk));

        desk.Ok("workspace", "1");
        Assert.DoesNotContain("2 (2)", Labels(desk));

        desk.Ok("workspace", "m+1");
        Assert.Equal("3 (3)", ActiveLabel(desk));

        desk.Ok("workspace", "-1");
        Assert.Equal("2 (2)", ActiveLabel(desk));

        desk.Ok("workspace", "1");
        desk.Ok("workspace", "r+1");
        Assert.Equal("2 (2)", ActiveLabel(desk));

        desk.Ok("workspace", "r+1");
        Assert.Equal("3 (3)", ActiveLabel(desk));

        desk.Ok("workspace", "r~1");
        Assert.Equal("1 (1)", ActiveLabel(desk));

        desk.Ok("workspace", "empty");
        Assert.Equal("2 (2)", ActiveLabel(desk));

        desk.Ok("workspace", "previous");
        Assert.Equal("1 (1)", ActiveLabel(desk));

        desk.Ok("workspace", "name:TEST_WORKSPACE_NULL");
        Assert.Equal("TEST_WORKSPACE_NULL (TEST_WORKSPACE_NULL)", ActiveLabel(desk));

        desk.Ok("workspace", "1");

        // The new monitor takes workspace 2.
        desk.AddMonitor("HEADLESS-3", Desk.Second);
        var monitors = Monitors(desk);
        Assert.Contains("2 (2)", monitors.Select(ShownOn));
        Assert.Contains("1 (1)", monitors.Select(ShownOn));
        Assert.Contains("HEADLESS-3", monitors.Select(NameOf));

        desk.Ok("focusmonitor", "HEADLESS-2");
        Assert.Equal("1 (1)", ActiveLabel(desk));

        desk.Ok("workspace", "r+1");
        Assert.Equal("3 (3)", ActiveLabel(desk));

        desk.Ok("workspace", "1");
        desk.Ok("workspace", "r~2");
        Assert.Equal("3 (3)", ActiveLabel(desk));

        desk.Ok("workspace", "m+1");
        Assert.Equal("1 (1)", ActiveLabel(desk));

        // The reference expects an error here and does not check the reply.
        desk.Dispatch("workspace", "1");
        Assert.Equal("1 (1)", ActiveLabel(desk));

        desk.Keyword("binds:workspace_back_and_forth", "true");
        desk.Ok("workspace", "1");
        Assert.Equal("3 (3)", ActiveLabel(desk));
        desk.Keyword("binds:workspace_back_and_forth", "false");

        desk.Keyword("binds:hide_special_on_workspace_change", "true");
        desk.Ok("workspace", "special:HELLO");
        Assert.Contains("special:HELLO", Monitors(desk).Select(SpecialOn));

        // The reference does not check this reply either.
        desk.Dispatch("workspace", "3");
        Assert.Equal(2, Monitors(desk).Count(monitor => SpecialOn(monitor).Length == 0));
        desk.Keyword("binds:hide_special_on_workspace_change", "false");

        desk.Keyword("binds:allow_workspace_cycles", "true");
        desk.Ok("workspace", "1");
        desk.Ok("workspace", "3");
        desk.Ok("workspace", "1");

        desk.Ok("workspace", "previous");
        Assert.Equal("3 (3)", ActiveLabel(desk));

        desk.Ok("workspace", "previous");
        Assert.Equal("1 (1)", ActiveLabel(desk));

        desk.Ok("workspace", "previous");
        Assert.Equal("3 (3)", ActiveLabel(desk));

        desk.Keyword("binds:allow_workspace_cycles", "false");
        desk.Ok("workspace", "1");
        KillAll(desk);

        desk.Keyword("dwindle:force_split", "2");
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");
        desk.Spawn("kitty_C");
        desk.Keyword("dwindle:force_split", "0");

        desk.Ok("focuswindow", "class:kitty_B");
        desk.Ok("resizeactive", "20 20");

        desk.Keyword("binds:focus_preferred_method", "0");
        desk.Ok("focuswindow", "class:kitty_C");
        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("movefocus", "r");
        Assert.Equal("kitty_C", desk.ActiveClass());

        desk.Ok("focuswindow", "class:kitty_A");
        desk.Keyword("binds:focus_preferred_method", "1");
        desk.Ok("movefocus", "r");
        Assert.Equal("kitty_B", desk.ActiveClass());

        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("focusmonitor", "HEADLESS-3");
        desk.Spawn("kitty_D");
        Assert.Equal("kitty_D", desk.ActiveClass());

        desk.Ok("focusmonitor", "l");
        Assert.Equal("kitty_A", desk.ActiveClass());

        desk.Keyword("binds:movefocus_cycles_fullscreen", "false");
        desk.Ok("fullscreen", "0");
        desk.Ok("movefocus", "r");
        Assert.Equal("kitty_D", desk.ActiveClass());

        desk.Ok("focusmonitor", "l");
        Assert.Equal("kitty_A", desk.ActiveClass());

        desk.Keyword("binds:movefocus_cycles_fullscreen", "true");
        desk.Ok("movefocus", "r");
        Assert.Equal("kitty_B", desk.ActiveClass());

        KillAll(desk);

        MultimonBaf(desk);
        MultimonFocus(desk);
        WorkspaceHistoryMultiMon(desk);

        desk.RemoveMonitor("HEADLESS-3");

        SpecialWorkspaceFullscreen(desk);
        AsymmetricGaps(desk);
        DynamicWsEffects(desk);

        Assert.Empty(desk.Clients());
    }

    [Fact]
    public void WorkspacesFollowProperNoGaps()
    {
        var desk = new Desk("""
            workspace = w[tv1], gapsout:0, gapsin:0
            workspace = f[1], gapsout:0, gapsin:0
            windowrulev2 = border_size 0, float:0, workspace:w[tv1]
            windowrulev2 = rounding 0, float:0, workspace:w[tv1]
            windowrulev2 = border_size 0, float:0, workspace:f[1]
            windowrulev2 = rounding 0, float:0, workspace:f[1]
            """);
        desk.Ok("workspace", "100");

        desk.Spawn("a");
        Assert.Equal("1920,1080", desk.ActivePlace().Size);

        desk.Spawn("a");
        desk.Ok("movetoworkspace", "101");
        Assert.Equal("1920,1080", desk.ActivePlace().Size);
        Assert.Equal("101 (101)", ActiveLabel(desk));
    }

    [Fact]
    public void WorkspacesSmartGapsDirectionalMoveAcrossMonitors()
    {
        var desk = new Desk("""
            binds:window_direction_monitor_fallback = true
            workspace = w[tv1], gapsout:0, gapsin:0
            windowrulev2 = border_size 0, float:0, workspace:w[tv1]
            windowrulev2 = rounding 0, float:0, workspace:w[tv1]
            """);
        desk.AddMonitor("HSG-L", new Rectangle(20000, 0, 1920, 1080));
        desk.AddMonitor("HSG-R", new Rectangle(21920, 0, 1920, 1080));

        desk.Ok("focusmonitor", "HSG-L");
        desk.Ok("workspace", "230");
        desk.Spawn("smart_gaps_src_a");
        desk.Spawn("smart_gaps_src_b");
        Assert.Equal("smart_gaps_src_b", desk.ActiveClass());
        Assert.Equal("931,1036", desk.ActivePlace().Size);

        desk.Ok("focusmonitor", "HSG-R");
        desk.Ok("workspace", "231");
        desk.Spawn("smart_gaps_dst");
        Assert.Equal("smart_gaps_dst", desk.ActiveClass());
        Assert.Equal("1920,1080", desk.ActivePlace().Size);

        desk.Ok("focuswindow", "class:smart_gaps_src_b");
        desk.Ok("movewindow", "r");
        Assert.Equal("smart_gaps_src_b", desk.ActiveClass());
        Assert.Equal("931,1036", desk.ActivePlace().Size);

        desk.Ok("focusmonitor", "HSG-L");
        desk.Ok("focuswindow", "class:smart_gaps_src_a");
        Assert.Equal("smart_gaps_src_a", desk.ActiveClass());
        Assert.Equal("1920,1080", desk.ActivePlace().Size);
    }

    // The reference changes a Workspace's id with its change_id dispatcher, which has no line-based name.
    // changeworkspaceid stands for it here.
    [Fact]
    public void WorkspaceRenameChangeID()
    {
        var desk = new Desk();
        desk.Ok("workspace", "150");
        desk.Spawn("a");

        desk.Ok("workspace", "name:coce");
        desk.Spawn("a");

        desk.Ok("workspace", "100");
        Assert.Equal("100 (100)", ActiveLabel(desk));

        desk.Ok("changeworkspaceid", "100 101");
        Assert.Equal("101 (101)", ActiveLabel(desk));

        foreach (var bad in new[] { "101 -1", "101 abc", "101", "102 200", "101 150", "name:coce 105" })
            Assert.NotEqual("ok", desk.Dispatch("changeworkspaceid", bad));
        Assert.Equal("101 (101)", ActiveLabel(desk));

        desk.Ok("renameworkspace", "101 vaxry_was_here");
        Assert.Equal("101 (vaxry_was_here)", ActiveLabel(desk));

        desk.Ok("changeworkspaceid", "101 102");
        Assert.Equal("102 (vaxry_was_here)", ActiveLabel(desk));
    }

    // The reference renames through its test plugin, which also counts the rename events. Here the
    // events are read from the outcome of renameworkspace.
    [Fact]
    public void WorkspaceRenameEmitsGlobalEvent()
    {
        var desk = new Desk();
        desk.Ok("workspace", "5201");
        desk.Spawn("workspace_rename_event");

        var reply = desk.Core.Execute("dispatch renameworkspace 5201 renamed_by_event_test");
        Assert.Equal("ok", reply.Text);
        var events = CommandProtocol.FormatStream(reply.Effect.Events, reply.Effect.State);
        Assert.Equal(["renameworkspace>>5201,renamed_by_event_test"], events.Where(line => line.StartsWith("renameworkspace>>", StringComparison.Ordinal)));
        Assert.Equal("5201 (renamed_by_event_test)", ActiveLabel(desk));

        desk.Close("workspace_rename_event");
        desk.Ok("workspace", "1");
    }

    [Fact]
    public void WorkspaceChangeIDUpdatesRules()
    {
        var desk = new Desk("workspace = 201, gapsout:40 40 40 40\n");
        desk.Ok("workspace", "200");
        Assert.Equal("200 (200)", ActiveLabel(desk));

        desk.Spawn("a");
        Assert.Equal("22,22", desk.ActivePlace().At);

        desk.Ok("changeworkspaceid", "200 201");
        Assert.Equal("42,42", desk.ActivePlace().At);
    }

    [Fact]
    public void WorkspaceRenameDoesNotChangeAddress()
    {
        var desk = new Desk("workspace = name:vaxry, gapsout:40 40 40 40\n");
        desk.Ok("workspace", "200");
        Assert.Equal("200 (200)", ActiveLabel(desk));

        desk.Spawn("a");
        Assert.Equal("22,22", desk.ActivePlace().At);

        desk.Ok("renameworkspace", "200 vaxry");
        Assert.Equal("22,22", desk.ActivePlace().At);
    }

    [Fact]
    public void WorkspaceRulesMatchBareAddressableNames()
    {
        var desk = new Desk("workspace = vaxry, gapsout:40 40 40 40\n");
        desk.Ok("workspace", "name:vaxry");
        Assert.Equal("vaxry (vaxry)", ActiveLabel(desk));

        desk.Spawn("a");
        Assert.Equal("42,42", desk.ActivePlace().At);

        desk.Ok("renameworkspace", "vaxry renamed");
        Assert.Equal("vaxry (renamed)", ActiveLabel(desk));
        Assert.Equal("42,42", desk.ActivePlace().At);
    }

    [Fact]
    public void WorkspacesDistinctTiledAndFloatGaps()
    {
        var desk = new Desk("""
            workspace = name:workspacesDistinctTiledAndFloatGaps, gapsout:200, float_gaps:10, border:false
            windowrulev2 = float, workspace:name:workspacesDistinctTiledAndFloatGaps, class:workspacesDistinctTiledAndFloatGaps
            """);
        desk.Ok("workspace", "name:workspacesDistinctTiledAndFloatGaps");

        desk.Spawn("kitty");
        Assert.Equal(200, X(desk));

        desk.Spawn("workspacesDistinctTiledAndFloatGaps");
        desk.Ok("movewindow", "l");
        Assert.Equal(10, X(desk));
    }

    [Fact]
    public void WorkspaceSwitchUnfocusesWindowFromOldWorkspace()
    {
        var desk = new Desk();
        desk.Spawn("a");
        for (var followMouse = 0; followMouse <= 3; followMouse++)
        {
            desk.Keyword("input:follow_mouse", followMouse.ToString(CultureInfo.InvariantCulture));

            var before = desk.Send("activewindow");
            Assert.NotEqual("Invalid", before);

            desk.Ok("workspace", "2");
            Assert.Equal("Invalid", desk.Send("activewindow"));

            desk.Ok("workspace", "previous");
            Assert.Equal(before, desk.Send("activewindow"));
        }
    }

    [Fact]
    public void LuaFocusCreatesBareNamedWorkspace()
    {
        using var desk = Desk.WithLua("");
        Assert.Equal("nil", desk.Send("repl hl.get_workspace('hi')"));
        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'hi' })");
        Assert.Equal("hi (hi)", ActiveLabel(desk));
    }

    [Fact]
    public void LuaGetWorkspace()
    {
        using var desk = Desk.WithLua("");
        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:test' })");
        Assert.Equal("test (test)", ActiveLabel(desk));
        desk.Spawn("a");

        Assert.Equal("HL.Workspace(test:test)", desk.Send("repl hl.get_workspace('name:test')"));
        Assert.Equal("HL.Workspace(test:test)", desk.Send("repl hl.get_workspace('test')"));
        Assert.Equal("true", desk.Send("repl hl.get_workspace('name:test') == hl.get_active_workspace()"));
        Assert.Equal("true", desk.Send("repl hl.get_workspace('test') == hl.get_active_workspace()"));
        Assert.Equal("nil", desk.Send("repl type(hl.get_active_workspace().id)"));

        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 1})");
        Assert.Equal("1 (1)", ActiveLabel(desk));
        Assert.Equal("1", desk.Send("repl hl.get_active_workspace().id"));
        Assert.Equal("integer", desk.Send("repl math.type(hl.get_active_workspace().id)"));

        Assert.Equal("HL.Workspace(test:test)", desk.Send("repl hl.get_workspace('e-1')"));
        Assert.Equal("nil", desk.Send("repl hl.get_workspace('r+1')"));
        Assert.Equal("nil", desk.Send("repl hl.get_workspace(42)"));
    }

    [Fact]
    public void LuaSetWorkspace()
    {
        using var desk = Desk.WithLua("");

        // A new monitor takes workspace 2.
        desk.AddMonitor("HEADLESS-3", Desk.Second);
        Assert.Contains("2 (2)", Monitors(desk).Select(ShownOn));
        Assert.Contains("1 (1)", Monitors(desk).Select(ShownOn));

        // A recognizable window on the first monitor.
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");
        desk.Spawn("ws1");
        Assert.Equal("1 (1)", WorkspaceLabelOf(desk.ActiveWindow()));

        LuaOk(desk, "eval M1 = hl.get_monitors()[1]");
        LuaOk(desk, "eval M2 = hl.get_monitors()[2]");

        // Create: four new workspaces on monitor 2.
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = M2 })");
        Assert.Equal("HEADLESS-3", ActiveMonitor(desk));
        LuaOk(desk, "eval M2:set_workspace(100)");
        desk.Spawn("ws100");
        LuaOk(desk, "eval M2:set_workspace(101)");
        desk.Spawn("ws101");
        Assert.Contains("100 (100) on HEADLESS-3", Placed(desk));
        Assert.Contains("101 (101) on HEADLESS-3", Placed(desk));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");
        Assert.Equal("HEADLESS-2", ActiveMonitor(desk));

        // Inactive to focused: monitor 1 takes workspace 100, and 101 stays on monitor 2.
        LuaOk(desk, "eval M1:set_workspace(100)");
        Assert.Equal("ws100", desk.ActiveClass());
        Assert.Equal(("100 (100)", "HEADLESS-2"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-3' })");
        Assert.Equal(("101 (101)", "HEADLESS-3"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");

        // Active to focused: monitor 1 takes 101, and monitor 2 gets a new workspace 2.
        LuaOk(desk, "eval M1:set_workspace(101)");
        Assert.Equal("ws101", desk.ActiveClass());
        Assert.Equal(("101 (101)", "HEADLESS-2"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-3' })");
        Assert.Equal(("2 (2)", "HEADLESS-3"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");

        // Inactive to unfocused: monitor 2 takes 100 from monitor 1, and the focus stays.
        LuaOk(desk, "eval M2:set_workspace(100)");
        Assert.Equal("ws101", desk.ActiveClass());
        Assert.Equal(("101 (101)", "HEADLESS-2"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-3' })");
        Assert.Equal(("100 (100)", "HEADLESS-3"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");

        // Active to unfocused: monitor 2 takes 101, which monitor 1 shows, and monitor 1 goes back to 1.
        LuaOk(desk, "eval M2:set_workspace(101)");
        Assert.Equal("ws1", desk.ActiveClass());
        Assert.Equal(("1 (1)", "HEADLESS-2"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-3' })");
        Assert.Equal(("101 (101)", "HEADLESS-3"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");

        // Unfocused relative: m-1 is read on monitor 2.
        LuaOk(desk, "eval M2:set_workspace('m-1')");
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-3' })");
        Assert.Equal("ws100", desk.ActiveClass());
        Assert.Equal(("100 (100)", "HEADLESS-3"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");

        KillAll(desk);
        desk.RemoveMonitor("HEADLESS-3");
    }

    [Fact]
    public void LuaSetSpecialWorkspace()
    {
        using var desk = Desk.WithLua("");

        // A new monitor takes workspace 2.
        desk.AddMonitor("HEADLESS-3", Desk.Second);
        Assert.Contains("2 (2)", Monitors(desk).Select(ShownOn));
        Assert.Contains("1 (1)", Monitors(desk).Select(ShownOn));

        LuaOk(desk, "eval M1 = hl.get_monitor('HEADLESS-2')");
        LuaOk(desk, "eval M2 = hl.get_monitor('HEADLESS-3')");

        // A special workspace with a window on the focused monitor.
        LuaOk(desk, "eval M1:set_special_workspace(1)");
        desk.Spawn("a");
        Assert.Equal("true", desk.Send("repl hl.get_active_window().monitor == M1"));
        Assert.Equal("special:1 (special:1)", WorkspaceLabelOf(desk.ActiveWindow()));

        // A new special workspace on the unfocused monitor.
        LuaOk(desk, "eval M2:set_special_workspace(2)");
        Assert.Contains("special:2 (special:2) on HEADLESS-3", Placed(desk));

        // The focused monitor's special workspace moves to the unfocused one.
        LuaOk(desk, "eval M2:set_special_workspace(1)");
        Assert.Equal(("1 (1)", "HEADLESS-2"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-3' })");
        Assert.Equal("true", desk.Send("repl hl.get_active_window().monitor == M2"));
        Assert.Equal("special:1 (special:1)", WorkspaceLabelOf(desk.ActiveWindow()));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");

        // Clearing the unfocused monitor's special workspace.
        LuaOk(desk, "eval M2:set_special_workspace(nil)");
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-3' })");
        Assert.Equal(("2 (2)", "HEADLESS-3"), (ActiveLabel(desk), ActiveMonitor(desk)));
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");

        KillAll(desk);
        desk.RemoveMonitor("HEADLESS-3");
    }

    private static void LuaOk(Desk desk, string line) => Assert.Equal("ok", desk.Send(line));

    private static string ActiveMonitor(Desk desk) => desk.ActiveWorkspace().GetProperty("monitor").GetString() ?? "";

    // A client's workspace, as the reference writes it: `ADDRESS (NAME)`.
    private static string WorkspaceLabelOf(JsonElement client) => Label(client.GetProperty("workspace"));

    // Each workspace and its monitor, as the reference's workspaces reply writes `workspace ID (NAME) on monitor M:`.
    private static string[] Placed(Desk desk) =>
        [.. desk.Workspaces().Select(workspace => $"{Label(workspace)} on {workspace.GetProperty("monitor").GetString()}")];

    private static void MultimonBaf(Desk desk)
    {
        desk.Keyword("binds:workspace_back_and_forth", "1");

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "1");
        desk.Spawn("a");

        desk.Ok("workspace", "2");
        desk.Spawn("a");

        desk.Ok("focusmonitor", "HEADLESS-3");
        desk.Ok("workspace", "3");
        desk.Spawn("a");

        desk.Ok("workspace", "3");
        Assert.Equal("2 (2)", ActiveLabel(desk));

        desk.Ok("workspace", "4");
        desk.Ok("workspace", "4");
        Assert.Equal("2 (2)", ActiveLabel(desk));

        desk.Ok("workspace", "2");
        Assert.Equal("4 (4)", ActiveLabel(desk));

        desk.Ok("workspace", "3");
        desk.Ok("workspace", "3");
        Assert.Equal("4 (4)", ActiveLabel(desk));

        desk.Ok("workspace", "2");
        desk.Ok("workspace", "3");
        desk.Ok("workspace", "1");
        desk.Ok("workspace", "1");
        Assert.Equal("3 (3)", ActiveLabel(desk));

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("focusworkspaceoncurrentmonitor", "1");
        desk.Ok("focusmonitor", "HEADLESS-3");
        desk.Ok("focusworkspaceoncurrentmonitor", "3");
        desk.Ok("focusworkspaceoncurrentmonitor", "4");
        desk.Ok("focusworkspaceoncurrentmonitor", "2");
        desk.Ok("focusworkspaceoncurrentmonitor", "2");
        ExpectActive(desk, "4 (4)", "HEADLESS-3");

        desk.Ok("workspace", "1");
        desk.Ok("workspace", "1");
        ExpectActive(desk, "4 (4)", "HEADLESS-3");

        desk.Keyword("binds:workspace_back_and_forth", "2");

        // There is no previous Workspace on HEADLESS-2.
        desk.Ok("workspace", "1");
        desk.Ok("workspace", "1");
        ExpectActive(desk, "1 (1)", "HEADLESS-2");

        desk.Ok("focusworkspaceoncurrentmonitor", "3");
        desk.Ok("focusworkspaceoncurrentmonitor", "3");
        ExpectActive(desk, "1 (1)", "HEADLESS-2");

        desk.Ok("workspace", "1");
        ExpectActive(desk, "3 (3)", "HEADLESS-2");

        KillAll(desk);
    }

    private static void MultimonFocus(Desk desk)
    {
        desk.Keyword("input:follow_mouse", "0");
        desk.Ok("focusmonitor", "HEADLESS-3");
        desk.Ok("workspace", "8");
        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "7");

        desk.Spawn("a");
        desk.Spawn("b");

        desk.Ok("focuswindow", "class:a");
        desk.Ok("movefocus", "r");
        Assert.Equal("7 (7)", ActiveLabel(desk));
        Assert.Equal("b", desk.ActiveClass());

        desk.Ok("movefocus", "r");
        Assert.Equal("8 (8)", ActiveLabel(desk));

        desk.Spawn("c");
        Assert.Equal("c", desk.ActiveClass());
        Assert.Equal("8 (8)", ActiveLabel(desk));

        desk.Ok("movefocus", "l");
        Assert.Equal("b", desk.ActiveClass());
        Assert.Equal("7 (7)", ActiveLabel(desk));

        desk.Ok("movewindow", "r");
        Assert.Equal("b", desk.ActiveClass());
        Assert.Equal("8 (8)", ActiveLabel(desk));

        desk.Ok("movefocus", "r");
        Assert.Equal("c", desk.ActiveClass());
        Assert.Equal("8 (8)", ActiveLabel(desk));

        desk.Ok("movefocus", "l");

        desk.Keyword("general:no_focus_fallback", "true");
        desk.Keyword("binds:window_direction_monitor_fallback", "false");

        Assert.NotEqual("ok", desk.Dispatch("movefocus", "l"));
        Assert.Equal("b", desk.ActiveClass());
        Assert.Equal("8 (8)", ActiveLabel(desk));

        desk.Ok("movewindow", "l");
        Assert.Equal("b", desk.ActiveClass());
        Assert.Equal("8 (8)", ActiveLabel(desk));

        Assert.Equal("ok", desk.Send("reload"));
        KillAll(desk);
    }

    private static void WorkspaceHistoryMultiMon(Desk desk)
    {
        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "10");
        desk.Spawn("a");
        desk.Ok("workspace", "11");
        desk.Spawn("a");

        desk.Ok("focusmonitor", "HEADLESS-3");
        desk.Ok("workspace", "12");
        desk.Spawn("a");

        desk.Ok("focusmonitor", "HEADLESS-2");
        Assert.Equal("11 (11)", ActiveLabel(desk));

        desk.Ok("workspace", "previous_per_monitor");
        Assert.Equal("10 (10)", ActiveLabel(desk));

        KillAll(desk);
    }

    private static void SpecialWorkspaceFullscreen(Desk desk)
    {
        desk.Dispatch("workspace", "1");
        Assert.Empty(desk.Clients());

        desk.Ok("workspace", "special:test_fs_special");
        desk.Spawn("kitty_special");
        Assert.Equal("kitty_special", desk.ActiveClass());
        Assert.Equal("special:test_fs_special", WorkspaceNameOf(desk.ActiveWindow()));

        desk.Ok("fullscreen", "0");
        Assert.Equal(2, Desk.Fullscreen(desk.ActiveWindow()));
        Assert.Contains("special:test_fs_special", Monitors(desk).Select(SpecialOn));

        // The special workspace is hidden before a window opens on the regular one.
        desk.Ok("togglespecialworkspace", "test_fs_special");
        desk.Dispatch("workspace", "1");
        desk.Spawn("kitty_regular");

        desk.Ok("fullscreen", "0");
        Assert.Equal("kitty_regular", desk.ActiveClass());
        Assert.Equal(2, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Ok("togglespecialworkspace", "test_fs_special");
        desk.Ok("focuswindow", "class:kitty_special");
        Assert.Equal("kitty_special", desk.ActiveClass());

        desk.Ok("togglespecialworkspace", "test_fs_special");
        desk.Ok("focuswindow", "class:kitty_regular");
        Assert.Equal("kitty_regular", desk.ActiveClass());
        Assert.Equal(2, Desk.Fullscreen(desk.ActiveWindow()));
        Assert.Contains("", Monitors(desk).Select(SpecialOn));

        KillAll(desk);
        Assert.Equal("ok", desk.Send("reload"));
    }

    private static void AsymmetricGaps(Desk desk)
    {
        Assert.Empty(desk.Clients());
        desk.Dispatch("workspace", "1");
        desk.Send("reload");

        desk.Ok("workspace", "name:gap_split_test");
        Assert.Equal("ok", desk.Send("r/keyword general:gaps_in 0"));
        Assert.Equal("ok", desk.Send("r/keyword general:border_size 0"));
        Assert.Equal("ok", desk.Send("r/keyword dwindle:split_width_multiplier 1.0"));
        Assert.Equal("ok", desk.Send("r/keyword workspace name:gap_split_test, gapsout:0 1000 0 0"));

        // The default split, force_split 0, puts B below A.
        Assert.Equal("ok", desk.Send("r/keyword dwindle:force_split 0"));
        desk.Spawn("gaps_kitty_A");
        desk.Spawn("gaps_kitty_B");

        desk.Ok("focuswindow", "class:gaps_kitty_A");
        Assert.Equal("0,0", desk.ActivePlace().At);
        desk.Ok("focuswindow", "class:gaps_kitty_B");
        Assert.Equal("0,540", desk.ActivePlace().At);

        KillAll(desk);
        Assert.Empty(desk.Clients());

        // force_split 1 puts B above A, and then C left of B.
        Assert.Equal("ok", desk.Send("r/keyword dwindle:force_split 1"));
        desk.Spawn("gaps_kitty_A");
        desk.Spawn("gaps_kitty_B");

        desk.Ok("focuswindow", "class:gaps_kitty_B");
        Assert.Equal("0,0", desk.ActivePlace().At);
        desk.Ok("focuswindow", "class:gaps_kitty_A");
        Assert.Equal("0,540", desk.ActivePlace().At);

        desk.Ok("focuswindow", "class:gaps_kitty_B");
        desk.Spawn("gaps_kitty_C");

        desk.Ok("focuswindow", "class:gaps_kitty_C");
        Assert.Equal("0,0", desk.ActivePlace().At);
        desk.Ok("focuswindow", "class:gaps_kitty_B");
        Assert.Equal("460,0", desk.ActivePlace().At);

        KillAll(desk);
        Assert.Empty(desk.Clients());

        // force_split 2 puts B below A, and then C right of A.
        Assert.Equal("ok", desk.Send("r/keyword dwindle:force_split 2"));
        desk.Spawn("gaps_kitty_A");
        desk.Spawn("gaps_kitty_B");

        desk.Ok("focuswindow", "class:gaps_kitty_A");
        Assert.Equal("0,0", desk.ActivePlace().At);
        desk.Ok("focuswindow", "class:gaps_kitty_B");
        Assert.Equal("0,540", desk.ActivePlace().At);

        desk.Ok("focuswindow", "class:gaps_kitty_A");
        desk.Spawn("gaps_kitty_C");

        desk.Ok("focuswindow", "class:gaps_kitty_A");
        Assert.Equal("0,0", desk.ActivePlace().At);
        desk.Ok("focuswindow", "class:gaps_kitty_C");
        Assert.Equal("460,0", desk.ActivePlace().At);

        KillAll(desk);
    }

    private static void DynamicWsEffects(Desk desk)
    {
        desk.Ok("workspace", "69");
        desk.Spawn("bitch");

        Assert.Equal("ok", desk.Send("r/keyword workspace 69, bordersize:20"));
        Assert.Equal("ok", desk.Send("r/keyword workspace 69, rounding:false"));

        Assert.Equal("20", desk.Send("getprop class:bitch border_size"));
        Assert.Equal("0", desk.Send("getprop class:bitch rounding"));

        Assert.Equal("ok", desk.Send("reload"));
        KillAll(desk);
    }

    // The reference adds its rules over the socket, so reload drops them and reads only the reference config.
    private static void ReloadsReferenceConfig(Desk desk) =>
        desk.Core.ReadConfig = () => new ConfigRead(ConfigParser.Parse(Desk.Config));

    private static int X(Desk desk) => desk.Placed(desk.ActiveId()).X;

    private static void ExpectActive(Desk desk, string label, string monitor)
    {
        var active = desk.ActiveWorkspace();
        Assert.Equal(label, Label(active));
        Assert.Equal(monitor, active.GetProperty("monitor").GetString());
    }

    private static string ActiveLabel(Desk desk) => Label(desk.ActiveWorkspace());

    private static string[] Labels(Desk desk) => [.. desk.Workspaces().Select(Label)];

    // A Workspace as the reference reply writes it, `ADDRESS (NAME)`: its address, then its display name.
    private static string Label(JsonElement workspace) =>
        $"{workspace.GetProperty("address").GetString()} ({workspace.GetProperty("name").GetString()})";

    private static string WorkspaceNameOf(JsonElement client) =>
        client.GetProperty("workspace").GetProperty("name").GetString() ?? "";

    private static JsonElement[] Monitors(Desk desk)
    {
        using var document = JsonDocument.Parse(desk.Send("j/monitors"));
        return [.. document.RootElement.Clone().EnumerateArray()];
    }

    private static string NameOf(JsonElement monitor) => monitor.GetProperty("name").GetString() ?? "";

    // The reference writes a Monitor's Workspace as `ID (NAME)`.
    private static string ShownOn(JsonElement monitor)
    {
        var workspace = monitor.GetProperty("activeWorkspace");
        return $"{workspace.GetProperty("id").GetInt32()} ({workspace.GetProperty("name").GetString()})";
    }

    private static string SpecialOn(JsonElement monitor) => monitor.GetProperty("specialWorkspace").GetProperty("name").GetString() ?? "";

    // Closes every window, as the reference harness kills every kitty.
    private static void KillAll(Desk desk)
    {
        foreach (var client in desk.Clients())
        {
            var address = client.GetProperty("address").GetString() ?? "";
            desk.Handle(new WindowClosed(new WindowId(Convert.ToInt64(address[2..], 16))));
        }
    }
}
