namespace Winland.Application.Tests;

/// <summary>A tiled Client resized by its own border changes its split, as a <c>resizewindow</c> drag does.</summary>
public class NativeResizeTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void DraggingATiledBorderChangesTheSplit()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);

        var started = core.Handle(new WindowSizeStarted(new WindowId(1), new Point(960, 540)));
        var ended = core.Handle(new WindowSizeEnded(new WindowId(1), new Point(1060, 540)));

        // The system resizes the window while the border is held, so the core leaves it alone until the end.
        Assert.Empty(started.Placements);
        Assert.Equal(new Rectangle(0, 0, 1060, 1080), BoundsOf(ended, 1));
        Assert.Equal(new Rectangle(1060, 0, 860, 1080), BoundsOf(ended, 2));
    }

    // A client's resize request is a live resizewindow drag in Winland, so the neighbour follows
    // the border while it is held. The system sizes the window itself, so the core does not place it.
    [Fact]
    public void TheNeighbourFollowsTheBorderWhileItIsHeld()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);

        core.Handle(new WindowSizeStarted(new WindowId(1), new Point(960, 540)));
        var step = core.Handle(new WindowSizing(new WindowId(1), new Point(1060, 540)));

        Assert.True(step.Live);
        Assert.Empty(step.Motions);
        Assert.DoesNotContain(step.Placements, placement => placement.Client == new ClientId(1));
        Assert.Equal(new Rectangle(1060, 0, 860, 1080), BoundsOf(step, 2));

        var ended = core.Handle(new WindowSizeEnded(new WindowId(1), new Point(1100, 540)));

        Assert.False(ended.Live);
        Assert.Equal(new Rectangle(0, 0, 1100, 1080), BoundsOf(ended, 1));
        Assert.Equal(new Rectangle(1100, 0, 820, 1080), BoundsOf(ended, 2));
    }

    // The system sizes the window to the pointer, which would cover a neighbour that cannot shrink any further. The
    // window is held at the tile the Layout gives it instead, and only while the pointer is past it.
    [Fact]
    public void ABorderHeldPastANeighboursMinimumKeepsTheWindowAtItsTile()
    {
        var core = Flat("");
        Appear(core, 1);
        core.Handle(new WindowAppeared(new WindowId(2), Wide) { Traits = WindowTraits.Normal with { MinSize = new Size(600, 0) } });

        core.Handle(new WindowSizeStarted(new WindowId(1), new Point(960, 540), new Rectangle(0, 0, 960, 1080)));
        var within = core.Handle(new WindowSizing(new WindowId(1), new Point(1200, 540), new Rectangle(0, 0, 1200, 1080)));
        var past = core.Handle(new WindowSizing(new WindowId(1), new Point(1500, 540), new Rectangle(0, 0, 1500, 1080)));

        Assert.DoesNotContain(within.Placements, placement => placement.Client == new ClientId(1));
        Assert.Equal(new Rectangle(0, 0, 1320, 1080), BoundsOf(past, 1));
        Assert.Equal(new Rectangle(1320, 0, 600, 1080), BoundsOf(past, 2));
    }

    [Fact]
    public void ASizingStepWithoutASizeChangesNothing()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);

        var step = core.Handle(new WindowSizing(new WindowId(1), new Point(1060, 540)));

        Assert.Empty(step.Placements);
        Assert.False(step.Live);
    }

    [Fact]
    public void AResizeThatChangesNothingSendsTheClientBackToItsTile()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);

        core.Handle(new WindowSizeStarted(new WindowId(2), new Point(1500, 1079)));
        var ended = core.Handle(new WindowSizeEnded(new WindowId(2), new Point(1500, 1079)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(ended, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(ended, 2));
    }

    [Fact]
    public void ResizingAMaximizedClientSendsItBackToCoveringTheArea()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new WindowMaximizeRequested(new WindowId(2)));

        core.Handle(new WindowSizeStarted(new WindowId(2), new Point(1919, 540)));
        var ended = core.Handle(new WindowSizeEnded(new WindowId(2), new Point(1500, 540)));

        Assert.Equal(Wide, BoundsOf(ended, 2));
        Assert.Equal(FullscreenMode.Maximized, Client(ended, 2).Fullscreen);
    }

    [Fact]
    public void AFloatingClientKeepsTheSizeItWasGiven()
    {
        var core = Flat("");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));

        var started = core.Handle(new WindowSizeStarted(new WindowId(1), new Point(900, 700)));
        var ended = core.Handle(new WindowSizeEnded(new WindowId(1), new Point(950, 750)));

        Assert.Empty(started.Placements);
        Assert.Empty(ended.Placements);
        Assert.True(Client(ended, 1).Floating);
    }

    [Fact]
    public void ASizeEventForAnUnknownWindowChangesNothing()
    {
        var core = Flat("");
        Appear(core, 1);

        Assert.Empty(core.Handle(new WindowSizeStarted(new WindowId(99), new Point(10, 10))).Placements);
        Assert.Empty(core.Handle(new WindowSizeEnded(new WindowId(99), new Point(20, 20))).Placements);
    }

    // Letting go recenters a scrolling tape narrower than the screen, so the end places the Client where the
    // Layout now holds it, not where the last step of the drag left it.
    [Fact]
    public void TheEndPlacesTheClientWhereTheLayoutHoldsItAfterTheRelease()
    {
        var core = Flat("general:layout = scrolling\nscrolling:column_width = 0.3\n");
        Appear(core, 1);
        Appear(core, 2);

        core.Handle(new WindowSizeStarted(new WindowId(2), new Point(1535, 540)));
        var ended = core.Handle(new WindowSizeEnded(new WindowId(2), new Point(1435, 540)));

        Assert.Equal(Client(ended, 2).Bounds, BoundsOf(ended, 2));
    }

    // A bottom border changes only the height, as on a window Winland does not manage. The pointer drifting sideways
    // while it is held leaves the split beside the Client where it was.
    [Fact]
    public void ABottomBorderChangesOnlyTheHeight()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);
        Appear(core, 3);
        var tile = new Rectangle(960, 0, 960, 540);

        core.Handle(new WindowSizeStarted(new WindowId(2), new Point(1400, 540), tile));
        var step = core.Handle(new WindowSizing(new WindowId(2), new Point(1200, 640), tile with { Height = 640 }));
        var ended = core.Handle(new WindowSizeEnded(new WindowId(2), new Point(1200, 640), tile with { Height = 640 }));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(step, 1));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(ended, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 640), BoundsOf(ended, 2));
        Assert.Equal(new Rectangle(960, 640, 960, 440), BoundsOf(ended, 3));
    }

    private static WindowManager Flat(string extra)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra)));
        return core;
    }

    private static Outcome Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), Wide));
}
