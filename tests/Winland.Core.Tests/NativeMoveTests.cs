using Winland;

namespace Winland.Tests;

/// <summary>A Client moved by its own title bar is dragged like a <c>movewindow</c> drag: lifted, then dropped where the pointer ends.</summary>
public class NativeMoveTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void DroppingATiledClientOnTheLeftHalfPutsItLeftOfTheTileUnderThePointer()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);

        var started = core.Handle(new WindowMoveStarted(new WindowId(2), new Point(1000, 100)));
        var dropped = core.Handle(new WindowMoveEnded(new WindowId(2), new Point(100, 500)));

        // The system is moving the lifted window, so the core leaves it alone and lets the other tile grow.
        Assert.Equal(Wide, BoundsOf(started, 1));
        Assert.DoesNotContain(started.Placements, placement => placement.Client == Client(started, 2).Id);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(dropped, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(dropped, 1));
        Assert.False(Client(dropped, 2).Floating);
    }

    [Fact]
    public void DroppingWhereItWasSendsTheClientBackToItsPlace()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);

        core.Handle(new WindowMoveStarted(new WindowId(2), new Point(1000, 100)));
        var dropped = core.Handle(new WindowMoveEnded(new WindowId(2), new Point(1010, 120)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(dropped, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(dropped, 2));
    }

    [Fact]
    public void AFloatingClientStaysWhereItIsDropped()
    {
        var core = Flat("");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));

        var started = core.Handle(new WindowMoveStarted(new WindowId(1), new Point(700, 400)));
        var dropped = core.Handle(new WindowMoveEnded(new WindowId(1), new Point(900, 500)));

        Assert.Empty(started.Placements);
        Assert.Empty(dropped.Placements);
        Assert.True(Client(dropped, 1).Floating);
    }

    [Fact]
    public void AMonocleClientGoesBackToFillingTheArea()
    {
        var core = Flat("general:layout = monocle\n");
        Appear(core, 1);
        Appear(core, 2);

        core.Handle(new WindowMoveStarted(new WindowId(2), new Point(500, 500)));
        var dropped = core.Handle(new WindowMoveEnded(new WindowId(2), new Point(100, 900)));

        Assert.Equal(Wide, BoundsOf(dropped, 2));
        Assert.False(Client(dropped, 2).Floating);
    }

    [Fact]
    public void MasterDropsTheClientOntoTheMasterWhenThePointerEndsOverIt()
    {
        var core = Flat("general:layout = master\n");
        Appear(core, 1);
        Appear(core, 2);

        core.Handle(new WindowMoveStarted(new WindowId(2), new Point(1500, 500)));
        var dropped = core.Handle(new WindowMoveEnded(new WindowId(2), new Point(200, 500)));

        Assert.True(BoundsOf(dropped, 2).X < BoundsOf(dropped, 1).X);
        Assert.False(Client(dropped, 2).Floating);
    }

    [Fact]
    public void ScrollingTilesTheClientAgainAfterTheDrop()
    {
        var core = Flat("general:layout = scrolling\n");
        Appear(core, 1);
        Appear(core, 2);

        core.Handle(new WindowMoveStarted(new WindowId(2), new Point(1500, 500)));
        var dropped = core.Handle(new WindowMoveEnded(new WindowId(2), new Point(300, 500)));

        Assert.False(Client(dropped, 2).Floating);
        Assert.Contains(dropped.Placements, placement => placement.Client == Client(dropped, 2).Id);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    public void MovingAMaximizedOrFullscreenClientLeavesThatModeAndDropsItBackIn(string mode)
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new Dispatch("focuswindow", "address:0x2"));
        core.Handle(new Dispatch("fullscreen", mode));

        var started = core.Handle(new WindowMoveStarted(new WindowId(2), new Point(900, 20)));

        Assert.Equal(FullscreenMode.None, Client(started, 2).Fullscreen);
        Assert.Equal(Wide, BoundsOf(started, 1));
        Assert.DoesNotContain(started.Placements, placement => placement.Client == Client(started, 2).Id);

        var dropped = core.Handle(new WindowMoveEnded(new WindowId(2), new Point(100, 500)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(dropped, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(dropped, 1));
        Assert.Equal(FullscreenMode.None, Client(dropped, 2).Fullscreen);
    }

    [Fact]
    public void AMoveOfAnUnknownWindowChangesNothing()
    {
        var core = Flat("");
        Appear(core, 1);

        var started = core.Handle(new WindowMoveStarted(new WindowId(99), new Point(10, 10)));
        var ended = core.Handle(new WindowMoveEnded(new WindowId(99), new Point(20, 20)));

        Assert.Empty(started.Placements);
        Assert.Empty(ended.Placements);
    }

    [Fact]
    public void AClientThatClosesWhileTheSystemMovesItDoesNotBlockTheNextMove()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);
        Appear(core, 3);
        core.Handle(new WindowMoveStarted(new WindowId(3), new Point(1500, 100)));
        core.Handle(new WindowClosed(new WindowId(3)));

        var started = core.Handle(new WindowMoveStarted(new WindowId(2), new Point(1500, 100)));

        Assert.Equal(Wide, BoundsOf(started, 1));
    }

    [Fact]
    public void AClientThatClosesWhileTheSystemResizesItDoesNotBlockTheNextResize()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new WindowSizeStarted(new WindowId(1), new Point(960, 500)));
        core.Handle(new WindowClosed(new WindowId(1)));
        Appear(core, 3);

        core.Handle(new WindowSizeStarted(new WindowId(2), new Point(960, 500)));
        var ended = core.Handle(new WindowSizeEnded(new WindowId(2), new Point(900, 500)));

        Assert.NotEmpty(ended.Placements);
    }

    [Fact]
    public void AClientThatClosesDuringAMouseDragEndsTheDrag()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new PointerGesture("movewindow", new Point(1500, 100), PointerDragPhase.Begin, "mouse:272"));
        core.Handle(new WindowClosed(new WindowId(2)));

        var moved = core.Handle(new PointerGesture("movewindow", new Point(1400, 200), PointerDragPhase.Move, "mouse:272"));
        var started = core.Handle(new WindowMoveStarted(new WindowId(1), new Point(500, 100)));

        Assert.Empty(moved.Placements);
        Assert.Null(moved.Dispatcher);
        Assert.Equal(new WindowId(1), Assert.Single(started.State.Clients).Window);
        Assert.False(Assert.Single(core.Handle(new WindowMoveEnded(new WindowId(1), new Point(500, 100))).State.Clients).Floating);
    }

    [Fact]
    public void AClientThatClosesDuringABorderResizeEndsTheResize()
    {
        var core = Flat("general:resize_on_border = true\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        var bounds = Client(core.Handle(new Dispatch("no_op")), 1).Bounds;
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(bounds.X + bounds.Width + 5, bounds.Y + 50) });
        core.Handle(new WindowClosed(new WindowId(1)));

        var moved = core.Handle(new PointerMoved(new Point(bounds.X + bounds.Width + 40, bounds.Y + 60)));

        Assert.False(moved.Consumed);
        Assert.Null(moved.Dispatcher);
    }

    private static Core Flat(string extra)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra)));
        return core;
    }

    private static Outcome Appear(Core core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), Wide));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
