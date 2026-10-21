
namespace Winland.Application.Tests;

public class UrgencyTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void AFlashingClientIsReportedUrgentAndKeepsFocusWhereItIs()
    {
        var core = TwoClients();

        var flashed = core.Handle(new WindowFlashed(new WindowId(1)));

        Assert.Equal(["urgent>>1"], Lines(flashed));
        Assert.Null(flashed.Focus);
    }

    [Fact]
    public void TheFocusedClientFlashingIsNotUrgent()
    {
        var core = TwoClients();

        var flashed = core.Handle(new WindowFlashed(new WindowId(2)));

        Assert.Empty(flashed.Events);
    }

    [Fact]
    public void AnUnmanagedWindowFlashingIsNotUrgent()
    {
        var core = TwoClients();

        var flashed = core.Handle(new WindowFlashed(new WindowId(99)));

        Assert.Empty(flashed.Events);
    }

    [Fact]
    public void FocusUrgentOrLastFocusesTheUrgentClient()
    {
        var core = ThreeClients();
        core.Handle(new WindowFlashed(new WindowId(1)));

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        Assert.Null(outcome.Error);
        Assert.Equal(new WindowId(1), outcome.Focus);
    }

    [Fact]
    public void FocusUrgentOrLastFallsBackToTheClientFocusedBefore()
    {
        var core = ThreeClients();

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        Assert.Equal(new WindowId(2), outcome.Focus);
    }

    [Fact]
    public void FocusingTheUrgentClientClearsTheUrgency()
    {
        var core = ThreeClients();
        core.Handle(new WindowFlashed(new WindowId(1)));
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));
        core.Handle(new WindowFocused(new WindowId(3), FocusCause.User));

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        Assert.Equal(new WindowId(1), outcome.Focus);
        core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));
        Assert.Equal(new WindowId(3), core.Handle(new Dispatch("focusurgentorlast")).Focus);
    }

    [Fact]
    public void ARefusedActivationLeavesTheClientUrgent()
    {
        var core = ThreeClients();
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.Activated));

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        Assert.Equal(new WindowId(1), outcome.Focus);
    }

    [Fact]
    public void AClosedUrgentClientIsForgotten()
    {
        var core = ThreeClients();
        core.Handle(new WindowFlashed(new WindowId(1)));
        core.Handle(new WindowClosed(new WindowId(1)));

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        // The history left, oldest first, is 2 and 3, and Winland takes the second: the focused Client itself.
        Assert.Equal(new WindowId(3), outcome.Focus);
    }

    [Fact]
    public void WithNoUrgentClientTheSecondOldestInTheHistoryIsFocused()
    {
        var core = ThreeClients();
        core.Handle(new WindowFocused(new WindowId(1)));
        core.Handle(new WindowFocused(new WindowId(2)));

        // Oldest first the history is 3, 1, 2, so Winland goes to 1, not to 3, the one focused just before.
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("focusurgentorlast")).Focus);
    }

    [Fact]
    public void FocusCurrentOrLastGoesBackToTheClientFocusedBefore()
    {
        var core = ThreeClients();
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));
        core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));

        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("focuscurrentorlast")).Focus);
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));
        Assert.Equal(new WindowId(2), core.Handle(new Dispatch("focuscurrentorlast")).Focus);
    }

    [Fact]
    public void FocusCurrentOrLastWithAShortHistoryIsAnError()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        Assert.Equal("error: History too short", core.Handle(new Dispatch("focuscurrentorlast")).Error);
    }

    [Fact]
    public void FocusUrgentOrLastWithNothingToFocusIsAnError()
    {
        var core = new WindowManager();

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        Assert.Equal("error: window not found", outcome.Error);
    }

    [Fact]
    public void AnUrgentClientOnAnotherWorkspaceIsShown()
    {
        var core = TwoClients();
        core.Handle(new Dispatch("movetoworkspacesilent", "2,address:0x1"));
        core.Handle(new WindowFlashed(new WindowId(1)));

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
    }

    private static WindowManager TwoClients()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        return core;
    }

    private static WindowManager ThreeClients()
    {
        var core = TwoClients();
        core.Handle(new WindowAppeared(new WindowId(3), Area));
        return core;
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
