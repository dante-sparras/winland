namespace Winland.Domain.Tests;

public class PinnedClientsTests
{
    private static readonly WindowId Window = new(1);
    private static readonly WindowId Child = new(2);

    [Fact]
    public void APinnedClientIsPinnedUntilItIsUnpinned()
    {
        var pins = new PinnedClients();

        pins.Pin(Window);
        Assert.True(pins.IsPinned(Window));

        pins.Unpin(Window);
        Assert.False(pins.IsPinned(Window));
        Assert.False(pins.IsPinnedOrHeld(Window));
    }

    [Fact]
    public void AHeldPinIsNotPinnedAndComesBackOnRelease()
    {
        var pins = new PinnedClients();
        pins.Pin(Window);

        pins.Hold(Window);
        Assert.False(pins.IsPinned(Window));
        Assert.True(pins.IsHeld(Window));
        Assert.True(pins.IsPinnedOrHeld(Window));

        pins.Release(Window);
        Assert.True(pins.IsPinned(Window));
        Assert.False(pins.IsHeld(Window));
    }

    [Fact]
    public void AClientThatIsNotPinnedHoldsNothingAndReleasesNothing()
    {
        var pins = new PinnedClients();

        pins.Hold(Window);
        pins.Release(Window);

        Assert.False(pins.IsPinnedOrHeld(Window));
    }

    [Fact]
    public void PinningAClientWithAHeldPinTakesTheHold()
    {
        var pins = new PinnedClients();
        pins.Pin(Window);
        pins.Hold(Window);

        pins.Pin(Window);

        Assert.True(pins.IsPinned(Window));
        Assert.False(pins.IsHeld(Window));
    }

    [Fact]
    public void ForgettingAClientTakesBothItsPinAndItsHeldPin()
    {
        var pins = new PinnedClients();
        pins.Pin(Window);
        pins.Pin(Child);
        pins.Hold(Child);

        pins.Forget(Window);
        pins.Forget(Child);

        Assert.False(pins.IsPinnedOrHeld(Window));
        Assert.False(pins.IsPinnedOrHeld(Child));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    public void AFloatingClientIsPinnedAtOpenWhenARulePinsItOrItsOwnerIsPinned(bool floating, bool ruled, bool ownerPinned, bool expected)
    {
        var pins = new PinnedClients();
        if (ownerPinned)
            pins.Pin(Window);

        pins.PinAtOpen(Child, floating, ruled, Window);

        Assert.Equal(expected, pins.IsPinned(Child));
    }

    [Fact]
    public void AnOwnerWithAHeldPinDoesNotPinItsChild()
    {
        var pins = new PinnedClients();
        pins.Pin(Window);
        pins.Hold(Window);

        pins.PinAtOpen(Child, floating: true, ruled: false, owner: Window);

        Assert.False(pins.IsPinned(Child));
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    public void APinnedClientStaysVisibleWhenItIsNotMinimizedAndItsMonitorShowsAWorkspace(bool pinned, bool minimized, bool monitorShows, bool expected)
    {
        var pins = new PinnedClients();
        if (pinned)
            pins.Pin(Window);

        Assert.Equal(expected, pins.StaysVisible(Window, minimized, monitorShows));
    }

    [Fact]
    public void AClientWithAHeldPinDoesNotStayVisible()
    {
        var pins = new PinnedClients();
        pins.Pin(Window);
        pins.Hold(Window);

        Assert.False(pins.StaysVisible(Window, minimized: false, monitorShowsAWorkspace: true));
    }
}
