namespace Winland.Application.Tests;

public class SpecialWorkspaceTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void AClientCanBeSentToASpecialWorkspaceAndToggled()
    {
        var core = new WindowManager();
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
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 800, 600);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 800, 0, 800, 600);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        core.Handle(new Dispatch("movetoworkspace", "special:notes"));
        var second = core.Handle(new Dispatch("focusmonitor", right.Name));
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
        var core = new WindowManager(ConfigParser.Parse("""
            windowrule = workspace special:scratch silent, class:^(quiet)$
            windowrule = workspace special, class:^(loud)$
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
                @"activespecial>>special:scratch,\\.\DISPLAY1",
                @"activespecialv2>>-98,special:scratch,\\.\DISPLAY1",
            ],
            Lines(named));
        Assert.Equal(WorkspaceId.Initial, named.State.ActiveWorkspace);

        var same = core.Handle(new Dispatch("workspace", "special"));
        Assert.Equal(WorkspaceId.DefaultSpecial, same.State.SpecialWorkspace);
        Assert.Contains(same.Hide, window => window == new WindowId(2));
        Assert.Equal(
            [
                @"createworkspace>>special:special,\\.\DISPLAY1",
                @"createworkspacev2>>-99,special:special,\\.\DISPLAY1",
                "activewindow>>,",
                "activewindowv2>>",
                @"activespecial>>special:special,\\.\DISPLAY1",
                @"activespecialv2>>-99,special:special,\\.\DISPLAY1",
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
                @"activespecial>>,\\.\DISPLAY1",
                @"activespecialv2>>,,\\.\DISPLAY1",
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
    public void TheSpecialWorkspaceHidesWhenItsLastClientLeaves()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1));
        core.Handle(new Dispatch("movetoworkspace", "special"));
        var closed = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Null(closed.State.SpecialWorkspace);
        Assert.Empty(closed.State.Clients);
    }

    [Fact]
    public void ChangingWorkspaceHidesTheSpecialWorkspace()
    {
        var core = new WindowManager();
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
        var core = new WindowManager();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(new Dispatch("movetoworkspace", "special"));

        var closed = core.Handle(new Dispatch("focuswindow", "address:0x1"));

        Assert.Null(closed.State.SpecialWorkspace);
        Assert.Equal(new WindowId(1), closed.Focus);
        Assert.Contains(closed.State.Hidden, client => client.Window == new WindowId(2));
    }

    [Fact]
    public void OpeningASpecialWorkspaceClearsTheUrgencyOfTheClientItFocuses()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));
        core.Handle(new Dispatch("movetoworkspace", "special:scratch"));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new WindowFlashed(new WindowId(3)));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        // Client 3 is no longer urgent, so the history gives the Client focused before the one focused just now.
        Assert.Equal(new WindowId(2), outcome.Focus);
    }

    [Fact]
    public void ASilentMoveDoesNotShowTheSpecialWorkspace()
    {
        var core = new WindowManager();
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

    private static MonitorSnapshot Screen(Outcome outcome, int id) =>
        Assert.Single(outcome.State.Screens, screen => screen.Id == new MonitorId(id));

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
