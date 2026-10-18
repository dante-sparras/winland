using Winland;

namespace Winland.Tests;

public class ZoomCameraTests
{
    private static readonly Rectangle Screen = new(0, 0, 2000, 1000);

    [Fact]
    public void ZoomKeysAreHonored()
    {
        var options = CursorOptions.From(ConfigParser.Parse("""
            cursor {
                zoom_factor = 20
                zoom_rigid = true
                zoom_detached_camera = false
            }
            """));

        Assert.Equal(new CursorZoom(10, true, false), options.Behavior().Zoom);
        Assert.Equal(1, CursorOptions.From(ConfigParser.Parse("cursor:zoom_factor = 0.5\n")).ZoomFactor);
        Assert.Equal(new CursorZoom(1, false, true), CursorOptions.From(ConfigParser.Parse("")).Behavior().Zoom);
    }

    [Fact]
    public void NoZoomShowsNothing()
    {
        var camera = new ZoomCamera();

        Assert.Null(camera.Follow(Screen, new Point(500, 500), new CursorZoom(1, false, true)));
    }

    [Fact]
    public void DetachedCameraKeepsThePointUnderTheCursor()
    {
        var camera = new ZoomCamera();

        Assert.Equal(new ZoomView(2, 250, 250), camera.Follow(Screen, new Point(500, 500), new CursorZoom(2, false, true)));
    }

    [Fact]
    public void DetachedCameraPansOnlyPastItsMiddle()
    {
        var camera = new ZoomCamera();
        var zoom = new CursorZoom(2, false, true);
        _ = camera.Follow(Screen, new Point(500, 500), zoom);

        Assert.Equal(new ZoomView(2, 250, 250), camera.Follow(Screen, new Point(1100, 600), zoom));
        Assert.Equal(new ZoomView(2, 350, 250), camera.Follow(Screen, new Point(1300, 600), zoom));
    }

    [Fact]
    public void RigidDetachedCameraDoesNotPan()
    {
        var camera = new ZoomCamera();
        var zoom = new CursorZoom(2, true, true);
        _ = camera.Follow(Screen, new Point(500, 500), zoom);

        Assert.Equal(new ZoomView(2, 250, 250), camera.Follow(Screen, new Point(1300, 600), zoom));
    }

    [Fact]
    public void AttachedCameraZoomsAboutTheCursor()
    {
        var camera = new ZoomCamera();

        Assert.Equal(new ZoomView(2, 250, 250), camera.Follow(Screen, new Point(500, 500), new CursorZoom(2, false, false)));
        Assert.Equal(new ZoomView(2, 0, 250), camera.Follow(Screen, new Point(500, 500), new CursorZoom(2, true, false)));
    }

    [Fact]
    public void ViewStaysOnTheMonitor()
    {
        var camera = new ZoomCamera();

        Assert.Equal(new ZoomView(2, 0, 0), camera.Follow(Screen, new Point(100, 100), new CursorZoom(2, true, false)));
        Assert.Equal(new ZoomView(4, 1500, 750), camera.Follow(Screen, new Point(1999, 999), new CursorZoom(4, true, false)));
    }

    [Fact]
    public void ViewIsInDesktopPixels()
    {
        var camera = new ZoomCamera();
        var right = new Rectangle(2000, 0, 2000, 1000);

        Assert.Equal(new ZoomView(2, 2250, 250), camera.Follow(right, new Point(2500, 500), new CursorZoom(2, false, true)));
    }

    [Fact]
    public void CameraStartsOverAfterTheCursorLeavesItsMonitor()
    {
        var camera = new ZoomCamera();
        var right = new Rectangle(2000, 0, 2000, 1000);
        _ = camera.Follow(Screen, new Point(500, 500), new CursorZoom(2, false, true));
        _ = camera.Follow(right, new Point(2500, 500), new CursorZoom(2, false, true));

        Assert.Equal(new ZoomView(3, 0, 0), camera.Follow(Screen, new Point(0, 0), new CursorZoom(3, false, true)));
    }
}
