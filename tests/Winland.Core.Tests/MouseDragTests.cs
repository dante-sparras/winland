using Winland;

namespace Winland.Tests;

public class MouseDragTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void FloatingMoveFollowsThePointer()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(10, 20) });

        var moved = core.Handle(new PointerMoved(new Point(60, 50)));

        Assert.Equal("movewindow", moved.Dispatcher);
        // The float toggle recenters the 640x400 default, then the pointer moves it by (+50, +30).
        Assert.Equal(new Rectangle(690, 370, 640, 400), BoundsOf(moved, 1));
        Assert.True(Client(moved, 1).Floating);
    }

    [Fact]
    public void TiledDragDropsTheClientIntoTheTreeUnderThePointer()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1000, 100) });
        core.Handle(new PointerMoved(new Point(100, 500)));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(100, 500) });

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(dropped, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(dropped, 1));
        Assert.False(Client(dropped, 2).Floating);
    }

    [Fact]
    public void FloatingResizeGrowsFromTheGrabbedCorner()
    {
        var core = Flat("bindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1910, 1070) });

        var resized = core.Handle(new PointerMoved(new Point(1950, 1100)));

        Assert.Equal("resizewindow", resized.Dispatcher);
        // The recentered 640x400 float grows by the pointer delta (+40, +30).
        Assert.Equal(new Rectangle(640, 340, 680, 430), BoundsOf(resized, 1));
    }

    [Fact]
    public void TiledResizeChangesTheSplitRatio()
    {
        var core = Flat("bindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(960, 540) });

        var resized = core.Handle(new PointerMoved(new Point(1060, 540)));

        Assert.Equal(new Rectangle(0, 0, 1060, 1080), BoundsOf(resized, 1));
        Assert.Equal(new Rectangle(1060, 0, 860, 1080), BoundsOf(resized, 2));
    }

    private static Core Flat(string binds)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + binds)));
        return core;
    }

    private static void Appear(Core core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), Wide));

    private static void Press(Core core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
