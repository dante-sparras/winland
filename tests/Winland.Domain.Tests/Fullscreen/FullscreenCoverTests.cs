namespace Winland.Domain.Tests;

public class FullscreenCoverTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void AFloatingWindowIsLoweredUnderAFullscreenTileAndRaisedWhenItEnds()
    {
        var cover = new FullscreenCover(_ => false);
        var desk = Desk();
        Tile(desk, 1);
        Float(desk, 2);
        cover.Sync([desk], [Window(2)], _ => true);

        desk.Layout.SetFullscreen(Window(1), FullscreenMode.Fullscreen, FullscreenMode.Fullscreen);
        var lowered = cover.Sync([desk], [Window(2)], _ => true);

        Assert.Equal([(Window(2), false)], lowered);
        Assert.True(cover.IsBelow(Window(2)));
        Assert.False(cover.AllowedOver(desk, desk.ClientOf(Window(2))!));

        desk.Layout.SetFullscreen(Window(1), FullscreenMode.None, FullscreenMode.None);
        var raised = cover.Sync([desk], [Window(2)], _ => true);

        Assert.Equal([(Window(2), true)], raised);
        Assert.False(cover.IsBelow(Window(2)));
    }

    [Fact]
    public void APinnedWindowStaysOverAFullscreenTile()
    {
        var cover = new FullscreenCover(window => window == Window(2));
        var desk = Desk();
        Tile(desk, 1);
        Float(desk, 2);
        desk.Layout.SetFullscreen(Window(1), FullscreenMode.Fullscreen, FullscreenMode.Fullscreen);

        var requests = cover.Sync([desk], [Window(2)], _ => true);

        Assert.Empty(requests);
        Assert.True(cover.AllowedOver(desk, desk.ClientOf(Window(2))!));
    }

    [Fact]
    public void TheFlagLetsAWindowOverAndOnlyARestackableWindowIsMoved()
    {
        var cover = new FullscreenCover(_ => false);
        var desk = Desk();
        Tile(desk, 1);
        Float(desk, 2);
        Float(desk, 3);
        desk.Layout.SetFullscreen(Window(1), FullscreenMode.Fullscreen, FullscreenMode.Fullscreen);
        cover.Sync([desk], [], _ => true);
        cover.SetFlag(Window(2), true);

        var requests = cover.Sync([desk], [Window(2), Window(3)], window => window != Window(3));

        Assert.True(cover.Flag(Window(2)));
        Assert.False(cover.Flag(Window(3)));
        Assert.DoesNotContain(requests, request => request.Window == Window(3));
    }

    [Fact]
    public void TheCoveringClientIsAFloatingFullscreenOneFirst()
    {
        var desk = Desk();
        Tile(desk, 1);
        Float(desk, 2);
        desk.Layout.SetFullscreen(Window(1), FullscreenMode.Fullscreen, FullscreenMode.Fullscreen);
        desk.Layout.SetFullscreen(Window(2), FullscreenMode.Fullscreen, FullscreenMode.Fullscreen);

        Assert.Equal(Window(2), FullscreenCover.Covering(desk)?.Window);
    }

    [Fact]
    public void AClosedWindowIsForgotten()
    {
        var cover = new FullscreenCover(_ => false);
        cover.SetFlag(Window(1), false);

        cover.Forget(Window(1));

        Assert.True(cover.Flag(Window(1)));
    }

    private static LiveWorkspace Desk()
    {
        var layout = new DwindleLayout();
        layout.Fit(Area);
        return new LiveWorkspace(WorkspaceId.Initial, MonitorId.Primary, layout);
    }

    private static void Tile(LiveWorkspace desk, long window) =>
        desk.Layout.Add(Window(window), Area, null, false, new Rectangle(0, 0, 400, 300), WorkspaceId.Initial, "app", "title", monitor: Area);

    private static void Float(LiveWorkspace desk, long window) =>
        desk.Layout.Add(Window(window), Area, null, true, new Rectangle(100, 100, 400, 300), WorkspaceId.Initial, "app", "title", monitor: Area);

    private static WindowId Window(long id) => new(id);
}
