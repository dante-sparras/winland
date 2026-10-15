using Winland;

namespace Winland.Tests;

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

        Assert.Equal(new WindowId(2), outcome.Focus);
    }

    [Fact]
    public void FocusUrgentOrLastWithNothingToFocusIsAnError()
    {
        var core = new Core();

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

    private static Core TwoClients()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        return core;
    }

    private static Core ThreeClients()
    {
        var core = TwoClients();
        core.Handle(new WindowAppeared(new WindowId(3), Area));
        return core;
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
