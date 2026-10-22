namespace Winland.Infrastructure.Tests;

public class TrayMessageTests
{
    [Fact]
    public void OnlyTheCallbackIsAllowedWhenTheBroadcastIsNotRegistered()
    {
        Assert.Equal([TrayMessages.Callback], TrayMessages.Allowed(0));
    }

    [Fact]
    public void TheCallbackAndTaskbarCreatedAreAllowedTogether()
    {
        const uint taskbarCreated = 0xC000;

        var allowed = TrayMessages.Allowed(taskbarCreated);

        Assert.Contains(TrayMessages.Callback, allowed);
        Assert.Contains(taskbarCreated, allowed);
        Assert.Equal(2, allowed.Length);
    }
}
