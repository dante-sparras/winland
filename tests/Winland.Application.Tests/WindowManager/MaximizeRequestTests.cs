
namespace Winland.Application.Tests;

/// <summary>An application's maximize request is maximize mode 1. Asking again leaves it.</summary>
public class MaximizeRequestTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void AMaximizeRequestFillsTheWorkAreaAndAskingAgainGoesBack()
    {
        var core = Flat();
        Appear(core, 1);
        Appear(core, 2);

        var maximized = core.Handle(new WindowMaximizeRequested(new WindowId(2)));

        Assert.Equal(FullscreenMode.Maximized, Client(maximized, 2).Fullscreen);
        Assert.Equal(Wide, BoundsOf(maximized, 2));

        var left = core.Handle(new WindowMaximizeRequested(new WindowId(2)));

        Assert.Equal(FullscreenMode.None, Client(left, 2).Fullscreen);
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(left, 2));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(left, 1));
    }

    [Fact]
    public void ARequestFromAWindowThatIsNotFocusedMaximizesThatWindow()
    {
        var core = Flat();
        Appear(core, 1);
        Appear(core, 2);

        var maximized = core.Handle(new WindowMaximizeRequested(new WindowId(1)));

        Assert.Equal(FullscreenMode.Maximized, Client(maximized, 1).Fullscreen);
        Assert.Equal(FullscreenMode.None, Client(maximized, 2).Fullscreen);
    }

    [Fact]
    public void AWindowThatShowedMaximizedIsTiledAndItsLaterRequestsAreHonored()
    {
        var core = Flat();
        Appear(core, 1);
        core.Handle(new WindowAppeared(new WindowId(2), Wide) { ShownMaximized = true });

        var ignored = core.Handle(new WindowMaximizeRequested(new WindowId(2)));

        Assert.Equal(FullscreenMode.None, Client(ignored, 2).Fullscreen);
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(ignored, 2));

        var maximized = core.Handle(new WindowMaximizeRequested(new WindowId(2)));

        Assert.Equal(FullscreenMode.Maximized, Client(maximized, 2).Fullscreen);
    }

    [Fact]
    public void ARequestFromAnUnknownWindowChangesNothing()
    {
        var core = Flat();
        Appear(core, 1);

        var outcome = core.Handle(new WindowMaximizeRequested(new WindowId(99)));

        Assert.Empty(outcome.Placements);
        Assert.Equal(FullscreenMode.None, Client(outcome, 1).Fullscreen);
    }

    [Fact]
    public void AWindowThatRestoredASizeOfItsOwnIsPutBackOnItsTile()
    {
        var core = Flat();
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new WindowMaximizeRequested(new WindowId(2)));
        core.Handle(new WindowMaximizeRequested(new WindowId(2)));

        var drifted = core.Handle(new WindowDrifted(new WindowId(2), new Rectangle(400, 200, 1528, 850)));

        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(drifted, 2));
        Assert.Empty(drifted.Motions);
    }

    [Fact]
    public void AWindowAlreadyOnItsTileIsLeftAlone()
    {
        var core = Flat();
        Appear(core, 1);
        Appear(core, 2);

        var drifted = core.Handle(new WindowDrifted(new WindowId(2), new Rectangle(960, 0, 960, 1080)));

        Assert.Empty(drifted.Placements);
    }

    private static WindowManager Flat()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n")));
        return core;
    }

    private static Outcome Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), Wide));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id).Bounds;
    }
}
