namespace Winland.Application.Tests.Conformance;

/// <summary>
/// Winland's initial maximize cases. Its client asks to be maximized before the window first shows, once
/// before its first commit and once before it maps. On Windows both are a window shown maximized, which Winland
/// finds in the system's maximized state the first time the window moves, and takes as a maximize request.
/// </summary>
[Trait("Category", "Conformance")]
public class XdgInitialMaximizeConformance
{
    [Fact]
    public void XdgMaximizeBeforeInitialCommitIsIgnored() => InitialMaximizeIsIgnored();

    [Fact]
    public void XdgMaximizeBeforeMapIsIgnored() => InitialMaximizeIsIgnored();

    private static void InitialMaximizeIsIgnored()
    {
        var desk = new Desk();
        var window = desk.Spawn("xdg-initial-maximize", shownMaximized: true);
        desk.Handle(new WindowMaximizeRequested(window));

        var client = Assert.Single(desk.Clients());
        Assert.Equal("xdg-initial-maximize", Desk.ClassOf(client));
        Assert.True(client.GetProperty("mapped").GetBoolean());
        Assert.False(client.GetProperty("xwayland").GetBoolean());
        Assert.Equal(0, Desk.Fullscreen(client));
        Assert.Equal(0, Desk.FullscreenClient(client));
    }
}
