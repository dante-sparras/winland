namespace Winland.Application.Tests;

/// <summary>
/// A window that opens maximized asked for it before it mapped. Winland ignores that request, so the window is tiled.
/// </summary>
public class InitialMaximizeTests
{
    [Fact]
    public void AMaximizeRequestFromAWindowShownMaximizedIsIgnored()
    {
        var desk = new TestDesktop();
        var window = desk.Spawn("xdg-initial-maximize", shownMaximized: true);
        desk.Handle(new WindowMaximizeRequested(window));

        var client = Assert.Single(desk.Clients());
        Assert.Equal("xdg-initial-maximize", TestDesktop.ClassOf(client));
        Assert.True(client.GetProperty("mapped").GetBoolean());
        Assert.Equal(0, TestDesktop.Fullscreen(client));
        Assert.Equal(0, TestDesktop.FullscreenClient(client));
    }
}
