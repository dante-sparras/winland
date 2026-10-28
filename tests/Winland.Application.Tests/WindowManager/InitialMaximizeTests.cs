namespace Winland.Application.Tests;

/// <summary>
/// A window shown maximized before it first shows. Winland finds the system's maximized state the first time the window
/// moves, and takes it as a maximize request.
/// </summary>
public class InitialMaximizeTests
{
    [Fact]
    public void XdgMaximizeBeforeInitialCommitIsIgnored() => InitialMaximizeIsIgnored();

    [Fact]
    public void XdgMaximizeBeforeMapIsIgnored() => InitialMaximizeIsIgnored();

    private static void InitialMaximizeIsIgnored()
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
