namespace Winland.Domain.Tests;

public class FocusStateTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void TakingFocusIsRememberedByTheWorkspace()
    {
        var focus = new FocusState(_ => true);
        var desk = Desk(1, 2);

        focus.Take(Window(1), desk);

        Assert.Equal(Window(1), focus.Focused);
        Assert.Equal(Window(1), desk.Focus);
        Assert.Equal(Window(1), focus.Remembered(desk));
    }

    [Fact]
    public void FocusWithoutAWorkspaceLeavesEveryWorkspaceAsItWas()
    {
        var focus = new FocusState(_ => true);
        var desk = Desk(1, 2);
        focus.Take(Window(1), desk);

        focus.Take(Window(2));

        Assert.Equal(Window(2), focus.Focused);
        Assert.Equal(Window(1), desk.Focus);
    }

    [Fact]
    public void AWorkspaceDoesNotGiveFocusBackToAWindowThatTakesNone()
    {
        var focus = new FocusState(window => window != Window(2));
        var desk = Desk(1, 2);
        FocusState.Remember(desk, Window(2));

        Assert.Null(focus.Remembered(desk));
    }

    [Fact]
    public void AWorkspaceWithNothingRememberedGivesFocusToItsLastVisibleClient()
    {
        var focus = new FocusState(_ => true);
        var desk = Desk(1, 2);

        Assert.Equal(desk.LastVisible(), focus.Remembered(desk));
    }

    [Fact]
    public void TheHistoryIsNewestFirstAndRanksRecency()
    {
        var focus = new FocusState(_ => true);
        focus.Confirm(Window(1));
        focus.Confirm(Window(2));
        focus.Confirm(Window(1));

        Assert.Equal([Window(1), Window(2)], focus.History);
        Assert.True(focus.Recency(Window(1)) > focus.Recency(Window(2)));
        Assert.Equal(0, focus.Recency(Window(3)));
    }

    [Fact]
    public void RecentIsTheNewestOnTheWorkspaceThatCanTakeFocus()
    {
        var focus = new FocusState(window => window != Window(2));
        var desk = Desk(1, 2);
        focus.Confirm(Window(1));
        focus.Confirm(Window(9));
        focus.Confirm(Window(2));

        Assert.Equal(Window(1), focus.Recent(desk));
    }

    [Fact]
    public void AnUnmanagedWindowThatBecomesAClientHandsBackThePriorFocusOnce()
    {
        var focus = new FocusState(_ => true);
        focus.Take(Window(1));
        focus.UnmanagedTook(Window(5));

        Assert.Equal(Window(1), focus.Adopted(Window(5)));
        Assert.Null(focus.Adopted(Window(5)));
    }

    [Fact]
    public void AClosedWindowLeavesTheHistoryAndTheUnmanagedMarks()
    {
        var focus = new FocusState(_ => true);
        focus.Confirm(Window(1));
        focus.Take(Window(1));
        focus.UnmanagedTook(Window(5));

        focus.Forget(Window(1));
        focus.Forget(Window(5));

        Assert.Empty(focus.History);
        Assert.Null(focus.Unmanaged);
        Assert.Null(focus.BeforeUnmanaged);
    }

    [Fact]
    public void AfterLeavingTheOneRevealedWindowTakesFocusAndOtherwiseTheLastVisible()
    {
        var desk = Desk(1, 2, 3);

        Assert.Equal(Window(2), FocusState.AfterLeaving(desk, [Window(2)]));
        Assert.Equal(desk.LastVisible(), FocusState.AfterLeaving(desk, []));
        Assert.Equal(desk.LastVisible(), FocusState.AfterLeaving(desk, [Window(1), Window(2)]));
    }

    private static LiveWorkspace Desk(params long[] windows)
    {
        var layout = new DwindleLayout();
        layout.Fit(Area);
        foreach (var window in windows)
            layout.Add(Window(window), Area, null, false, new Rectangle(0, 0, 400, 300), WorkspaceId.Initial, "app", "title", monitor: Area);

        return new LiveWorkspace(WorkspaceId.Initial, MonitorId.Primary, layout);
    }

    private static WindowId Window(long id) => new(id);
}
