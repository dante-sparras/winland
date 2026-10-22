namespace Winland.Domain.Tests;

public class ShownWorkspacesTests
{
    private static readonly MonitorId Left = new(0);
    private static readonly MonitorId Right = new(1);

    private static WorkspaceId Number(long number, MonitorId monitor) => WorkspaceId.FromNumber(number, monitor);

    [Fact]
    public void AMonitorThatJustBecameLiveHasNoShownWorkspaceButStillAnswersWhatItShowedLast()
    {
        var shown = new ShownWorkspaces();
        shown.Show(Left, Number(3, Left));

        shown.SetLive([Left]);

        Assert.Null(shown.Shown(Left));
        Assert.Equal(Number(3, Left), shown.ActiveOn(Left));
    }

    [Fact]
    public void AShowOnALiveMonitorIsItsShownWorkspace()
    {
        var shown = new ShownWorkspaces();
        shown.SetLive([Left, Right]);

        shown.Show(Left, Number(2, Left));

        Assert.Equal(Number(2, Left), shown.Shown(Left));
        Assert.Null(shown.Shown(Right));
        Assert.Equal(WorkspaceId.Initial, shown.ActiveOn(Right));
    }

    [Fact]
    public void AMonitorThatLeavesKeepsWhatItShowedLast()
    {
        var shown = new ShownWorkspaces();
        shown.SetLive([Left, Right]);
        shown.Show(Right, Number(4, Right));

        shown.SetLive([Left]);

        Assert.Null(shown.Shown(Right));
        Assert.Equal(Number(4, Right), shown.LastShown(Right));
        Assert.Equal(Number(4, Right), shown.ActiveOn(Right));
    }

    [Fact]
    public void ASpecialWorkspaceShowsOverALiveMonitorUntilItCloses()
    {
        var shown = new ShownWorkspaces();
        shown.SetLive([Left]);
        var scratch = new WorkspaceId("special:scratch");

        shown.ShowSpecial(Left, scratch);
        var open = shown.SpecialOn(Left);
        shown.ShowSpecial(Left, null);

        Assert.Equal(scratch, open);
        Assert.Null(shown.SpecialOn(Left));
    }

    [Fact]
    public void ARenameMovesTheShownWorkspaceAndItsVisits()
    {
        var shown = new ShownWorkspaces();
        shown.SetLive([Left]);
        shown.Show(Left, Number(1, Left));
        shown.Visit(Number(2, Left), Left, allowCycles: false);
        shown.Visit(Number(1, Left), Left, allowCycles: false);

        shown.Rename(Number(2, Left), Number(7, Left));

        Assert.Equal(Number(7, Left), shown.Previous(Left, perMonitor: false));
    }

    [Fact]
    public void ARemovedWorkspaceIsNoLongerShownOrVisited()
    {
        var shown = new ShownWorkspaces();
        shown.SetLive([Left]);
        shown.Visit(Number(2, Left), Left, allowCycles: false);
        shown.Visit(Number(1, Left), Left, allowCycles: false);
        shown.Show(Left, Number(1, Left));

        shown.Remove(Number(1, Left));

        Assert.Null(shown.Shown(Left));
        Assert.Equal(WorkspaceId.Initial, shown.ActiveOn(Left));
    }

    [Fact]
    public void PreviousPerMonitorSkipsVisitsOnOtherMonitors()
    {
        var shown = new ShownWorkspaces();
        shown.SetLive([Left, Right]);
        shown.Visit(Number(5, Left), Left, allowCycles: false);
        shown.Visit(Number(1, Right), Right, allowCycles: false);
        shown.Visit(Number(1, Left), Left, allowCycles: false);
        shown.Show(Left, Number(1, Left));

        Assert.Equal(Number(1, Right), shown.Previous(Left, perMonitor: false));
        Assert.Equal(Number(5, Left), shown.Previous(Left, perMonitor: true));
    }

    [Fact]
    public void WithoutCyclesTheSameWorkspaceTwiceIsOneVisit()
    {
        var shown = new ShownWorkspaces();
        shown.SetLive([Left]);
        shown.Visit(Number(2, Left), Left, allowCycles: false);
        shown.Visit(Number(1, Left), Left, allowCycles: false);
        shown.Visit(Number(1, Left), Left, allowCycles: false);
        shown.Show(Left, Number(1, Left));

        Assert.Equal(Number(2, Left), shown.Previous(Left, perMonitor: false));
    }

    [Fact]
    public void OnlyWhatAnEnabledMonitorShowsIsShown()
    {
        var shown = new ShownWorkspaces();
        shown.SetLive([Left, Right], enabled: [Left]);
        shown.Show(Left, Number(1, Left));
        shown.Show(Right, Number(1, Right));

        Assert.True(shown.IsShown(Number(1, Left)));
        Assert.False(shown.IsShown(Number(1, Right)));
        Assert.True(shown.ShowsAWorkspace(Left));
        Assert.False(shown.ShowsAWorkspace(Right));
    }

    [Fact]
    public void ASpecialWorkspaceIsShownOverTheEnabledMonitorThatOpensIt()
    {
        var shown = new ShownWorkspaces();
        var scratch = new WorkspaceId("special:scratch");
        shown.SetLive([Left, Right]);
        shown.ShowSpecial(Right, scratch);

        Assert.Equal(Right, shown.ShowingSpecial(scratch));
        Assert.True(shown.IsShown(scratch));

        shown.SetLive([Left, Right], enabled: [Left]);
        Assert.Null(shown.ShowingSpecial(scratch));
    }

    [Fact]
    public void WithNoMonitorReportedThePrimaryShowsWhatItShowedLast()
    {
        var shown = new ShownWorkspaces();
        shown.Show(MonitorId.Primary, Number(3, MonitorId.Primary));

        Assert.True(shown.IsShown(Number(3, MonitorId.Primary)));
        Assert.False(shown.IsShown(Number(4, MonitorId.Primary)));
        Assert.True(shown.ShowsAWorkspace(Left));
    }
}
