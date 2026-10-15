using Winland;

namespace Winland.Tests;

public class WorkspaceTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void SwitchingShowsOnlyTheTargetWorkspacesClients()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);

        var outcome = Press(core, "2");

        Assert.True(outcome.Consumed);
        Assert.Equal("workspace", outcome.Dispatcher);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(MonitorId.Primary, outcome.State.ActiveMonitor);
        Assert.Empty(outcome.Placements);
        Assert.Empty(outcome.Show);
        Assert.Equal([new WindowId(1), new WindowId(2)], outcome.Hide);
        Assert.Equal(2, outcome.State.Hidden.Count);
        Assert.All(outcome.State.Hidden, client => Assert.Equal(new WorkspaceId("1"), client.Workspace));
        Assert.Contains(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId("2") && workspace.Monitor == MonitorId.Primary);
    }

    [Fact]
    public void SwitchingBackRestoresThatWorkspacesLayout()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        Press(core, "2");
        var opened = Appear(core, 3);

        Assert.Equal(new WorkspaceId("2"), opened.State.ActiveWorkspace);
        Assert.Equal(Area, BoundsOf(opened, 3));
        Assert.Contains(opened.State.Hidden, client => client.Window == new WindowId(1));

        var back = Press(core, "1");

        Assert.Equal(new WorkspaceId("1"), back.State.ActiveWorkspace);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(back, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(back, 2));
        Assert.Equal([new WindowId(1), new WindowId(2)], back.Show);
        Assert.Equal([new WindowId(3)], back.Hide);
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(3));
    }

    [Fact]
    public void MoveToWorkspaceSendsTheActiveClientAndFollows()
    {
        var core = Load("bind = SUPER SHIFT, 2, movetoworkspace, 2");
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        core.Handle(new KeyPressed("Shift_L"));

        var outcome = Press(core, "2");

        Assert.Equal("movetoworkspace", outcome.Dispatcher);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(Area, BoundsOf(outcome, 2));
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal([new WindowId(2)], outcome.Show);
        Assert.Equal([new WindowId(1)], outcome.Hide);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void MoveToWorkspaceSilentKeepsTheCurrentWorkspace()
    {
        var core = Load("bind = SUPER ALT, 2, movetoworkspacesilent, 2");
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        core.Handle(new KeyPressed("Alt_L"));

        var outcome = Press(core, "2");

        Assert.Equal("movetoworkspacesilent", outcome.Dispatcher);
        Assert.Equal(WorkspaceId.Initial, outcome.State.ActiveWorkspace);
        Assert.Equal(Area, BoundsOf(outcome, 1));
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal([new WindowId(2)], outcome.Hide);
        Assert.Empty(outcome.Show);
        Assert.Equal([Client(outcome, 2)], outcome.State.Hidden);
    }

    [Fact]
    public void NewWindowJoinsTheActiveWorkspace()
    {
        var core = Load("""
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        HoldSuper(core);
        Press(core, "2");

        var outcome = Appear(core, 4);

        Assert.Equal(new WorkspaceId("2"), Client(outcome, 4).Workspace);
        Assert.Equal(Area, BoundsOf(outcome, 4));
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void RelativeNamedAndPreviousAddressesFollowTheManual()
    {
        var core = Load("""
            bind = SUPER, O, workspace, +1
            bind = SUPER, I, workspace, -1
            bind = SUPER, P, workspace, previous
            bind = SUPER, N, workspace, name:notes
            bind = SUPER, 1, workspace, 1
            """);
        Appear(core, 1);
        HoldSuper(core);

        var next = Press(core, "O");
        Assert.Equal(new WorkspaceId("2"), next.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1)], next.Hide);

        var third = Press(core, "O");
        Assert.Equal(new WorkspaceId("3"), third.State.ActiveWorkspace);

        var back = Press(core, "I");
        Assert.Equal(new WorkspaceId("2"), back.State.ActiveWorkspace);

        var named = Press(core, "N");
        Assert.Equal(new WorkspaceId("notes"), named.State.ActiveWorkspace);
        Assert.Equal(MonitorId.Primary, Assert.Single(named.State.Workspaces, workspace => workspace.Id == new WorkspaceId("notes")).Monitor);

        var previous = Press(core, "P");
        Assert.Equal(new WorkspaceId("2"), previous.State.ActiveWorkspace);

        var home = Press(core, "1");
        Assert.Equal(new WorkspaceId("1"), home.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1)], home.Show);
    }

    [Fact]
    public void NumericStepIncludesEmptyIdsAndMonitorStepUsesExistingWorkspaces()
    {
        var core = Load("""
            bind = SUPER SHIFT, 3, movetoworkspacesilent, 3
            bind = SUPER, R, workspace, r+1
            bind = SUPER, M, workspace, m+1
            bind = SUPER, P, workspace, previous
            """);
        Appear(core, 1);
        HoldSuper(core);
        core.Handle(new KeyPressed("Shift_L"));
        Press(core, "3");
        core.Handle(new KeyReleased("Shift_L"));

        var stepped = Press(core, "R");

        Assert.Equal(new WorkspaceId("2"), stepped.State.ActiveWorkspace);
        Assert.Contains(stepped.State.Hidden, client => client.Window == new WindowId(1) && client.Workspace == new WorkspaceId("3"));

        var home = Press(core, "P");
        Assert.Equal(new WorkspaceId("1"), home.State.ActiveWorkspace);

        var cycled = Press(core, "M");
        Assert.Equal(new WorkspaceId("3"), cycled.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1)], cycled.Show);
    }

    [Fact]
    public void EmptyAddressOpensTheFirstEmptyWorkspace()
    {
        var core = Load("bind = SUPER, E, workspace, empty");
        Appear(core, 1);
        HoldSuper(core);

        var outcome = Press(core, "E");

        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void MinimizedClientStaysMinimizedAcrossASwitch()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new WindowMinimized(new WindowId(2)));
        HoldSuper(core);
        var away = Press(core, "2");
        Assert.Contains(away.Hide, window => window == new WindowId(2));

        var back = Press(core, "1");

        Assert.Equal([new WindowId(1)], back.Show);
        Assert.DoesNotContain(back.Show, window => window == new WindowId(2));
        Assert.Equal([new WindowId(2)], back.ShowMinimized);
        Assert.True(Client(back, 2).Minimized);
        Assert.Empty(core.Handle(new WindowFocused(new WindowId(1))).Hide);
        Assert.Equal(Area, BoundsOf(back, 1));
        Assert.DoesNotContain(back.Placements, placement => placement.Client == Client(back, 2).Id);
    }

    [Fact]
    public void DispatchSwitchesTheActiveWorkspace()
    {
        var core = new Core();
        Appear(core, 1);
        Appear(core, 2);

        var outcome = core.Handle(new Dispatch("workspace", "2"));

        Assert.False(outcome.Consumed);
        Assert.Equal("workspace", outcome.Dispatcher);
        Assert.Null(outcome.Close);
        Assert.Null(outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1), new WindowId(2)], outcome.Hide);
        Assert.Empty(outcome.Show);
        Assert.All(outcome.State.Hidden, client => Assert.Equal(new WorkspaceId("1"), client.Workspace));
    }

    [Fact]
    public void DispatchMoveToWorkspaceFollowsAndSilentStays()
    {
        var core = new Core();
        Appear(core, 1);
        Appear(core, 2);

        var moved = core.Handle(new Dispatch("movetoworkspacesilent", "3"));

        Assert.False(moved.Consumed);
        Assert.Equal("movetoworkspacesilent", moved.Dispatcher);
        Assert.Equal(WorkspaceId.Initial, moved.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), Client(moved, 2).Workspace);
        Assert.Equal([new WindowId(2)], moved.Hide);
        Assert.Equal(Area, BoundsOf(moved, 1));

        var followed = core.Handle(new Dispatch("movetoworkspace", "3"));

        Assert.Equal("movetoworkspace", followed.Dispatcher);
        Assert.Equal(new WorkspaceId("3"), followed.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), Client(followed, 1).Workspace);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(followed, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(followed, 1));
        Assert.Equal([new WindowId(2), new WindowId(1)], followed.Show);
        Assert.Empty(followed.Hide);
    }

    [Fact]
    public void MoveFocusStaysInsideTheActiveWorkspace()
    {
        var core = Load("bind = SUPER, H, movefocus, l");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new Dispatch("workspace", "2"));
        Appear(core, 3);
        Appear(core, 4);
        HoldSuper(core);

        var focused = Press(core, "H");

        Assert.True(focused.Consumed);
        Assert.Equal("movefocus", focused.Dispatcher);
        Assert.Equal(new WindowId(3), focused.Focus);
        Assert.Equal(new WorkspaceId("2"), focused.State.ActiveWorkspace);
        Assert.Contains(focused.State.Hidden, client => client.Window == new WindowId(1));
        Assert.Contains(focused.State.Hidden, client => client.Window == new WindowId(2));

        var stopped = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Null(stopped.Focus);
        Assert.Equal(new WorkspaceId("2"), stopped.State.ActiveWorkspace);
        Assert.Equal(new WindowId(3), core.Handle(new Dispatch("killactive")).Close);

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(back, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(back, 2));
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(3));
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(4));
    }

    [Fact]
    public void ToggleFloatingAfterSwitchingLeavesTheOtherWorkspaceTiled()
    {
        var core = Load("bind = SUPER, Space, togglefloating");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new Dispatch("workspace", "2"));
        Appear(core, 3);
        HoldSuper(core);

        var floating = Press(core, "Space");

        Assert.True(floating.Consumed);
        Assert.Equal("togglefloating", floating.Dispatcher);
        Assert.Equal(new WorkspaceId("2"), floating.State.ActiveWorkspace);
        Assert.True(Client(floating, 3).Floating);
        Assert.False(Client(floating, 1).Floating);
        Assert.False(Client(floating, 2).Floating);
        Assert.Equal(new Rectangle(640, 340, 640, 400), BoundsOf(floating, 3));

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new WorkspaceId("1"), back.State.ActiveWorkspace);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(back, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(back, 2));
        Assert.Contains(back.Hide, window => window == new WindowId(3));
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(3) && client.Floating);
    }

    [Fact]
    public void InvalidWorkspaceAddressDoesNotSwitch()
    {
        var core = Load("""
            bind = SUPER, Z, workspace, 0
            bind = SUPER, N, workspace, name:
            """);
        Appear(core, 1);
        HoldSuper(core);

        var zero = Press(core, "Z");
        Assert.Equal("workspace", zero.Dispatcher);
        Assert.Equal(WorkspaceId.Initial, zero.State.ActiveWorkspace);
        Assert.Empty(zero.Hide);

        var blank = Press(core, "N");
        Assert.Equal(WorkspaceId.Initial, blank.State.ActiveWorkspace);
        Assert.Contains(blank.State.Clients, client => client.Window == new WindowId(1));
    }

    private static Core Load(string text)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    private static Outcome Appear(Core core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), Area));

    private static void HoldSuper(Core core) => core.Handle(new KeyPressed("Super_L"));

    private static Outcome Press(Core core, string key)
    {
        var outcome = core.Handle(new KeyPressed(key));
        core.Handle(new KeyReleased(key));
        return outcome;
    }

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }
}
