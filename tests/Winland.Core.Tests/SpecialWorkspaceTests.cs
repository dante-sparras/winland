using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class SpecialWorkspaceTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void AClientCanBeSentToASpecialWorkspaceAndToggled()
    {
        var core = new Core();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var moved = core.Handle(new Dispatch("movetoworkspace", "special:scratch"));

        Assert.Equal(WorkspaceId.Initial, moved.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("special:scratch"), Client(moved, 2).Workspace);
        Assert.Equal(WorkspaceId.Initial, Client(moved, 1).Workspace);
        Assert.DoesNotContain(moved.State.Hidden, client => client.Window == new WindowId(1));
        Assert.DoesNotContain(moved.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new WorkspaceId("special:scratch"), moved.State.SpecialWorkspace);
        Assert.Equal(new WindowId(2), moved.Focus);

        var closed = core.Handle(new Dispatch("togglespecialworkspace", "scratch"));

        Assert.Equal(WorkspaceId.Initial, closed.State.ActiveWorkspace);
        Assert.Null(closed.State.SpecialWorkspace);
        Assert.Contains(closed.Hide, window => window == new WindowId(2));
        Assert.Contains(closed.State.Hidden, client => client.Window == new WindowId(2));
        Assert.DoesNotContain(closed.State.Hidden, client => client.Window == new WindowId(1));
        Assert.Equal(new WindowId(1), closed.Focus);

        var opened = core.Handle(new Dispatch("togglespecialworkspace", "scratch"));

        Assert.Equal(WorkspaceId.Initial, opened.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("special:scratch"), opened.State.SpecialWorkspace);
        Assert.Contains(opened.Show, window => window == new WindowId(2));
        Assert.DoesNotContain(opened.State.Hidden, client => client.Window == new WindowId(1));
        Assert.DoesNotContain(opened.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new WindowId(2), opened.Focus);
    }

    [Fact]
    public void OneSpecialWorkspaceIsShownPerMonitor()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 800, 600);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 800, 0, 800, 600);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        core.Handle(new Dispatch("movetoworkspace", "special:notes"));
        var second = core.Handle(new Dispatch("workspace", "2"));
        Assert.Equal(new MonitorId(1), second.State.ActiveMonitor);
        var other = core.Handle(new Dispatch("movetoworkspace", "special:term"));

        Assert.Equal(new WorkspaceId("special:notes"), Screen(other, 0).SpecialWorkspace);
        Assert.Equal(new WorkspaceId("special:term"), Screen(other, 1).SpecialWorkspace);
        Assert.DoesNotContain(other.State.Hidden, client => client.Window == new WindowId(1));
        Assert.DoesNotContain(other.State.Hidden, client => client.Window == new WindowId(2));

        var replaced = core.Handle(new Dispatch("togglespecialworkspace", "notes"));

        Assert.Null(Screen(replaced, 0).SpecialWorkspace);
        Assert.Equal(new WorkspaceId("special:notes"), Screen(replaced, 1).SpecialWorkspace);
        Assert.Contains(replaced.State.Hidden, client => client.Window == new WindowId(2));
        Assert.DoesNotContain(replaced.State.Hidden, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void AddressesRulesAndEventsCoverSpecial()
    {
        var core = new Core(ConfigParser.Parse("""
            windowrulev2 = workspace special:scratch silent, class:^(quiet)$
            windowrulev2 = workspace special, class:^(loud)$
            """));
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 800, 600);
        core.Handle(new MonitorsChanged([display]));
        core.Handle(Appeared(1));

        var silent = core.Handle(Appeared(2, "quiet"));
        Assert.Equal(new WorkspaceId("special:scratch"), Client(silent, 2).Workspace);
        Assert.Null(silent.State.SpecialWorkspace);
        Assert.Contains(silent.Hide, window => window == new WindowId(2));
        Assert.DoesNotContain(Lines(silent), line => line.StartsWith("activespecial", StringComparison.Ordinal));

        var named = core.Handle(new Dispatch("workspace", "special:scratch"));
        Assert.Equal(
            [
                "activewindow>>quiet,",
                "activewindowv2>>2",
                "activespecial>>special:scratch,\\\\.\\DISPLAY1",
                "activespecialv2>>-98,special:scratch,\\\\.\\DISPLAY1",
            ],
            Lines(named));
        Assert.Equal(WorkspaceId.Initial, named.State.ActiveWorkspace);

        var same = core.Handle(new Dispatch("workspace", "special"));
        Assert.Equal(WorkspaceId.DefaultSpecial, same.State.SpecialWorkspace);
        Assert.Contains(same.Hide, window => window == new WindowId(2));
        Assert.Equal(
            [
                "createworkspace>>special:special",
                "createworkspacev2>>-99,special:special",
                "activewindow>>,",
                "activewindowv2>>",
                "activespecial>>special:special,\\\\.\\DISPLAY1",
                "activespecialv2>>-99,special:special,\\\\.\\DISPLAY1",
            ],
            Lines(same));

        var loud = core.Handle(Appeared(3, "loud"));
        Assert.Equal(WorkspaceId.DefaultSpecial, Client(loud, 3).Workspace);
        Assert.Equal(WorkspaceId.DefaultSpecial, loud.State.SpecialWorkspace);

        var closed = core.Handle(new Dispatch("togglespecialworkspace", ""));
        Assert.Equal(
            [
                "activewindow>>,",
                "activewindowv2>>1",
                "activespecial>>,\\\\.\\DISPLAY1",
                "activespecialv2>>,,\\\\.\\DISPLAY1",
            ],
            Lines(closed));
        Assert.Null(closed.State.SpecialWorkspace);

        var monitors = Json(core.Execute("j/monitors").Text);
        Assert.False(monitors[0].GetProperty("specialWorkspace").TryGetProperty("id", out _));
        Assert.Equal("", monitors[0].GetProperty("specialWorkspace").GetProperty("name").GetString());

        core.Handle(new Dispatch("togglespecialworkspace", ""));
        var open = Json(core.Execute("j/monitors").Text);
        Assert.False(open[0].GetProperty("specialWorkspace").TryGetProperty("id", out _));
        Assert.Equal("special", open[0].GetProperty("specialWorkspace").GetProperty("type").GetString());
        Assert.Equal("special:special", open[0].GetProperty("specialWorkspace").GetProperty("name").GetString());
        Assert.Equal(1, open[0].GetProperty("activeWorkspace").GetProperty("id").GetInt32());
    }

    [Fact]
    public void CloseOnEmptyHidesTheSpecialWorkspaceUnlessToldNotTo()
    {
        var core = new Core();
        core.Handle(Appeared(1));
        core.Handle(new Dispatch("movetoworkspace", "special"));
        var closed = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Null(closed.State.SpecialWorkspace);
        Assert.Empty(closed.State.Clients);

        var kept = new Core(ConfigParser.Parse("misc:close_special_on_empty = false\n"));
        kept.Handle(Appeared(1));
        kept.Handle(new Dispatch("movetoworkspace", "special"));
        var stayed = kept.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(WorkspaceId.DefaultSpecial, stayed.State.SpecialWorkspace);
        Assert.Empty(stayed.State.Clients);
    }

    [Fact]
    public void ChangingWorkspaceHidesTheSpecialWorkspaceWhenAsked()
    {
        var core = new Core(ConfigParser.Parse("binds:hide_special_on_workspace_change = true\n"));
        core.Handle(Appeared(1));
        core.Handle(new Dispatch("movetoworkspace", "special:scratch"));
        var changed = core.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(new WorkspaceId("2"), changed.State.ActiveWorkspace);
        Assert.Null(changed.State.SpecialWorkspace);
        Assert.Contains(changed.State.Hidden, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void FallthroughKeepsTheSpecialWorkspaceOpenWhenFocusLeavesIt()
    {
        var core = new Core();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(new Dispatch("movetoworkspace", "special"));

        var closed = core.Handle(new Dispatch("focuswindow", "address:0x1"));

        Assert.Null(closed.State.SpecialWorkspace);
        Assert.Equal(new WindowId(1), closed.Focus);
        Assert.Contains(closed.State.Hidden, client => client.Window == new WindowId(2));

        var through = new Core(ConfigParser.Parse("input:special_fallthrough = true\n"));
        through.Handle(Appeared(1));
        through.Handle(Appeared(2));
        through.Handle(new Dispatch("movetoworkspace", "special"));
        var stayed = through.Handle(new Dispatch("focuswindow", "address:0x1"));

        Assert.Equal(WorkspaceId.DefaultSpecial, stayed.State.SpecialWorkspace);
        Assert.Equal(new WindowId(1), stayed.Focus);
        Assert.DoesNotContain(stayed.State.Hidden, client => client.Window == new WindowId(2));
    }

    [Fact]
    public void TogglingASpecialWorkspaceWarpsToItsClient()
    {
        var core = new Core(ConfigParser.Parse("cursor:warp_on_toggle_special = 1\n"));
        core.Handle(Appeared(1));
        core.Handle(new Dispatch("movetoworkspacesilent", "special"));

        var opened = core.Handle(new Dispatch("togglespecialworkspace", ""));

        Assert.Equal(new WarpTo(new Point(400, 300)), opened.Warp);
    }

    [Fact]
    public void SpecialKeywordsAreHonored()
    {
        var document = ConfigParser.Parse("""
            misc:close_special_on_empty = false
            binds:hide_special_on_workspace_change = true
            input:special_fallthrough = true
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(3, document.Settings.Count);
    }

    [Fact]
    public void ASilentMoveDoesNotShowTheSpecialWorkspace()
    {
        var core = new Core();
        core.Handle(Appeared(1));
        var moved = core.Handle(new Dispatch("movetoworkspacesilent", "special:scratch"));

        Assert.Null(moved.State.SpecialWorkspace);
        Assert.Equal(new WorkspaceId("special:scratch"), Client(moved, 1).Workspace);
        Assert.Contains(moved.State.Hidden, client => client.Window == new WindowId(1));
        Assert.Equal(WorkspaceId.Initial, moved.State.ActiveWorkspace);
    }

    private static WindowAppeared Appeared(long window, string? windowClass = null) => new(new WindowId(window), Area)
    {
        Traits = windowClass is null ? WindowTraits.Normal : WindowTraits.Normal with { Class = windowClass },
    };

    private static Monitor Display(string name, string description, int x, int y, int width, int height) =>
        new(name, description, x, y, width, height, 1, true, new Rectangle(x, y, width, height));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static MonitorSnapshot Screen(Outcome outcome, int id) =>
        Assert.Single(outcome.State.Screens, screen => screen.Id == new MonitorId(id));

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
