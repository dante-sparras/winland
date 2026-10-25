using System.Globalization;
using System.Text.Json;

namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's workspace cases. Each test is named after its case, and sends the same commands.</summary>
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

        desk.Ok("workspace", "1");

        // Each Monitor shows its own Workspace 1. The new one does not take a number from this Monitor.
        desk.AddMonitor("HEADLESS-3", Desk.Second);
        var monitors = Monitors(desk);
        Assert.Equal(2, monitors.Count(monitor => ShownOn(monitor) == "1 (1)"));
        Assert.Contains("HEADLESS-3", monitors.Select(NameOf));

        desk.Ok("focusmonitor", "HEADLESS-2");
        Assert.Equal("1 (1)", ActiveLabel(desk));

        // r+1 is the next number on this Monitor. Workspace 3 still holds a window, and 2 is free.
        desk.Ok("workspace", "r+1");
        Assert.Equal("2 (2)", ActiveLabel(desk));

        desk.Ok("workspace", "1");
        desk.Ok("workspace", "r~2");
        Assert.Equal("2 (2)", ActiveLabel(desk));

        // m cycles the Workspaces on this Monitor: 1, 2, and 3.
        desk.Ok("workspace", "m+1");
        Assert.Equal("3 (3)", ActiveLabel(desk));

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

        Assert.Empty(desk.Clients());
    }

    // The reference changes a Workspace's id with its change_id dispatcher, which has no line-based name.
    // changeworkspaceid stands for it here.
    [Fact]
    public void WorkspaceRenameChangeID()
    {
        var desk = new Desk();
        desk.Ok("workspace", "150");
        desk.Spawn("a");

        desk.Ok("workspace", "100");
        Assert.Equal("100 (100)", ActiveLabel(desk));

        desk.Ok("changeworkspaceid", "100 101");
        Assert.Equal("101 (101)", ActiveLabel(desk));

        foreach (var bad in new[] { "101 -1", "101 abc", "101", "102 200", "101 150", "name:coce 105" })
            Assert.NotEqual("ok", desk.Dispatch("changeworkspaceid", bad));
        Assert.Equal("101 (101)", ActiveLabel(desk));

        desk.Ok("changeworkspaceid", "101 102");
        Assert.Equal("102 (102)", ActiveLabel(desk));
    }

    [Fact]
    public void WorkspaceChangeIdEmitsTheMonitor()
    {
        var desk = new Desk();
        desk.Ok("workspace", "5201");
        desk.Spawn("workspace_rename_event");

        var reply = desk.WindowManager.Execute("dispatch changeworkspaceid 5201 5202");
        Assert.Equal("ok", reply.Text);
        var events = CommandProtocol.FormatStream(reply.Effect.Events, reply.Effect.State);
        Assert.Equal(["changeworkspaceid>>5201,5202,HEADLESS-2"], events.Where(line => line.StartsWith("changeworkspaceid>>", StringComparison.Ordinal)));
        Assert.Equal("5202 (5202)", ActiveLabel(desk));

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

    private static void MultimonBaf(Desk desk)
    {
        desk.Keyword("binds:workspace_back_and_forth", "0");

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "1");
        desk.Spawn("a");
        desk.Ok("workspace", "2");
        desk.Spawn("a");

        desk.Ok("focusmonitor", "HEADLESS-3");
        desk.Ok("workspace", "3");
        desk.Spawn("a");

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Keyword("binds:workspace_back_and_forth", "1");

        // previous is global. Workspace 2 again returns to Workspace 3, which HEADLESS-3 is showing.
        desk.Ok("workspace", "2");
        ExpectActive(desk, "3 (3)", "HEADLESS-3");

        desk.Ok("workspace", "4");
        desk.Ok("workspace", "5");
        desk.Ok("workspace", "5");
        ExpectActive(desk, "4 (4)", "HEADLESS-3");

        desk.Keyword("binds:workspace_back_and_forth", "2");

        // per_monitor stays on this Monitor. Asking again for 4 returns to 5.
        desk.Ok("workspace", "4");
        ExpectActive(desk, "5 (5)", "HEADLESS-3");

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "6");
        desk.Ok("workspace", "6");
        ExpectActive(desk, "2 (2)", "HEADLESS-2");

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

        desk.Ok("workspace", "8");
        Assert.Equal("ok", desk.Send("r/keyword general:gaps_in 0"));
        Assert.Equal("ok", desk.Send("r/keyword general:border_size 0"));
        Assert.Equal("ok", desk.Send("r/keyword dwindle:split_width_multiplier 1.0"));
        Assert.Equal("ok", desk.Send("r/keyword workspace 8, gapsout:0 1000 0 0"));

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

    // The reference adds its rules over the socket, so reload drops them and reads only the reference config.
    private static void ReloadsReferenceConfig(Desk desk) =>
        desk.WindowManager.ReadConfig = () => new ConfigRead(ConfigParser.Parse(Desk.Config));

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
