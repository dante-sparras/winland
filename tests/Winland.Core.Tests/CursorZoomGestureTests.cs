using Winland;

namespace Winland.Tests;

/// <summary>The cursorZoom gesture changes the cursor zoom as Winland's does.</summary>
public class CursorZoomGestureTests
{
    [Fact]
    public void MultMultipliesTheZoomEachTime()
    {
        var core = Loaded("gesture = 3, swipe, cursorZoom, 2, mult\n");

        Assert.Equal(2, Swipe(core).Cursor!.Zoom.Factor);
        Assert.Equal(4, Swipe(core).Cursor!.Zoom.Factor);
    }

    [Fact]
    public void LiveFollowsThePinchAtOnceAndSnapsToNoZoomNearOne()
    {
        var core = Loaded("gesture = 2, pinch, cursorZoom, 1, live\n");
        core.Handle(new PinchBegan(2));

        var spread = core.Handle(new PinchUpdated(2, 0, 0, 1.5));
        var nearly = core.Handle(new PinchUpdated(2, 0, 0, 1.02));

        Assert.Equal(1.5, spread.Cursor!.Zoom.Factor, 6);
        Assert.Null(spread.Cursor.Zoom.Animation);
        Assert.Equal(1, nearly.Cursor!.Zoom.Factor);
    }

    [Fact]
    public void ReadingTheConfigDropsTheGesturesZoom()
    {
        var config = "gesture = 3, swipe, cursorZoom, 3\n";
        var core = Loaded(config);
        Swipe(core);

        var reloaded = core.Handle(new Configure(ConfigParser.Parse(config)));

        Assert.Equal(1, reloaded.Cursor!.Zoom.Factor);
    }

    private static Outcome Swipe(Core core)
    {
        core.Handle(new SwipeBegan(3));
        for (var i = 0; i < 5; i++)
            core.Handle(new SwipeUpdated(3, 10, 0));
        return core.Handle(new SwipeEnded());
    }

    private static Core Loaded(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 1920, 1080)));
        return core;
    }
}
