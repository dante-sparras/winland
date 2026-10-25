namespace Winland.Application.Tests;

public class GeometryDispatcherTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly Rectangle Centered = new(860, 465, 200, 150);

    [Fact]
    public void TheDispatcherIsKnown() => Assert.True(Dispatchers.IsKnown("centerwindow"));

    [Fact]
    public void CenterWindowPutsTheActiveFloatBackInTheMiddleOfTheWorkArea()
    {
        var core = OneFloat();
        Place(core, 1, new Rectangle(0, 0, 200, 150));

        var outcome = core.Handle(new Dispatch("centerwindow"));

        Assert.Equal(Centered, BoundsOf(outcome, 1));
    }

    [Fact]
    public void CenterWindowRejectsATiledClient()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new Dispatch("centerwindow"));

        Assert.Equal("error: No floating window found", outcome.Error);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void AWindowAfterACommaIsActedOnInPlaceOfTheActiveOne()
    {
        var core = TwoFloats();
        Place(core, 1, new Rectangle(0, 0, 200, 150));

        var centered = core.Handle(new Dispatch("centerwindow", ",address:0x1"));
        Assert.Equal(Centered, BoundsOf(centered, 1));

        var full = core.Handle(new Dispatch("fullscreen", "0,address:0x1"));
        Assert.Equal(FullscreenMode.Fullscreen, Assert.Single(full.State.Clients, client => client.Window == new WindowId(1)).Fullscreen);
        Assert.Equal(FullscreenMode.None, Assert.Single(full.State.Clients, client => client.Window == new WindowId(2)).Fullscreen);
    }

    [Fact]
    public void AWindowThatMatchesNothingIsAnError()
    {
        var core = TwoFloats();

        var outcome = core.Handle(new Dispatch("centerwindow", ",address:0x9"));

        Assert.Equal("error: window not found", outcome.Error);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void ABindPassesTheWindowAfterTheComma()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("""
            windowrule = float, class:^(box)$
            bind = SUPER, Z, togglegroup, ,address:0x1
            """)));
        core.Handle(Box(1));
        core.Handle(Box(2));

        core.Handle(new KeyPressed("Super_L"));
        core.Handle(new KeyPressed("Z"));

        Assert.Contains("\"grouped\":[\"0x1\"]", core.Execute("j/clients").Text, StringComparison.Ordinal);
    }

    private static WindowManager OneFloat()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, class:^(box)$\n"));
        core.Handle(Box(1));
        return core;
    }

    private static WindowManager TwoFloats()
    {
        var core = OneFloat();
        core.Handle(Box(2));
        return core;
    }

    private static WindowAppeared Box(long window) =>
        new(new WindowId(window), Area)
        {
            Traits = WindowTraits.Normal with { Class = "box" },
            Bounds = new Rectangle(5, 7, 200, 150),
        };
}
