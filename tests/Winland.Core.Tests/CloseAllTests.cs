using Winland.Core;

namespace Winland.Core.Tests;

public class CloseAllTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void CloseAllIsKnown() => Assert.True(Dispatchers.IsKnown("closeall"));

    [Fact]
    public void CloseAllAsksEveryClientToCloseAndShowsWorkspaceOne()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(Appeared(3));
        core.Handle(new Dispatch("workspace", "1"));
        core.Handle(Appeared(2));
        core.Handle(new Dispatch("movetoworkspace", "special:scratch"));

        var closed = core.Handle(new Dispatch("closeall", ""));

        Assert.Equal("closeall", closed.Dispatcher);
        Assert.Null(closed.Error);
        Assert.Empty(closed.Kills);
        Assert.Equal(
            [new WindowId(1), new WindowId(2), new WindowId(3)],
            closed.Closes.OrderBy(window => window.Value).ToArray());
        Assert.Equal(WorkspaceId.Initial, closed.State.ActiveWorkspace);
        Assert.Null(closed.State.SpecialWorkspace);
        Assert.Contains(closed.Hide, window => window == new WindowId(2));
        Assert.Contains(closed.State.Clients, client => client.Window == new WindowId(3));
    }

    [Fact]
    public void NoCloseForLeavesThatClientOpen()
    {
        long now = 1_000;
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = no_close_for 500, class:^(stick)$\n")) { RuleClock = () => now };
        core.Handle(Appeared(1, "stick"));
        core.Handle(Appeared(2, "box"));

        var closed = core.Handle(new Dispatch("closeall", ""));

        Assert.Null(closed.Error);
        Assert.Equal([new WindowId(2)], closed.Closes);
        Assert.Equal(WorkspaceId.Initial, closed.State.ActiveWorkspace);
    }

    [Fact]
    public void CloseAllShowsWorkspaceOneOfTheFocusedMonitor()
    {
        var core = new WindowManager();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080));
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY2"));

        var closed = core.Handle(new Dispatch("closeall", ""));

        Assert.Equal(new MonitorId(1), closed.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), closed.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), Workspace(closed, WorkspaceId.FromNumber(1, new MonitorId(1))).Monitor);
        Assert.Contains(closed.Closes, window => window == new WindowId(1));
        Assert.Contains(closed.Closes, window => window == new WindowId(2));
    }

    private static WindowAppeared Appeared(long window, string? windowClass = null) => new(new WindowId(window), Area)
    {
        Traits = windowClass is null ? WindowTraits.Normal : WindowTraits.Normal with { Class = windowClass },
    };

    private static Workspace Workspace(Outcome outcome, WorkspaceId id) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == id);
}
