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
    }

    [Fact]
    public void ForgettingAClientTakesItsPin()
    {
        var pins = new PinnedClients();
        pins.Pin(Window);

        pins.Forget(Window);

        Assert.False(pins.IsPinned(Window));
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
}
