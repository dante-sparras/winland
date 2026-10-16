using Winland;

namespace Winland.Tests;

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

    // A client's resize request is a live resizewindow drag in the pinned release, so the neighbour follows
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
        return Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id).Bounds;
    }
}
