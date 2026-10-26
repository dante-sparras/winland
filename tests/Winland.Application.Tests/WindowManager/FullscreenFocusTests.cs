namespace Winland.Application.Tests;

/// <summary>On a workspace with a maximized or fullscreen Client, that Client owns the pointer.</summary>
public class FullscreenFocusTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    public void ThePointerOverACoveredTileDoesNotFocusIt(string mode)
    {
        var core = Desk();
        Move(core, new Point(1500, 500), 1_000);
        core.Handle(new Dispatch("fullscreen", mode));
        Assert.Equal("\"address\":\"0x2\"", Address(core));

        var over = Move(core, new Point(300, 500), 1_100);

        Assert.Null(over.Focus);
        Assert.DoesNotContain(over.Events, item => item is FocusChanged);
        Assert.Equal("\"address\":\"0x2\"", Address(core));
    }

    [Fact]
    public void AMaximizedClientTakesTheFocusWhenThePointerIsOverIt()
    {
        var core = Desk();
        core.Handle(new Dispatch("focuswindow", "address:0x1"));
        core.Handle(new WindowMaximizeRequested(new WindowId(2)));
        Assert.Equal("\"address\":\"0x1\"", Address(core));

        Move(core, new Point(300, 500), 1_000);
        var over = Move(core, new Point(400, 520), 1_100);

        Assert.Equal(new WindowId(2), over.Focus);
    }

    [Fact]
    public void AClientThatCoversItsMonitorByItselfOwnsThePointer()
    {
        var core = Desk();
        Move(core, new Point(1500, 500), 1_000);

        var covered = core.Handle(new WindowFullscreenRequested(new WindowId(2), true));
        var over = Move(core, new Point(300, 500), 1_100);

        Assert.Equal(FullscreenMode.Fullscreen, Client(covered, 2).Fullscreen);
        Assert.Null(over.Focus);
        Assert.Equal("\"address\":\"0x2\"", Address(core));
    }

    [Fact]
    public void AClientThatLeavesItsOwnFullscreenGoesBackToItsTile()
    {
        var core = Desk();
        Move(core, new Point(1500, 500), 1_000);
        core.Handle(new WindowFullscreenRequested(new WindowId(2), true));

        var left = core.Handle(new WindowFullscreenRequested(new WindowId(2), false));

        Assert.Equal(FullscreenMode.None, Client(left, 2).Fullscreen);
        Assert.Contains(left.Placements, placement => placement.Client == Client(left, 2).Id && placement.Bounds.Width < Area.Width);
        Assert.Equal(new WindowId(1), Move(core, new Point(300, 500), 1_100).Focus);
    }

    [Fact]
    public void AClientThatGoesFullscreenOrLeavesItByItselfIsPlacedWithoutAMotion()
    {
        var core = new WindowManager(ConfigParser.Parse("general:border_size = 0\nanimation = global, 1, 5, default\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var entered = core.Handle(new WindowFullscreenRequested(new WindowId(2), true));
        var left = core.Handle(new WindowFullscreenRequested(new WindowId(2), false));

        Assert.Contains(entered.Placements, placement => placement.Client == Client(entered, 2).Id);
        Assert.Contains(left.Placements, placement => placement.Client == Client(left, 2).Id);
        Assert.DoesNotContain(entered.Motions, motion => motion.Window == new WindowId(2) && motion.Move is not null);
        Assert.DoesNotContain(left.Motions, motion => motion.Window == new WindowId(2) && motion.Move is not null);
    }

    [Fact]
    public void AWindowThatLeavesAFullscreenItNeverHadChangesNothing()
    {
        var core = Desk();
        core.Handle(new Dispatch("fullscreen", "1"));

        var left = core.Handle(new WindowFullscreenRequested(new WindowId(2), false));

        Assert.Equal(FullscreenMode.Maximized, Client(left, 2).Fullscreen);
        Assert.Empty(left.Placements);
    }

    [Fact]
    public void WithNothingCoveringTheTilesStillFollowThePointer()
    {
        var core = Desk();
        Move(core, new Point(1500, 500), 1_000);

        var over = Move(core, new Point(300, 500), 1_100);

        Assert.Equal(new WindowId(1), over.Focus);
    }

    private static WindowManager Desk()
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        return core;
    }

    private static Outcome Move(WindowManager core, Point point, long time) =>
        core.Handle(new PointerMoved(point) { TimeMs = time });

    private static Client Client(Outcome outcome, long window) =>
        outcome.State.Clients.Single(client => client.Window == new WindowId(window));

    private static string Address(WindowManager core)
    {
        var text = core.Execute("j/activewindow").Text;
        var at = text.IndexOf("\"address\":", StringComparison.Ordinal);
        Assert.True(at >= 0, text);
        return text[at..(at + "\"address\":\"0x0\"".Length)];
    }
}
